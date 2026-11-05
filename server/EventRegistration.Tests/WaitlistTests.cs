using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>Waitlist: FIFO, no payment on join, notification gate, capacity-backed offers, expiry and queue advance.</summary>
public class WaitlistTests
{
    private const string Hayride = "Pumpkin Patch Hayride";

    private sealed record Hayrides(TestHarness Harness, int EventId, int SlotId, int Adult, int Child, int Wagon);

    /// <summary>A hayride departure sold out by one held order of 20 adults + 5 children.</summary>
    private static async Task<(Hayrides Ride, OrderDto Filler)> SoldOutHayrideAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (eventId, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        int Id(string name) => inventory.Single(i => i.TicketType!.Name == name).TicketTypeId;
        var ride = new Hayrides(harness, eventId, slotId, Id("Adult"), Id("Child"), Id("Family Wagon"));
        var (filler, _) = await harness.CreateServices().Orders.CreateOrderAsync("filler",
            new CreateOrderRequest(slotId, [new(ride.Adult, 20), new(ride.Child, 5)], null), default);
        return (ride, filler);
    }

    private static async Task<WaitlistStatusDto> JoinAsync(Hayrides ride, string key, params TicketQuantityRequest[] tickets) =>
        (await ride.Harness.CreateServices().Waitlist.JoinAsync(key, new JoinWaitlistRequest(ride.SlotId, $"{key}@example.com", tickets), default)).Entry;

    private static Task<WaitlistStatusDto> StatusAsync(Hayrides ride, Guid reference) =>
        ride.Harness.CreateServices().Waitlist.GetStatusAsync(reference, default);

    /// <summary>Frees places by cancelling the filler order (customer abandoned checkout).</summary>
    private static Task CancelFillerAsync(Hayrides ride, OrderDto filler) =>
        ride.Harness.CreateServices().Payments.ConfirmAsync(filler.Id, cancel: true, default);

    private static async Task<int> LivePeopleAsync(Hayrides ride)
    {
        await using var db = ride.Harness.CreateContext();
        return await db.SlotInventories.Where(i => i.TimeSlotId == ride.SlotId).SumAsync(i => i.Reserved + i.Sold);
    }

