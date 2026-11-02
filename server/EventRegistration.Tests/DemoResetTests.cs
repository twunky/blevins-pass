using System.Net;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>Development demo reset: refunds, releases and availability restore (fake Stripe outcomes).</summary>
public class DemoResetTests
{
    private const string LanternWalk = "Harvest Lantern Walk";

    /// <summary>Buys tickets through the normal lifecycle and confirms payment.</summary>
    private static async Task<Guid> PaidGeneralAdmissionOrderAsync(TestHarness harness, int quantity = 2)
    {
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync(Guid.NewGuid().ToString(),
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, quantity)], null), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        return order.Id;
    }

    private static async Task<Guid> PaidSeatOrderAsync(TestHarness harness, int seatIndex)
    {
        var (_, slotIds, seats) = await harness.ConcertAsync();
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync(Guid.NewGuid().ToString(),
            new CreateOrderRequest(slotIds[1], null, [seats[seatIndex].Id]), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        return order.Id;
    }

    private static async Task<(int Reserved, int Sold, int LiveSeats, int Capacity, int PriceSum, int Orders, string Theme)> SnapshotAsync(TestHarness harness)
    {
        await using var db = harness.CreateContext();
        var inventory = await db.SlotInventories.AsNoTracking().ToListAsync();
        return (
            inventory.Sum(i => i.Reserved),
            inventory.Sum(i => i.Sold),
            await db.SeatReservations.CountAsync(r => r.Status == SeatReservationStatus.Held || r.Status == SeatReservationStatus.Sold),
            inventory.Sum(i => i.Capacity),
            await db.TicketTypes.SumAsync(t => t.PriceCents) + await db.SeatingSections.SumAsync(s => s.PriceCents),
            await db.Orders.CountAsync(),
            (await db.SiteSettings.SingleAsync()).ActiveTheme.ToString());
    }

    [Fact]
    public async Task Verified_refunds_and_expirations_restore_all_availability_and_keep_history()
    {
        await using var harness = await TestHarness.CreateAsync();
        var paidTickets = await PaidGeneralAdmissionOrderAsync(harness);
        var paidSeat = await PaidSeatOrderAsync(harness, seatIndex: 0);
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (openCheckout, _) = await harness.CreateServices().Orders.CreateOrderAsync("open",
            new CreateOrderRequest(slotId, [new(inventory[1].TicketTypeId, 1)], null), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(openCheckout.Id, default);
        await harness.CreateServices().Orders.CreateOrderAsync("no-checkout",
            new CreateOrderRequest(slotId, [new(inventory[1].TicketTypeId, 1)], null), default);
        var before = await SnapshotAsync(harness);

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        var after = await SnapshotAsync(harness);
        Assert.True(result.Completed);
        Assert.Equal(2, result.RefundedOrders.Count);
        Assert.All(result.RefundedOrders, r => Assert.Equal("refunded", r.ProviderStatus));
        Assert.Equal(2, result.ReleasedPendingOrders);
        Assert.Equal((0, 0, 0), (after.Reserved, after.Sold, after.LiveSeats));
        Assert.Equal((before.Capacity, before.PriceSum, before.Orders, before.Theme), (after.Capacity, after.PriceSum, after.Orders, after.Theme));
        Assert.Equal(OrderStatus.RefundedByDemoReset, (await harness.OrderAsync(paidTickets)).Status);
        Assert.Equal(OrderStatus.RefundedByDemoReset, (await harness.OrderAsync(paidSeat)).Status);
        Assert.Equal(OrderStatus.Released, (await harness.OrderAsync(openCheckout.Id)).Status);
        Assert.NotNull((await harness.OrderAsync(paidTickets)).StripeSessionId); // Stripe reference preserved
    }

    [Fact]
    public async Task Repeated_reset_and_already_refunded_payments_never_refund_twice()
    {
        await using var harness = await TestHarness.CreateAsync();
        await PaidGeneralAdmissionOrderAsync(harness);
        var refundedInDashboard = await PaidSeatOrderAsync(harness, seatIndex: 1);
        harness.Gateway.MarkRefundedAtProvider((await harness.OrderAsync(refundedInDashboard)).StripeSessionId!);
        var gate = new BookingOperationGate();

        // Double click: two resets at once, then a third later.
        var results = await Task.WhenAll(harness.CreateDemoReset(gate).ResetAsync(default), harness.CreateDemoReset(gate).ResetAsync(default));
        var third = await harness.CreateDemoReset(gate).ResetAsync(default);

        Assert.All(results.Append(third), r => Assert.True(r.Completed));
        Assert.Equal(1, harness.Gateway.RefundsCreated); // only the not-yet-refunded purchase
        var after = await SnapshotAsync(harness);
        Assert.Equal((0, 0, 0), (after.Reserved, after.Sold, after.LiveSeats));
    }

    [Fact]
    public async Task Refund_failure_keeps_paid_inventory_sold()
    {
        await using var harness = await TestHarness.CreateAsync();
        var order = await PaidGeneralAdmissionOrderAsync(harness);
        harness.Gateway.FailRefunds = true;

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Contains(result.Blockers, b => b.OrderId == order);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(order)).Status);
        Assert.Equal(2, (await SnapshotAsync(harness)).Sold);
    }

    [Fact]
    public async Task Pending_refund_is_not_treated_as_refunded()
    {
        await using var harness = await TestHarness.CreateAsync();
        var order = await PaidGeneralAdmissionOrderAsync(harness);
        harness.Gateway.RefundsStayPending = true;

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Equal("refund_pending", Assert.Single(result.RefundedOrders).ProviderStatus);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(order)).Status);
        Assert.Equal(2, (await SnapshotAsync(harness)).Sold);
    }

    [Fact]
    public async Task Uncertain_checkout_creation_keeps_its_hold_and_blocks_reset()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("uncertain",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 2)], null), default);
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.UncertainButCreated;
        await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.Succeed;
        harness.Gateway.RejectReplays = true;

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Contains(result.Blockers, b => b.OrderId == order.Id);
        Assert.Equal(2, (await SnapshotAsync(harness)).Reserved);
    }

    [Fact]
    public async Task Payment_completing_before_expiration_is_confirmed_then_refunded_never_released_unpaid()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("paid-no-return",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 2)], null), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!); // paid, buyer never returned

        var withRefunds = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.True(withRefunds.Completed);
        Assert.Equal(OrderStatus.RefundedByDemoReset, (await harness.OrderAsync(order.Id)).Status);
        Assert.Equal(1, harness.Gateway.RefundsCreated);
    }

    [Fact]
    public async Task With_refunds_disabled_paid_orders_block_and_nothing_is_freed()
    {
        await using var harness = await TestHarness.CreateAsync();
        var order = await PaidGeneralAdmissionOrderAsync(harness);

        var result = await harness.CreateDemoReset(new BookingOperationGate(), refundCompletedTestPurchases: false).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Equal(0, harness.Gateway.RefundsCreated);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(order)).Status);
        Assert.Equal(2, (await SnapshotAsync(harness)).Sold);
    }

    [Fact]
    public async Task Live_mode_target_is_never_refunded()
    {
        await using var harness = await TestHarness.CreateAsync();
        var order = await PaidGeneralAdmissionOrderAsync(harness);
        harness.Gateway.LiveMode = true;

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Equal(0, harness.Gateway.RefundsCreated);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(order)).Status);
    }

    [Fact]
    public async Task Reset_waits_for_an_in_flight_checkout_and_late_payment_cannot_reconfirm()
    {
        await using var harness = await TestHarness.CreateAsync();
        var gate = new BookingOperationGate();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("racing",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 2)], null), default);
        var stripeReceived = new TaskCompletionSource();
        var letStripeRespond = new TaskCompletionSource();
        harness.Gateway.PauseFirstCreate = async () => { stripeReceived.SetResult(); await letStripeRespond.Task; };

        // A checkout request (holding the booking gate, as the controller does) is paused inside Stripe.
        var checkout = Task.Run(async () =>
        {
            using var operation = await gate.EnterBookingOperationAsync();
            return await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        });
        await stripeReceived.Task;
        var reset = harness.CreateDemoReset(gate).ResetAsync(default);
        var finishedEarly = await Task.WhenAny(reset, Task.Delay(300)) == reset;

        letStripeRespond.SetResult();
        await checkout;
        var result = await reset;

        Assert.False(finishedEarly);
        Assert.True(result.Completed);
        var released = await harness.OrderAsync(order.Id);
        Assert.Equal(OrderStatus.Released, released.Status);

        // A payment arriving afterwards on that (expired) session can never re-confirm or resell it.
        harness.Gateway.Pay(released.StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        Assert.Equal(OrderStatus.Released, (await harness.OrderAsync(order.Id)).Status);
        Assert.Equal(0, (await SnapshotAsync(harness)).Reserved + (await SnapshotAsync(harness)).Sold);
    }

    [Fact]
    public async Task Mixed_completed_and_pending_refunds_are_reported_separately_and_inventory_stays_held()
    {
        await using var harness = await TestHarness.CreateAsync();
        var completed = await PaidGeneralAdmissionOrderAsync(harness);
        var pending = await PaidSeatOrderAsync(harness, seatIndex: 2);
        harness.Gateway.PendingRefundSessionIds.Add((await harness.OrderAsync(pending)).StripeSessionId!);

        var result = await harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.False(result.Completed);
        Assert.Equal("refunded", result.RefundedOrders.Single(r => r.OrderId == completed).ProviderStatus);
        Assert.Equal("refund_pending", result.RefundedOrders.Single(r => r.OrderId == pending).ProviderStatus);
        Assert.Contains("1 refund(s) completed", result.Message);
        Assert.Contains("1 refund(s) are pending or incomplete", result.Message);
        Assert.Single(result.Blockers, b => b.OrderId == pending);
        // Nothing local changed: both orders stay Confirmed and their tickets/seat stay sold until a retry succeeds.
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(completed)).Status);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(pending)).Status);
        var after = await SnapshotAsync(harness);
        Assert.Equal((2, 1), (after.Sold, after.LiveSeats));
    }

    [Theory]
    [InlineData(true, true, "127.0.0.1", true)]
    [InlineData(true, true, "::1", true)]
    [InlineData(false, true, "127.0.0.1", false)]
    [InlineData(true, false, "127.0.0.1", false)]
    [InlineData(true, true, "192.168.1.20", false)]
    public void Endpoint_requires_development_opt_in_and_loopback(bool isDevelopment, bool enabled, string remote, bool allowed)
    {
        Assert.Equal(allowed, DemoResetOptions.IsAllowed(isDevelopment, enabled, IPAddress.Parse(remote)));
    }
}
