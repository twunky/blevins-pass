using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;

namespace EventRegistration.Tests;

/// <summary>Order/payment transitions, using the deterministic fake gateway for provider outcomes.</summary>
public class PaymentLifecycleTests
{
    private const string LanternWalk = "Harvest Lantern Walk";

    private static async Task<(TestHarness Harness, OrderDto Order, int SlotId, int TicketTypeId)> CreateHeldOrderAsync(int quantity = 2)
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var typeId = inventory[0].TicketTypeId;
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync(Guid.NewGuid().ToString(),
            new CreateOrderRequest(slotId, [new(typeId, quantity)], null), default);
        return (harness, order, slotId, typeId);
    }

    [Fact]
    public async Task Paid_session_confirms_once_and_moves_holds_to_sold()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        var checkout = await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);

        // Return from Stripe twice plus a concurrent duplicate: inventory must move exactly once.
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        await Task.WhenAll(
            harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default),
            harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default));

        var confirmed = await harness.OrderAsync(order.Id);
        var inventory = await harness.InventoryAsync(slotId, typeId);
        Assert.StartsWith("https://", checkout.CheckoutUrl);
        Assert.Equal("Confirmed", confirmed.Status.ToString());
        Assert.Equal("buyer@example.com", confirmed.CustomerEmail);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(2, inventory.Sold);
    }

    [Fact]
    public async Task Unpaid_open_session_keeps_the_hold_on_return()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);

        Assert.Equal("PendingPayment", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Repeated_checkout_returns_the_same_session()
    {
        var (harness, order, _, _) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        var first = await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        var second = await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);

        Assert.Equal(first.CheckoutUrl, second.CheckoutUrl);
        Assert.Equal(1, harness.Gateway.CreateCalls);
    }

    [Fact]
    public async Task Definitive_creation_failure_releases_the_hold()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.Reject;

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("Released", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(0, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Theory]
    [InlineData(FakeCreateBehavior.UncertainNothingCreated)]
    [InlineData(FakeCreateBehavior.UncertainButCreated)]
    public async Task Uncertain_creation_retains_the_hold_and_retry_reuses_the_session_identity(FakeCreateBehavior behavior)
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        harness.Gateway.NextCreateBehavior = behavior;

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("PendingCheckout", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);

        // Even long after the pre-checkout window, reconciliation must not release blindly.
        harness.Clock.Advance(TimeSpan.FromMinutes(20));
        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.Succeed;
        var retry = await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);

        var stored = await harness.OrderAsync(order.Id);
        Assert.Equal("PendingPayment", stored.Status.ToString());
        Assert.EndsWith(stored.StripeSessionId!, retry.CheckoutUrl);
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Expired_unpaid_session_releases_exactly_once()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);

        harness.Clock.Advance(TimeSpan.FromMinutes(32));
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);
        await harness.CreateServices().Payments.ReconcileStaleOrdersAsync(slotId, default);

        var released = await harness.OrderAsync(order.Id);
        Assert.Equal("Released", released.Status.ToString());
        Assert.Equal("CheckoutExpired", released.ReleaseReason);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Elapsed_local_time_alone_does_not_release_when_provider_is_unreachable()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);

        harness.Clock.Advance(TimeSpan.FromHours(2));
        harness.Gateway.FailReads = true;
        await harness.CreateServices().Payments.ReconcileStaleOrdersAsync(slotId, default);

        Assert.Equal("PendingPayment", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Paid_order_without_a_return_visit_is_confirmed_by_later_reconciliation()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);

        // Customer closed the tab; a later availability read for the slot settles the order.
        harness.Clock.Advance(TimeSpan.FromMinutes(40));
        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(order.EventId, slotId, default);

        Assert.Equal("Confirmed", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Sold);
        Assert.Equal(18, availability.TicketTypes.Single(t => t.TicketTypeId == typeId).Remaining);
    }

    [Fact]
    public async Task Cancel_expires_the_open_session_then_releases()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);

        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: true, default);

        var released = await harness.OrderAsync(order.Id);
        Assert.Equal("Released", released.Status.ToString());
        Assert.Equal("CancelledByCustomer", released.ReleaseReason);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Cancel_racing_a_successful_payment_confirms_the_order()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);

        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: true, default);

        Assert.Equal("Confirmed", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Sold);
    }

    [Fact]
    public async Task Abandoned_order_without_checkout_is_released_after_the_hold_window()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(order.EventId, slotId, default);

        Assert.Equal("AbandonedBeforeCheckout", (await harness.OrderAsync(order.Id)).ReleaseReason);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Paid_session_with_mismatched_amount_is_not_confirmed()
    {
        var (harness, order, slotId, typeId) = await CreateHeldOrderAsync();
        await using var harnessScope = harness;
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);
        harness.Gateway.AmountOverride = 1;

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("PendingPayment", (await harness.OrderAsync(order.Id)).Status.ToString());
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }
}