    [Fact]
    public async Task Joining_is_first_come_first_served_retry_safe_and_never_calls_the_payment_provider()
    {
        var (ride, _) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;

        var first = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 2));
        var retry = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 2));
        var second = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Child, 1));

        Assert.Equal(first.Reference, retry.Reference);
        Assert.Equal(("Waiting", 1), (first.Status, first.PositionInLine));
        Assert.Equal(("Waiting", 2), (second.Status, second.PositionInLine));
        Assert.False(first.CanAccept);
        Assert.Equal(0, ride.Harness.Gateway.CreateCalls);
        await using var db = ride.Harness.CreateContext();
        Assert.Equal(2, await db.WaitlistEntries.CountAsync());
        Assert.Equal(1, await db.Orders.CountAsync()); // only the order that sold the departure out

        var reused = await Assert.ThrowsAsync<BookingException>(() => JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 3)));
        Assert.Equal("idempotency_key_reused", reused.Code);
    }

    [Theory]
    [InlineData("not-an-email", 2)]
    [InlineData("", 2)]
    [InlineData("big@example.com", 26)]
    public async Task Invalid_waitlist_requests_are_rejected(string email, int quantity)
    {
        var (ride, _) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var tickets = new List<TicketQuantityRequest> { new(ride.Adult, Math.Min(quantity, 20)) };
        if (quantity > 20)
        {
            tickets.Add(new(ride.Child, quantity - 20)); // a group larger than the 25-person departure
        }
        var request = new JoinWaitlistRequest(ride.SlotId, email, tickets);

        var error = await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Waitlist.JoinAsync("k", request, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Waitlist_rejects_seated_events_and_closed_departures()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, concertSlots, seats) = await harness.ConcertAsync();
        var seated = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Waitlist.JoinAsync("s",
            new JoinWaitlistRequest(concertSlots[0], "a@example.com", [new(1, 1)]), default));
        Assert.Equal(400, seated.StatusCode);

        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        harness.Clock.Advance(TimeSpan.FromDays(60));
        var closed = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Waitlist.JoinAsync("c",
            new JoinWaitlistRequest(slotId, "a@example.com", [new(inventory[0].TicketTypeId, 1)]), default));
        Assert.Equal("slot_closed", closed.Code);
    }

    [Fact]
    public async Task Freed_places_go_to_the_head_of_the_line_and_purchase_waits_for_manual_notification()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 2));
        var bob = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Child, 1));

        await CancelFillerAsync(ride, filler);
        var availability = await ride.Harness.CreateServices().Catalog.GetTicketAvailabilityAsync(ride.EventId, ride.SlotId, default);

        // Both fit in order; each offer reserves its places so the public sees what is left after them.
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, alice.Reference)).Status);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal(22, availability.SharedRemaining);
        Assert.Equal(3, await LivePeopleAsync(ride));

        // Not purchasable until the owner records the manual notification.
        var early = await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default));
        Assert.Equal("offer_not_available", early.Code);

        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);
        var offered = await StatusAsync(ride, alice.Reference);
        Assert.Equal("OfferAvailable", offered.Status);
        Assert.True(offered.CanAccept);
        Assert.NotNull(offered.OfferExpiresUtc);

        // Accept twice (retry) -> one order, priced by the server, holding exactly the offered places.
        var orderId = await ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default);
        Assert.Equal(orderId, await ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default));
        var order = await ride.Harness.CreateServices().Orders.GetOrderAsync(orderId);
        Assert.Equal(2 * 1800, order.TotalCents);
        Assert.Equal(3, await LivePeopleAsync(ride));

        // Ordinary hosted checkout, retried: one session.
        var checkout = await ride.Harness.CreateServices().Payments.StartCheckoutAsync(orderId, default);
        var again = await ride.Harness.CreateServices().Payments.StartCheckoutAsync(orderId, default);
        Assert.Equal(checkout.CheckoutUrl, again.CheckoutUrl);
        Assert.Equal("CheckoutStarted", (await StatusAsync(ride, alice.Reference)).Status);

        ride.Harness.Gateway.Pay((await ride.Harness.OrderAsync(orderId)).StripeSessionId!);
        await ride.Harness.CreateServices().Payments.ConfirmAsync(orderId, cancel: false, default);
        Assert.Equal("Purchased", (await StatusAsync(ride, alice.Reference)).Status);
        Assert.Equal(2, (await ride.Harness.InventoryAsync(ride.SlotId, ride.Adult)).Sold);
    }

    [Fact]
    public async Task Head_of_line_group_that_does_not_fit_blocks_smaller_groups_behind_it_and_public_sales()
    {
        var (ride, _) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Orders.CreateOrderAsync("sold-out",
            new CreateOrderRequest(ride.SlotId, [new(ride.Adult, 1)], null), default));

        var big = await JoinAsync(ride, "big", new TicketQuantityRequest(ride.Child, 3));
        var small = await JoinAsync(ride, "small", new TicketQuantityRequest(ride.Child, 1));

        // Free exactly one place: the 3-person group at the head does not fit, so nobody is offered.
        await ride.Harness.CreateServices().Waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(26, null), default);
        Assert.Equal("Waiting", (await StatusAsync(ride, big.Reference)).Status);
        Assert.Equal("Waiting", (await StatusAsync(ride, small.Reference)).Status);

        // And a new public buyer cannot jump the line for that place.
        var jump = await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Orders.CreateOrderAsync("jump",
            new CreateOrderRequest(ride.SlotId, [new(ride.Child, 1)], null), default));
        Assert.Equal("waitlist_active", jump.Code);
        var availability = await ride.Harness.CreateServices().Catalog.GetTicketAvailabilityAsync(ride.EventId, ride.SlotId, default);
        Assert.All(availability.TicketTypes, t => Assert.Equal(0, t.Remaining));
        Assert.Equal(2, availability.WaitlistWaiting);

        // Two more places: the head group fits, then the next one does not, so it keeps waiting.
        await ride.Harness.CreateServices().Waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(28, null), default);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, big.Reference)).Status);
        Assert.Equal("Waiting", (await StatusAsync(ride, small.Reference)).Status);
        Assert.Equal(28, await LivePeopleAsync(ride));
    }

    [Fact]
    public async Task Expired_offer_returns_its_places_once_and_the_queue_advances()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 20));
        var bob = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Child, 10));
        await CancelFillerAsync(ride, filler);
        await ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default);
        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);
        Assert.Equal("Waiting", (await StatusAsync(ride, bob.Reference)).Status); // 20 + 10 > 25

        ride.Harness.Clock.Advance(TimeSpan.FromMinutes(ride.Harness.WaitlistOptions.OfferLifetimeMinutes + 1));
        // Two concurrent readers both notice the expiry; places must be returned exactly once.
        await Task.WhenAll(
            ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default),
            ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default));

        Assert.Equal("Expired", (await StatusAsync(ride, alice.Reference)).Status);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal(0, (await ride.Harness.InventoryAsync(ride.SlotId, ride.Adult)).Reserved);
        Assert.Equal(10, (await ride.Harness.InventoryAsync(ride.SlotId, ride.Child)).Reserved);
        var late = await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default));
        Assert.Equal("offer_expired", late.Code);
    }

    [Fact]
    public async Task Concurrent_accepts_create_one_order_and_offers_cannot_be_bought_by_others()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 20), new TicketQuantityRequest(ride.Child, 5));
        await CancelFillerAsync(ride, filler);
        await ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default);
        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);

        // The offer holds every place: a public buyer is refused.
        await Assert.ThrowsAsync<BookingException>(() => ride.Harness.CreateServices().Orders.CreateOrderAsync("public",
            new CreateOrderRequest(ride.SlotId, [new(ride.Child, 1)], null), default));

        var accepts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default))));

        Assert.Single(accepts.Distinct());
        await using var db = ride.Harness.CreateContext();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.RequestIdempotencyKey == $"waitlist-{alice.Reference:N}"));
        Assert.Equal(25, await LivePeopleAsync(ride));
    }

    [Fact]
    public async Task Cancelling_an_offer_or_letting_the_accepted_order_lapse_passes_places_on()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 20));
        var bob = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Adult, 20));
        var carol = await JoinAsync(ride, "carol", new TicketQuantityRequest(ride.Adult, 20));
        await CancelFillerAsync(ride, filler);
        await ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default);

        // Alice leaves (twice: harmless); Bob is offered next.
        await ride.Harness.CreateServices().Waitlist.CancelAsync(alice.Reference, default);
        await ride.Harness.CreateServices().Waitlist.CancelAsync(alice.Reference, default);
        Assert.Equal("Cancelled", (await StatusAsync(ride, alice.Reference)).Status);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal(20, (await ride.Harness.InventoryAsync(ride.SlotId, ride.Adult)).Reserved);

        // Bob accepts but never starts checkout; the abandoned order is released and Carol is offered.
        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(bob.Reference, default);
        var bobOrder = await ride.Harness.CreateServices().Waitlist.AcceptAsync(bob.Reference, default);
        ride.Harness.Clock.Advance(TimeSpan.FromMinutes(16));
        await ride.Harness.CreateServices().Catalog.GetTicketAvailabilityAsync(ride.EventId, ride.SlotId, default);

        Assert.Equal("Released", (await ride.Harness.OrderAsync(bobOrder)).Status.ToString());
        Assert.Equal("Lapsed", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, carol.Reference)).Status);
        Assert.Equal(20, (await ride.Harness.InventoryAsync(ride.SlotId, ride.Adult)).Reserved);
    }

    [Fact]
    public async Task Unknown_payment_outcome_keeps_the_places_and_does_not_advance_the_queue()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 20), new TicketQuantityRequest(ride.Child, 5));
        var bob = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Adult, 1));
        await CancelFillerAsync(ride, filler);
        await ride.Harness.CreateServices().Waitlist.PromoteAsync(ride.SlotId, default);
        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);
        var orderId = await ride.Harness.CreateServices().Waitlist.AcceptAsync(alice.Reference, default);
        await ride.Harness.CreateServices().Payments.StartCheckoutAsync(orderId, default);

        // Session lifetime passes but Stripe cannot be reached: nothing is released.
        ride.Harness.Clock.Advance(TimeSpan.FromHours(2));
        ride.Harness.Gateway.FailReads = true;
        await ride.Harness.CreateServices().Catalog.GetTicketAvailabilityAsync(ride.EventId, ride.SlotId, default);

        Assert.Equal("PendingPayment", (await ride.Harness.OrderAsync(orderId)).Status.ToString());
        Assert.Equal("Waiting", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal(25, await LivePeopleAsync(ride));
    }

    [Fact]
    public async Task Capacity_increase_feeds_the_queue_and_rejects_invalid_or_below_commitment_values()
    {
        var (ride, _) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Child, 2));
        var waitlist = ride.Harness.CreateServices().Waitlist;

        await Assert.ThrowsAsync<BookingException>(() => waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(24, null), default));
        await Assert.ThrowsAsync<BookingException>(() => waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(0, null), default));
        await Assert.ThrowsAsync<BookingException>(() => waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(null, [new(ride.Adult, 19)]), default));
        await Assert.ThrowsAsync<BookingException>(() => waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(null, []), default));
        Assert.Equal("Waiting", (await StatusAsync(ride, alice.Reference)).Status);

        await ride.Harness.CreateServices().Waitlist.IncreaseCapacityAsync(ride.SlotId, new IncreaseCapacityRequest(27, null), default);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, alice.Reference)).Status);
        var departures = await ride.Harness.CreateServices().Waitlist.GetDeparturesAsync(default);
        var departure = departures.Single(d => d.TimeSlotId == ride.SlotId);
        Assert.Equal((27, 27), (departure.SharedCapacity!.Value, departure.SharedCommitted));
    }

    [Fact]
    public async Task Operator_outbox_labels_states_and_demo_reset_closes_open_offers()
    {
        var (ride, filler) = await SoldOutHayrideAsync();
        await using var scope = ride.Harness;
        var alice = await JoinAsync(ride, "alice", new TicketQuantityRequest(ride.Adult, 2));
        var bob = await JoinAsync(ride, "bob", new TicketQuantityRequest(ride.Adult, 20), new TicketQuantityRequest(ride.Child, 4));
        await CancelFillerAsync(ride, filler);

        var outbox = await ride.Harness.CreateServices().Waitlist.GetOutboxAsync(default);
        Assert.Equal(["OfferReadyToNotify", "Queued"], outbox.Select(e => e.OperatorStatus));
        Assert.EndsWith($"/?waitlist={alice.Reference}", outbox[0].PrivateLink);
        await ride.Harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);
        Assert.Equal("ManuallyNotified", (await ride.Harness.CreateServices().Waitlist.GetOutboxAsync(default))[0].OperatorStatus);

        var reset = await ride.Harness.CreateDemoReset(new BookingOperationGate()).ResetAsync(default);

        Assert.True(reset.Completed);
        await using (var db = ride.Harness.CreateContext())
        {
            // Directly after the reset (before any request moves the queue): offer closed, ledger empty, Bob still in line.
            Assert.Equal(WaitlistStatus.Cancelled, (await db.WaitlistEntries.SingleAsync(w => w.PublicReference == alice.Reference)).Status);
            Assert.Equal(0, await db.SlotInventories.SumAsync(i => i.Reserved + i.Sold));
            Assert.Equal(WaitlistStatus.Waiting, (await db.WaitlistEntries.SingleAsync(w => w.PublicReference == bob.Reference)).Status);
        }

        // Review 001: a customer's own status refresh advances the queue, so Bob is offered the freed places.
        Assert.Equal("Cancelled", (await StatusAsync(ride, alice.Reference)).Status);
        Assert.Equal("OfferPendingContact", (await StatusAsync(ride, bob.Reference)).Status);
        Assert.Equal(24, await LivePeopleAsync(ride));
    }
}
