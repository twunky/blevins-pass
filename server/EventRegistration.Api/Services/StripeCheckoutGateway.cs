using System.Net;
using Stripe;
using Stripe.Checkout;

namespace EventRegistration.Api.Services;

/// <summary>
/// Real Stripe-hosted Checkout in test mode. The secret key comes from configuration
/// ("Stripe:SecretKey", i.e. the environment variable Stripe__SecretKey) and never leaves the server.
/// </summary>
public class StripeCheckoutGateway(IConfiguration configuration) : ICheckoutGateway
{
    public async Task<CheckoutSessionSnapshot> CreateSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            PaymentMethodTypes = ["card"],
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() },
            ExpiresAt = request.ExpiresUtc,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            LineItems = request.LineItems.Select(item => new SessionLineItemOptions
            {
                Quantity = item.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = request.Currency,
                    UnitAmount = item.UnitAmountCents,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = item.Name },
                },
            }).ToList(),
        };

        try
        {
            var session = await CreateSessionService().CreateAsync(
                options,
                new RequestOptions { IdempotencyKey = request.IdempotencyKey },
                cancellationToken);
            return ToSnapshot(session);
        }
        catch (StripeException exception) when (IsDefinitiveRejection(exception))
        {
            throw new CheckoutRejectedException($"Stripe rejected the checkout request ({exception.StripeError?.Code ?? exception.HttpStatusCode.ToString()}).");
        }
        catch (Exception exception) when (exception is StripeException or HttpRequestException or TaskCanceledException)
        {
            throw new CheckoutUncertainException("Stripe did not confirm whether the checkout session was created.", exception);
        }
    }

    public async Task<CheckoutSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await CreateSessionService().GetAsync(sessionId, cancellationToken: cancellationToken);
            return ToSnapshot(session);
        }
        catch (Exception exception) when (exception is StripeException or HttpRequestException or TaskCanceledException)
        {
            throw new CheckoutUncertainException("Could not retrieve the checkout session from Stripe.", exception);
        }
    }

    public async Task<CheckoutSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await CreateSessionService().ExpireAsync(sessionId, cancellationToken: cancellationToken);
            return ToSnapshot(session);
        }
        catch (StripeException exception) when (exception.HttpStatusCode == HttpStatusCode.BadRequest)
        {
            // Stripe refuses to expire a session that is already complete or expired; re-read its real state.
            return await GetSessionAsync(sessionId, cancellationToken);
        }
        catch (Exception exception) when (exception is StripeException or HttpRequestException or TaskCanceledException)
        {
            throw new CheckoutUncertainException("Could not expire the checkout session at Stripe.", exception);
        }
    }

    public async Task<PaymentRefundSnapshot> RefundSessionPaymentAsync(string sessionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        try
        {
            var session = await new SessionService(client).GetAsync(sessionId, cancellationToken: cancellationToken);
            if (string.IsNullOrEmpty(session.PaymentIntentId))
            {
                throw new CheckoutUncertainException("The checkout session has no payment to refund.");
            }

            var paymentIntent = await new PaymentIntentService(client).GetAsync(session.PaymentIntentId, cancellationToken: cancellationToken);
            if (paymentIntent.Livemode)
            {
                throw new CheckoutConfigurationException("Refusing to refund a live-mode payment.");
            }

            var before = await ReadRefundStateAsync(client, paymentIntent, cancellationToken);
            var remaining = before.AmountReceivedCents - before.AmountRefundedCents;
            if (remaining > 0 && !before.HasPendingRefund)
            {
                await new RefundService(client).CreateAsync(
                    new RefundCreateOptions
                    {
                        PaymentIntent = paymentIntent.Id,
                        Amount = remaining,
                        Metadata = new Dictionary<string, string> { ["reason"] = "blevinspass-demo-reset" },
                    },
                    new RequestOptions { IdempotencyKey = idempotencyKey },
                    cancellationToken);
            }

            // Success is decided by re-reading Stripe, not by the create call having been accepted.
            return await ReadRefundStateAsync(client, paymentIntent, cancellationToken);
        }
        catch (Exception exception) when (exception is StripeException or HttpRequestException or TaskCanceledException)
        {
            throw new CheckoutUncertainException("Could not complete or verify the refund at Stripe.", exception);
        }
    }

    private static async Task<PaymentRefundSnapshot> ReadRefundStateAsync(StripeClient client, PaymentIntent paymentIntent, CancellationToken cancellationToken)
    {
        var refunds = await new RefundService(client).ListAsync(
            new RefundListOptions { PaymentIntent = paymentIntent.Id, Limit = 100 },
            cancellationToken: cancellationToken);
        return new PaymentRefundSnapshot(
            PaymentIntentId: paymentIntent.Id,
            IsTestMode: !paymentIntent.Livemode,
            AmountReceivedCents: paymentIntent.AmountReceived,
            AmountRefundedCents: refunds.Data.Where(r => r.Status == "succeeded").Sum(r => r.Amount),
            HasPendingRefund: refunds.Data.Any(r => r.Status is "pending" or "requires_action"),
            RefundIds: refunds.Data.Select(r => r.Id).ToList());
    }

    private SessionService CreateSessionService() => new(CreateClient());

    private StripeClient CreateClient()
    {
        var secretKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new CheckoutConfigurationException(
                "Stripe is not configured. Set the environment variable Stripe__SecretKey to your Stripe test secret key (sk_test_...) and restart the API.");
        }
        if (!secretKey.StartsWith("sk_test_", StringComparison.Ordinal) && !secretKey.StartsWith("rk_test_", StringComparison.Ordinal))
        {
            // This demo must never take real money.
            throw new CheckoutConfigurationException("Only Stripe test-mode keys (sk_test_... or rk_test_...) are accepted.");
        }
        return new StripeClient(secretKey);
    }

    /// <summary>
    /// 4xx responses other than 409 (idempotency in progress), 429 (rate limit) and idempotency
    /// errors mean Stripe refused this request. PaymentService only treats that as proof that no
    /// session exists when it was the order's first attempt.
    /// </summary>
    private static bool IsDefinitiveRejection(StripeException exception)
    {
        var status = (int)exception.HttpStatusCode;
        // An idempotency error means a request with this key already exists, so it proves nothing
        // about whether a session was created; treat it as uncertain.
        var isIdempotencyError = exception.StripeError?.Type == "idempotency_error";
        return status >= 400 && status < 500 && status != 409 && status != 429 && !isIdempotencyError;
    }

    private static CheckoutSessionSnapshot ToSnapshot(Session session) => new(
        SessionId: session.Id,
        Url: session.Url,
        ClientReferenceId: session.ClientReferenceId,
        State: session.Status switch
        {
            "complete" => CheckoutSessionState.Complete,
            "expired" => CheckoutSessionState.Expired,
            _ => CheckoutSessionState.Open,
        },
        IsPaid: session.PaymentStatus == "paid",
        IsTestMode: !session.Livemode,
        IsPaymentMode: session.Mode == "payment",
        AmountTotalCents: session.AmountTotal,
        Currency: session.Currency,
        CustomerEmail: session.CustomerDetails?.Email ?? session.CustomerEmail,
        ExpiresUtc: DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc));
}
