using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

/// <summary>
/// Owns the order payment lifecycle: creating the Stripe session, verifying payment server-side,
/// and reconciling pending orders. Core rule: inventory is released only when we know no payment
/// can still succeed (Stripe says expired/unpaid, Stripe definitively refused creation, or no
/// checkout was ever attempted). A local clock alone never proves that.
/// </summary>
public class PaymentService(
    AppDbContext db,
    InventoryService inventory,
    ICheckoutGateway gateway,
    TimeProvider clock,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<PaymentService> logger)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>Creates (or recovers) the single hosted Checkout session for an order.</summary>
    public async Task<CheckoutSessionDto> StartCheckoutAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId);

        switch (order.Status)
        {
            case OrderStatus.Confirmed:
                throw BookingException.Conflict("order_already_confirmed", "This order is already paid.");
            case OrderStatus.Released or OrderStatus.RefundedByDemoReset:
                throw BookingException.Conflict("order_released", "This order's hold was released. Please start a new order.");
            case OrderStatus.PendingPayment:
                if (order.StripeSessionExpiresUtc > UtcNow.AddMinutes(1) && order.StripeCheckoutUrl is not null)
                {
                    return new CheckoutSessionDto(order.Id, order.StripeCheckoutUrl, order.StripeSessionExpiresUtc.Value);
                }
                await ReconcileAsync(order, cancel: false, cancellationToken);
                throw BookingException.Conflict("checkout_expired", "The checkout window for this order has ended. Please start a new order.");
        }

        var link = await CreateOrRecoverSessionAsync(order.Id, cancellationToken);
        return new CheckoutSessionDto(order.Id, link.Url, link.ExpiresUtc);
    }

    /// <summary>
    /// Called when the customer returns from Stripe (success or cancel). Verifies the session
    /// server-side; query-string "success" is never trusted. With cancel=true an open session is
    /// expired at Stripe first, and if it turns out to be paid the order is confirmed anyway.
    /// </summary>
    public async Task ConfirmAsync(Guid orderId, bool cancel, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId);
        await ReconcileAsync(order, cancel, cancellationToken);
    }

    /// <summary>
    /// Opportunistic cleanup (no background scheduler): settles orders for this slot whose local
    /// deadlines passed. Provider uncertainty leaves the hold in place.
    /// </summary>
    public async Task ReconcileStaleOrdersAsync(int timeSlotId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var staleOrderIds = await db.Orders.AsNoTracking()
            .Where(o => o.TimeSlotId == timeSlotId
                && ((o.Status == OrderStatus.PendingCheckout && o.PreCheckoutHoldExpiresUtc < now)
                    || (o.Status == OrderStatus.PendingPayment && o.StripeSessionExpiresUtc < now)))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        foreach (var staleOrderId in staleOrderIds)
        {
            try
            {
                await ReconcileAsync(await LoadOrderAsync(staleOrderId), cancel: false, cancellationToken);
            }
            catch (BookingException exception)
            {
                logger.LogWarning("Order {OrderId} kept on hold during reconciliation: {Reason}", staleOrderId, exception.Message);
            }
        }
    }

    private async Task ReconcileAsync(Order order, bool cancel, CancellationToken cancellationToken)
    {
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Released or OrderStatus.RefundedByDemoReset)
        {
            return;
        }

        if (order.Status == OrderStatus.PendingCheckout)
        {
            if (order.CheckoutAttemptStartedUtc is null)
            {
                // No session was requested when we read the order. The release re-checks that in the
                // same UPDATE, so a checkout that claimed the attempt meanwhile wins and nothing is released.
                if (cancel)
                {
                    await ReleaseAsync(order.Id, "CancelledBeforeCheckout", requireNoCheckoutAttempt: true);
                }
                else if (order.PreCheckoutHoldExpiresUtc < UtcNow)
                {
                    await ReleaseAsync(order.Id, "AbandonedBeforeCheckout", requireNoCheckoutAttempt: true);
                }
                return;
            }

            // A creation attempt has an unknown outcome. Replaying it with the same idempotency key
            // either returns the existing session or proves none was created.
            if (!cancel && order.PreCheckoutHoldExpiresUtc >= UtcNow)
            {
                return;
            }
            await CreateOrRecoverSessionAsync(order.Id, cancellationToken);
            order = await LoadOrderAsync(order.Id);
            if (order.Status != OrderStatus.PendingPayment)
            {
                return;
            }
        }

        var session = await GetSessionOrThrowAsync(order.StripeSessionId!, cancellationToken);

        if (!session.IsPaid && session.State == CheckoutSessionState.Open && cancel)
        {
            session = await RunProviderCallAsync(() => gateway.ExpireSessionAsync(order.StripeSessionId!, cancellationToken));
        }

        if (session.IsPaid)
        {
            VerifyPaidSessionMatchesOrder(order, session);
            await MarkConfirmedAsync(order.Id, session.CustomerEmail);
        }
        else if (session.State == CheckoutSessionState.Expired)
        {
            await ReleaseAsync(order.Id, cancel ? "CancelledByCustomer" : "CheckoutExpired", requireNoCheckoutAttempt: false);
        }
        // Otherwise the session is still open and may accept payment: keep the hold.
    }

    /// <summary>
    /// Atomically claims the order's single checkout-creation attempt and freezes its Stripe payload
    /// (expiry). Returns true only for the caller that made the claim, i.e. the first attempt.
    /// Fails (returns false) if the order is no longer PendingCheckout or an attempt already exists.
    /// </summary>
    internal async Task<bool> TryClaimCheckoutAttemptAsync(Guid orderId)
    {
        var now = UtcNow;
        var plannedExpiry = now.AddMinutes(checkoutOptions.Value.SessionLifetimeMinutes);
        var claimed = await db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.PendingCheckout && o.CheckoutAttemptStartedUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.CheckoutAttemptStartedUtc, now)
                .SetProperty(o => o.PlannedSessionExpiresUtc, plannedExpiry));
        return claimed == 1;
    }

    private async Task<CheckoutLink> CreateOrRecoverSessionAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // 1. Claim (or find) the attempt in the database BEFORE calling Stripe. The claim and the
        //    pre-checkout release are both guarded UPDATEs on the same row, so exactly one wins.
        var isFirstAttempt = await TryClaimCheckoutAttemptAsync(orderId);
        var order = await LoadOrderAsync(orderId);

        switch (order.Status)
        {
            case OrderStatus.PendingPayment when order.StripeCheckoutUrl is not null && order.StripeSessionExpiresUtc is not null:
                return new CheckoutLink(order.StripeCheckoutUrl, order.StripeSessionExpiresUtc.Value);
            case OrderStatus.Released or OrderStatus.RefundedByDemoReset:
                throw BookingException.Conflict("order_released", "This order's hold was released. Please start a new order.");
            case OrderStatus.Confirmed:
                throw BookingException.Conflict("order_already_confirmed", "This order is already paid.");
        }

        // 2. Every caller sends the payload frozen by the claim, so concurrent or retried requests with
        //    the same idempotency key are identical (Stripe rejects a reused key with other parameters).
        var request = new CheckoutSessionRequest(
            order.Id,
            order.StripeIdempotencyKey,
            order.Currency,
            order.Items.OrderBy(i => i.Id).Select(i => new CheckoutLineItem(i.Description, i.UnitPriceCents, i.Quantity)).ToList(),
            order.PlannedSessionExpiresUtc!.Value,
            $"{checkoutOptions.Value.ClientBaseUrl}/?orderId={order.Id}&checkout=success",
            $"{checkoutOptions.Value.ClientBaseUrl}/?orderId={order.Id}&checkout=cancel");

        CheckoutSessionSnapshot snapshot;
        try
        {
            snapshot = await gateway.CreateSessionAsync(request, cancellationToken);
        }
        catch (CheckoutConfigurationException exception)
        {
            // The request never left the server, so no session exists. Undo only our own claim.
            if (isFirstAttempt)
            {
                var attemptStarted = order.CheckoutAttemptStartedUtc;
                await db.Orders
                    .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingCheckout && o.CheckoutAttemptStartedUtc == attemptStarted)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(o => o.CheckoutAttemptStartedUtc, (DateTime?)null)
                        .SetProperty(o => o.PlannedSessionExpiresUtc, (DateTime?)null), CancellationToken.None);
            }
            throw new BookingException(StatusCodes.Status503ServiceUnavailable, "payment_not_configured", exception.Message);
        }
        catch (CheckoutRejectedException exception) when (isFirstAttempt)
        {
            // Stripe refused the very first request for this key, so no session can exist: release.
            logger.LogWarning("Stripe refused checkout for order {OrderId}: {Reason}", order.Id, exception.Message);
            await ReleaseAsync(order.Id, "CheckoutCreationRejected", requireNoCheckoutAttempt: false);
            throw BookingException.Conflict("checkout_rejected", "The payment provider could not start checkout for this order. Your hold was released; please start again.");
        }
        catch (CheckoutRejectedException exception)
        {
            // A rejected RECOVERY does not prove the earlier, uncertain attempt created nothing.
            logger.LogWarning("Checkout recovery for order {OrderId} was rejected; hold retained: {Reason}", order.Id, exception.Message);
            throw BookingException.ProviderUnavailable("We could not confirm the checkout status with the payment provider. Your tickets are still held; please try again.");
        }
        catch (CheckoutUncertainException exception)
        {
            logger.LogWarning(exception, "Checkout creation outcome unknown for order {OrderId}; hold retained", order.Id);
            throw BookingException.ProviderUnavailable("We could not reach the payment provider. Your tickets are still held; please try again.");
        }

        if (snapshot.ClientReferenceId != order.Id.ToString())
        {
            throw new InvalidOperationException($"Stripe returned a session for a different order than {order.Id}.");
        }

        // 3. Record the session. Only valid from PendingCheckout; a duplicate caller with the same
        //    session is fine, anything else means the order changed and the URL must not be handed out.
        var recorded = await db.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingCheckout)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, OrderStatus.PendingPayment)
                .SetProperty(o => o.StripeSessionId, snapshot.SessionId)
                .SetProperty(o => o.StripeCheckoutUrl, snapshot.Url)
                .SetProperty(o => o.StripeSessionExpiresUtc, snapshot.ExpiresUtc), cancellationToken);

        if (recorded == 1 || await IsRecordedSessionAsync(order.Id, snapshot.SessionId))
        {
            return new CheckoutLink(snapshot.Url ?? "", snapshot.ExpiresUtc);
        }

        // Defensive: the order left PendingCheckout without this session. Make the session unpayable.
        logger.LogError("Order {OrderId} changed state while its checkout session was being created; expiring session", order.Id);
        try
        {
            await gateway.ExpireSessionAsync(snapshot.SessionId, CancellationToken.None);
        }
        catch (CheckoutUncertainException exception)
        {
            logger.LogError(exception, "Could not expire orphaned session for order {OrderId}", order.Id);
        }
        throw BookingException.Conflict("order_released", "This order is no longer awaiting checkout. Please start a new order.");
    }

    private async Task<bool> IsRecordedSessionAsync(Guid orderId, string sessionId) =>
        await db.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.StripeSessionId == sessionId
            && (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Confirmed));

    private sealed record CheckoutLink(string Url, DateTime ExpiresUtc);

    private async Task<CheckoutSessionSnapshot> GetSessionOrThrowAsync(string sessionId, CancellationToken cancellationToken) =>
        await RunProviderCallAsync(() => gateway.GetSessionAsync(sessionId, cancellationToken));

    private static async Task<CheckoutSessionSnapshot> RunProviderCallAsync(Func<Task<CheckoutSessionSnapshot>> call)
    {
        try
        {
            return await call();
        }
        catch (CheckoutUncertainException)
        {
            throw BookingException.ProviderUnavailable("We could not verify the payment with the provider yet. Your tickets are still held; please retry.");
        }
        catch (CheckoutConfigurationException exception)
        {
            throw new BookingException(StatusCodes.Status503ServiceUnavailable, "payment_not_configured", exception.Message);
        }
    }

    private static void VerifyPaidSessionMatchesOrder(Order order, CheckoutSessionSnapshot session)
    {
        var matches = session.SessionId == order.StripeSessionId
            && session.ClientReferenceId == order.Id.ToString()
            && session.IsTestMode
            && session.IsPaymentMode
            && session.AmountTotalCents == order.TotalCents
            && string.Equals(session.Currency, order.Currency, StringComparison.OrdinalIgnoreCase);

        if (!matches)
        {
            throw BookingException.Conflict("payment_verification_failed", "The payment could not be matched to this order. Your tickets remain held; please contact support.");
        }
    }

    /// <summary>Idempotent: only the request that flips the status moves inventory.</summary>
    private async Task MarkConfirmedAsync(Guid orderId, string? customerEmail)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = UtcNow;
        var flipped = await db.Orders
            .Where(o => o.Id == orderId && (o.Status == OrderStatus.PendingCheckout || o.Status == OrderStatus.PendingPayment))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, OrderStatus.Confirmed)
                .SetProperty(o => o.ConfirmedUtc, now)
                .SetProperty(o => o.CustomerEmail, customerEmail));

        if (flipped == 1)
        {
            await inventory.ConvertHoldsToSoldAsync(await LoadOrderAsync(orderId));
        }
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Idempotent: only the request that flips the status returns inventory. Confirmed orders never release.
    /// With <paramref name="requireNoCheckoutAttempt"/>, the release also fails if a checkout attempt has
    /// been claimed since the caller looked, which arbitrates "abandoned" cleanup against a starting checkout.
    /// Returns true if this call released the order.
    /// </summary>
    internal async Task<bool> ReleaseAsync(Guid orderId, string reason, bool requireNoCheckoutAttempt)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = UtcNow;
        var flipped = await db.Orders
            .Where(o => o.Id == orderId
                && (o.Status == OrderStatus.PendingCheckout || o.Status == OrderStatus.PendingPayment)
                && (!requireNoCheckoutAttempt || (o.Status == OrderStatus.PendingCheckout && o.CheckoutAttemptStartedUtc == null)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Status, OrderStatus.Released)
                .SetProperty(o => o.ReleaseReason, reason)
                .SetProperty(o => o.ReleasedUtc, now));

        if (flipped == 1)
        {
            await inventory.ReleaseHoldsAsync(await LoadOrderAsync(orderId));
        }
        await transaction.CommitAsync();
        return flipped == 1;
    }

    /// <summary>Loads a fresh tracked copy (ExecuteUpdate bypasses the change tracker).</summary>
    private async Task<Order> LoadOrderAsync(Guid orderId)
    {
        var tracked = db.ChangeTracker.Entries<Order>().FirstOrDefault(e => e.Entity.Id == orderId);
        if (tracked is not null)
        {
            await tracked.ReloadAsync();
            return tracked.Entity;
        }
        return await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw BookingException.NotFound("Order not found.");
    }
}
