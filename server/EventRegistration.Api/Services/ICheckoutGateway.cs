namespace EventRegistration.Api.Services;

/// <summary>
/// The payment provider boundary. Production uses <see cref="StripeCheckoutGateway"/> (real Stripe
/// test-mode Checkout). Tests substitute a deterministic fake to exercise failure paths.
/// </summary>
public interface ICheckoutGateway
{
    /// <summary>
    /// Creates (or, when retried with the same idempotency key, returns) a hosted Checkout session.
    /// Throws <see cref="CheckoutRejectedException"/> when the provider definitively refused,
    /// or <see cref="CheckoutUncertainException"/> when the outcome is unknown.
    /// </summary>
    Task<CheckoutSessionSnapshot> CreateSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken);

    /// <summary>Retrieves current session state. Throws <see cref="CheckoutUncertainException"/> on failure.</summary>
    Task<CheckoutSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Expires an open session so it can no longer accept payment, returning its new state.</summary>
    Task<CheckoutSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Development demo reset only: refunds whatever is still unrefunded on the payment of this exact
    /// session (never any other payment), using a stable idempotency key, then re-reads the refunds.
    /// Throws <see cref="CheckoutUncertainException"/> if the outcome could not be verified.
    /// </summary>
    Task<PaymentRefundSnapshot> RefundSessionPaymentAsync(string sessionId, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>Verified refund state of one Checkout Session's payment.</summary>
public record PaymentRefundSnapshot(
    string PaymentIntentId,
    bool IsTestMode,
    long AmountReceivedCents,
    long AmountRefundedCents,
    bool HasPendingRefund,
    IReadOnlyList<string> RefundIds)
{
    /// <summary>Only succeeded refunds count; a pending refund is not a completed refund.</summary>
    public bool IsFullyRefunded => AmountReceivedCents > 0 && AmountRefundedCents >= AmountReceivedCents && !HasPendingRefund;
}

public record CheckoutLineItem(string Name, int UnitAmountCents, int Quantity);

public record CheckoutSessionRequest(
    Guid OrderId,
    string IdempotencyKey,
    string Currency,
    IReadOnlyList<CheckoutLineItem> LineItems,
    DateTime ExpiresUtc,
    string SuccessUrl,
    string CancelUrl);

/// <summary>Provider-neutral view of a Checkout session.</summary>
public record CheckoutSessionSnapshot(
    string SessionId,
    string? Url,
    string? ClientReferenceId,
    CheckoutSessionState State,
    bool IsPaid,
    bool IsTestMode,
    bool IsPaymentMode,
    long? AmountTotalCents,
    string? Currency,
    string? CustomerEmail,
    DateTime ExpiresUtc);

public enum CheckoutSessionState
{
    Open,
    Complete,
    Expired,
}

/// <summary>The provider definitively refused the request; no session was created.</summary>
public class CheckoutRejectedException(string message) : Exception(message);

/// <summary>The outcome is unknown (timeout, network, provider 5xx/429). Holds must be retained.</summary>
public class CheckoutUncertainException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Stripe is not configured (missing/invalid key). Never treated as payment success.</summary>
public class CheckoutConfigurationException(string message) : Exception(message);
