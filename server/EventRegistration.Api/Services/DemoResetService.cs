using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

/// <summary>
/// Development-only "Reset demo availability". Order of work:
/// 1. Settle every pending order through the normal lifecycle (expire its Stripe session, then
///    confirm-if-paid or release). Anything uncertain blocks the reset.
/// 2. Refund each Confirmed order (owner setting DemoReset:RefundCompletedTestPurchases), verified
///    against this order's own Checkout Session. If refunds are not enabled, paid orders block the reset.
/// 3. Only if nothing is blocked: one transaction marks refunded orders RefundedByDemoReset and sets
///    every inventory row to Reserved=0/Sold=0 and every live seat claim to Released.
/// Stripe is never called inside a database transaction. Refunds already made are reported even
/// when a later step blocks; they cannot be rolled back.
/// </summary>
public class DemoResetService(
    AppDbContext db,
    PaymentService payments,
    ICheckoutGateway gateway,
    BookingOperationGate gate,
    TimeProvider clock,
    IOptions<DemoResetOptions> options,
    ILogger<DemoResetService> logger)
{
    public async Task<DemoResetResultDto> ResetAsync(CancellationToken cancellationToken)
    {
        using var exclusive = await gate.EnterResetAsync(cancellationToken);

        var blockers = new List<DemoResetBlockerDto>();
        var refunds = new List<DemoResetRefundDto>();

        var releasedPending = await SettlePendingOrdersAsync(blockers, cancellationToken);
        await RefundAuthorizedPurchasesAsync(refunds, blockers, cancellationToken);

        if (blockers.Count > 0)
        {
            return new DemoResetResultDto(false, BlockedMessage(refunds), refunds, releasedPending, blockers);
        }

        var localBlockers = await ResetLocalAvailabilityAsync(refunds);
        if (localBlockers.Count > 0)
        {
            return new DemoResetResultDto(false, BlockedMessage(refunds), refunds, releasedPending, localBlockers);
        }

        logger.LogWarning("Demo reset completed: {Refunds} refund(s), {Released} pending order(s) released", refunds.Count, releasedPending);
        return new DemoResetResultDto(true, "All demo ticket and seat availability was restored.", refunds, releasedPending, []);
    }

    private async Task<int> SettlePendingOrdersAsync(List<DemoResetBlockerDto> blockers, CancellationToken cancellationToken)
    {
        var pendingIds = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingCheckout || o.Status == OrderStatus.PendingPayment)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var released = 0;
        foreach (var orderId in pendingIds)
        {
            try
            {
                // Same path as the buyer's cancel: expire at Stripe first; paid -> Confirmed, expired -> Released.
                await payments.ConfirmAsync(orderId, cancel: true, cancellationToken);
            }
            catch (BookingException exception)
            {
                blockers.Add(new DemoResetBlockerDto(orderId, $"Pending checkout could not be safely closed: {exception.Message}"));
                continue;
            }

            var status = await StatusOfAsync(orderId);
            if (status == OrderStatus.Released)
            {
                released++;
            }
            else if (status is OrderStatus.PendingCheckout or OrderStatus.PendingPayment)
            {
                blockers.Add(new DemoResetBlockerDto(orderId, "Checkout session may still accept payment; retry the reset."));
            }
            // A pending order that turned out to be paid is now Confirmed and is handled by the refund step.
        }
        return released;
    }

    private async Task RefundAuthorizedPurchasesAsync(List<DemoResetRefundDto> refunds, List<DemoResetBlockerDto> blockers, CancellationToken cancellationToken)
    {
        var refundsEnabled = options.Value.RefundCompletedTestPurchases;
        var confirmedOrders = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Confirmed)
            .OrderBy(o => o.ConfirmedUtc)
            .ToListAsync(cancellationToken);

        foreach (var order in confirmedOrders)
        {
            if (!refundsEnabled)
            {
                blockers.Add(new DemoResetBlockerDto(order.Id,
                    "Paid order and DemoReset:RefundCompletedTestPurchases is off; its tickets stay sold."));
                continue;
            }

            var refund = await RefundOneAsync(order, cancellationToken);
            if (refund.Blocker is not null)
            {
                blockers.Add(refund.Blocker);
            }
            if (refund.Result is not null)
            {
                refunds.Add(refund.Result);
            }
        }
    }

    private async Task<(DemoResetRefundDto? Result, DemoResetBlockerDto? Blocker)> RefundOneAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.StripeSessionId is null)
        {
            return (null, new DemoResetBlockerDto(order.Id, "Confirmed order has no stored Checkout Session."));
        }

        try
        {
            // Bind the refund to this application's own session for this order, never "the latest payment".
            var session = await gateway.GetSessionAsync(order.StripeSessionId, cancellationToken);
            var matches = session.IsPaid
                && session.IsTestMode
                && session.ClientReferenceId == order.Id.ToString()
                && session.AmountTotalCents == order.TotalCents
                && string.Equals(session.Currency, order.Currency, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                return (null, new DemoResetBlockerDto(order.Id, "Stripe session does not match this order (or is not a paid test-mode session); not refunded."));
            }

            var refund = await gateway.RefundSessionPaymentAsync(order.StripeSessionId, $"blevinspass-order-{order.Id:N}-demo-reset-refund", cancellationToken);
            if (!refund.IsTestMode || refund.AmountReceivedCents != order.TotalCents)
            {
                return (null, new DemoResetBlockerDto(order.Id, "Payment amount or mode does not match the order; not treated as refunded."));
            }

            var status = refund.IsFullyRefunded ? "refunded" : refund.HasPendingRefund ? "refund_pending" : "not_fully_refunded";
            var result = new DemoResetRefundDto(order.Id, order.StripeSessionId, refund.PaymentIntentId, refund.RefundIds, refund.AmountRefundedCents, status);
            return refund.IsFullyRefunded
                ? (result, null)
                : (result, new DemoResetBlockerDto(order.Id, $"Refund not yet complete at Stripe ({status}); tickets stay sold. Retry later."));
        }
        catch (CheckoutUncertainException exception)
        {
            logger.LogWarning(exception, "Demo reset refund for order {OrderId} could not be verified", order.Id);
            return (null, new DemoResetBlockerDto(order.Id, "Refund could not be completed or verified at Stripe; tickets stay sold. Retry the reset."));
        }
        catch (CheckoutConfigurationException exception)
        {
            return (null, new DemoResetBlockerDto(order.Id, exception.Message));
        }
    }

    /// <summary>The only place availability is zeroed, in one transaction, after provider outcomes are verified.</summary>
    private async Task<List<DemoResetBlockerDto>> ResetLocalAvailabilityAsync(List<DemoResetRefundDto> refunds)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync();

        foreach (var refund in refunds)
        {
            var reason = $"DemoResetRefund {string.Join(",", refund.RefundIds)}";
            await db.Orders
                .Where(o => o.Id == refund.OrderId && o.Status == OrderStatus.Confirmed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.Status, OrderStatus.RefundedByDemoReset)
                    .SetProperty(o => o.ReleaseReason, reason)
                    .SetProperty(o => o.ReleasedUtc, now));
        }

        // Authoritative re-check inside the transaction: nothing live may remain before counters are zeroed.
        var stillLive = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingCheckout || o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Confirmed)
            .Select(o => o.Id)
            .ToListAsync();
        if (stillLive.Count > 0)
        {
            await transaction.RollbackAsync();
            return stillLive.Select(id => new DemoResetBlockerDto(id, "Order became active during the reset; retry.")).ToList();
        }

        await db.SlotInventories.ExecuteUpdateAsync(setters => setters
            .SetProperty(i => i.Reserved, 0)
            .SetProperty(i => i.Sold, 0));
        await db.SeatReservations
            .Where(r => r.Status == SeatReservationStatus.Held || r.Status == SeatReservationStatus.Sold)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, SeatReservationStatus.Released));

        await transaction.CommitAsync();
        return [];
    }

    private async Task<OrderStatus> StatusOfAsync(Guid orderId) =>
        await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync();

    /// <summary>
    /// Says exactly what happened at Stripe before the block: completed refunds cannot be undone and
    /// will not be repeated; pending or incomplete ones are named separately and are not successes.
    /// </summary>
    internal static string BlockedMessage(List<DemoResetRefundDto> refunds)
    {
        var completed = refunds.Count(r => r.ProviderStatus == "refunded");
        var incomplete = refunds.Count - completed;
        var message = "Availability was NOT reset; resolve the listed orders and retry.";
        if (completed > 0)
        {
            message += $" {completed} refund(s) completed at Stripe; they are not rolled back and will not be repeated.";
        }
        if (incomplete > 0)
        {
            message += $" {incomplete} refund(s) are pending or incomplete at Stripe and are not counted as refunded.";
        }
        return message;
    }
}
