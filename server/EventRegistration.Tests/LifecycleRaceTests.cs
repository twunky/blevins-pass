using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;

namespace EventRegistration.Tests;

/// <summary>
/// Deterministic interleavings of checkout creation, cleanup and recovery (REVIEW-001). Each
/// "request" uses its own DbContext/connection; ordering is forced with explicit pauses, not luck.
/// </summary>
public class LifecycleRaceTests
{
    private static async Task<(TestHarness Harness, OrderDto Order, int SlotId, int TicketTypeId)> CreateHeldOrderAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync("Harvest Lantern Walk");
        var typeId = inventory[0].TicketTypeId;
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync(Guid.NewGuid().ToString(),
            new CreateOrderRequest(slotId, [new(typeId, 2)], null), default);
        return (harness, order, slotId, typeId);
    }

    [Fact]
    public async Task Stale_abandoned_cleanup_cannot_release_after_checkout_claimed_the_attempt()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        // Cleanup decided "no attempt yet" earlier; checkout claims first; then cleanup tries to release.
        var claimed = await harness.CreateServices().Payments.TryClaimCheckoutAttemptAsync(order.Id);
        var released = await harness.CreateServices().Payments.ReleaseAsync(order.Id, "AbandonedBeforeCheckout", requireNoCheckoutAttempt: true);

        Assert.True(claimed);
        Assert.False(released);
        Assert.Equal("PendingCheckout", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Stale_checkout_caller_cannot_create_a_session_after_cleanup_released_the_order()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        var released = await harness.CreateServices().Payments.ReleaseAsync(order.Id, "AbandonedBeforeCheckout", requireNoCheckoutAttempt: true);
        var claimed = await harness.CreateServices().Payments.TryClaimCheckoutAttemptAsync(order.Id);
        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));

        Assert.True(released);
        Assert.False(claimed);
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(0, harness.Gateway.CreateCalls); // no payable session was ever requested
        Assert.Equal(0, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Cleanup_while_stripe_is_creating_the_session_keeps_the_hold()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        var stripeReceived = new TaskCompletionSource();
        var letStripeRespond = new TaskCompletionSource();
        harness.Gateway.PauseFirstCreate = async () =>
        {
            stripeReceived.SetResult();
            await letStripeRespond.Task;
        };

        var checkout = harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        await stripeReceived.Task;

        // The pre-checkout window has passed and another request runs cleanup for this slot.
        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(order.EventId, slotId, default);
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);

        letStripeRespond.SetResult();
        var session = await checkout;

        var stored = await harness.OrderAsync(order.Id);
        Assert.Equal("PendingPayment", stored.Status.ToString());
        Assert.EndsWith(stored.StripeSessionId!, session.CheckoutUrl);
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Concurrent_first_checkouts_with_different_clocks_reuse_one_frozen_payload()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        var laterServerClock = new TestClock(harness.Clock.UtcNow.AddMinutes(2));
        var stripeReceived = new TaskCompletionSource();
        var letStripeRespond = new TaskCompletionSource();
        harness.Gateway.PauseFirstCreate = async () =>
        {
            stripeReceived.SetResult();
            await letStripeRespond.Task;
        };

        // Caller A claims the attempt and is paused inside Stripe; caller B (a clock 2 minutes ahead) retries.
        var callerA = harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        await stripeReceived.Task;
        var callerB = await harness.CreateServices(laterServerClock).Payments.StartCheckoutAsync(order.Id, default);
        letStripeRespond.SetResult();
        var resultA = await callerA;

        Assert.Equal(resultA.CheckoutUrl, callerB.CheckoutUrl); // the fake rejects mismatched payloads, so equality proves one payload
        Assert.Equal("PendingPayment", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Lost_response_then_rejected_retry_keeps_the_hold_until_recovery_succeeds()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.UncertainButCreated;

        var lost = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));

        // The session exists at Stripe, but the retry is refused: that is not proof nothing was created.
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.Succeed;
        harness.Gateway.RejectReplays = true;
        var rejectedRetry = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));
        harness.Clock.Advance(TimeSpan.FromMinutes(20));
        await harness.CreateServices().Payments.ReconcileStaleOrdersAsync(slotId, default);

        Assert.Equal(503, lost.StatusCode);
        Assert.Equal(503, rejectedRetry.StatusCode);
        Assert.Equal("PendingCheckout", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);

        // Recovery: once the provider answers the replay, the original session is recorded.
        harness.Gateway.RejectReplays = false;
        var recovered = await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        var stored = await harness.OrderAsync(order.Id);
        Assert.Equal("PendingPayment", stored.Status.ToString());
        Assert.EndsWith(stored.StripeSessionId!, recovered.CheckoutUrl);
    }

    [Fact]
    public async Task Repeated_and_concurrent_confirmation_never_double_counts_sold_inventory()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default)));
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: true, default);

        var inventory = await harness.InventoryAsync(slotId, typeId);
        Assert.Equal(2, inventory.Sold);
        Assert.Equal(0, inventory.Reserved);
    }
}
