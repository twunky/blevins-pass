using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>
/// Owner-approved queue policy: on independent-capacity departures (no shared limit) FIFO applies per
/// ticket type, so a VIP waiter never blocks General Admission. Shared departures keep departure-wide FIFO.
/// </summary>
public class PartialSelloutTests
{
    private const string Cider = "Orchard Cider Tasting"; // General Admission 30, VIP 2, no shared limit

    private static async Task<(TestHarness Harness, int EventId, int SlotId, int Ga, int Vip)> CiderAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (eventId, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Cider);
        return (harness, eventId, slotId,
            inventory.Single(i => i.TicketType!.Name == "General Admission").TicketTypeId,
            inventory.Single(i => i.TicketType!.Name == "VIP").TicketTypeId);
    }

    private static Task<(WaitlistStatusDto Entry, bool Created)> JoinAsync(TestHarness harness, string key, int slotId, params TicketQuantityRequest[] tickets) =>
        harness.CreateServices().Waitlist.JoinAsync(key, new JoinWaitlistRequest(slotId, $"{key}@example.com", tickets), default);

    private static Task<(OrderDto Order, bool Created)> BuyAsync(TestHarness harness, string key, int slotId, params TicketQuantityRequest[] tickets) =>
        harness.CreateServices().Orders.CreateOrderAsync(key, new CreateOrderRequest(slotId, tickets, null), default);

    [Fact]
    public async Task Vip_waiter_does_not_block_general_admission()
    {
        var (harness, eventId, slotId, ga, vip) = await CiderAsync();
        await using var scope = harness;
        await BuyAsync(harness, "vip-sellout", slotId, new TicketQuantityRequest(vip, 2));

        var (entry, created) = await JoinAsync(harness, "vip-fan", slotId, new TicketQuantityRequest(vip, 1));
        var (gaOrder, _) = await BuyAsync(harness, "ga-buyer", slotId, new TicketQuantityRequest(ga, 2));
        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, slotId, default);

        Assert.True(created);
        Assert.Equal(("Waiting", 1), (entry.Status, entry.PositionInLine));
        Assert.Equal("PendingCheckout", gaOrder.Status);
        Assert.Equal((28, 0), (availability.TicketTypes.Single(t => t.TicketTypeId == ga).Remaining, availability.TicketTypes.Single(t => t.TicketTypeId == vip).Remaining));
        Assert.Equal((0, 1), (availability.TicketTypes.Single(t => t.TicketTypeId == ga).WaitlistWaiting, availability.TicketTypes.Single(t => t.TicketTypeId == vip).WaitlistWaiting));
        Assert.Equal(0, harness.Gateway.CreateCalls);

        // A VIP buyer cannot jump the VIP line; GA is still not a waitlist matter (F1), and requests name one type.
        await Assert.ThrowsAsync<BookingException>(() => BuyAsync(harness, "vip-jump", slotId, new TicketQuantityRequest(vip, 1)));
        Assert.Equal("tickets_available", (await Assert.ThrowsAsync<BookingException>(() => JoinAsync(harness, "ga-join", slotId, new TicketQuantityRequest(ga, 1)))).Code);
        Assert.Equal(400, (await Assert.ThrowsAsync<BookingException>(() => JoinAsync(harness, "both", slotId, new TicketQuantityRequest(ga, 1), new TicketQuantityRequest(vip, 1)))).StatusCode);
    }

    [Fact]
    public async Task Competing_vip_requests_keep_fifo_including_head_of_line_within_the_type()
    {
        var (harness, _, slotId, ga, vip) = await CiderAsync();
        await using var scope = harness;
        var (first, _) = await BuyAsync(harness, "vip-1", slotId, new TicketQuantityRequest(vip, 1));
        await BuyAsync(harness, "vip-2", slotId, new TicketQuantityRequest(vip, 1));
        var (alice, _) = await JoinAsync(harness, "alice", slotId, new TicketQuantityRequest(vip, 2));
        var (bob, _) = await JoinAsync(harness, "bob", slotId, new TicketQuantityRequest(vip, 1));
        Assert.Equal(2, bob.PositionInLine);

        // One VIP place frees: Alice (2) does not fit, so Bob (1) behind her is not offered.
        await harness.CreateServices().Payments.ConfirmAsync(first.Id, cancel: true, default);
        Assert.Equal("Waiting", (await harness.CreateServices().Waitlist.GetStatusAsync(alice.Reference, default)).Status);
        Assert.Equal("Waiting", (await harness.CreateServices().Waitlist.GetStatusAsync(bob.Reference, default)).Status);
        Assert.Equal(1, (await harness.InventoryAsync(slotId, vip)).Reserved);

        // GA stays purchasable throughout.
        await BuyAsync(harness, "ga", slotId, new TicketQuantityRequest(ga, 3));
        Assert.Equal(3, (await harness.InventoryAsync(slotId, ga)).Reserved);
    }

    [Fact]
    public async Task Existing_multi_type_entry_stays_atomic_and_protects_both_types()
    {
        var (harness, _, slotId, ga, vip) = await CiderAsync();
        await using var scope = harness;
        var (vipOrder, _) = await BuyAsync(harness, "vip-sellout", slotId, new TicketQuantityRequest(vip, 2));
        await using (var db = harness.CreateContext())
        {
            // A Phase 2 entry created before per-type requests existed: GA 2 + VIP 1, linked.
            db.WaitlistEntries.Add(new WaitlistEntry
            {
                PublicReference = Guid.NewGuid(), EventId = (await db.TimeSlots.SingleAsync(s => s.Id == slotId)).EventId, TimeSlotId = slotId,
                CustomerEmail = "legacy@example.com", Status = WaitlistStatus.Waiting, RequestIdempotencyKey = "legacy", RequestFingerprint = "legacy",
                CreatedUtc = harness.Clock.UtcNow, Items = [new WaitlistItem { TicketTypeId = ga, Quantity = 2 }, new WaitlistItem { TicketTypeId = vip, Quantity = 1 }],
            });
            await db.SaveChangesAsync();
        }

        // GA is protected by the older linked entry: no public GA sale, and a GA-only request queues behind it.
        Assert.Equal("waitlist_active", (await Assert.ThrowsAsync<BookingException>(() => BuyAsync(harness, "ga-buyer", slotId, new TicketQuantityRequest(ga, 1)))).Code);
        var (gaOnly, _) = await JoinAsync(harness, "ga-only", slotId, new TicketQuantityRequest(ga, 1));
        Assert.Equal("Waiting", gaOnly.Status);
        Assert.Equal((0, 0), ((await harness.InventoryAsync(slotId, ga)).Reserved, 0));

        // VIP frees: the linked entry is offered all-or-nothing, then the GA-only request.
        await harness.CreateServices().Payments.ConfirmAsync(vipOrder.Id, cancel: true, default);
        await harness.CreateServices().Waitlist.PromoteAsync(slotId, default);
        Assert.Equal((3, 1), ((await harness.InventoryAsync(slotId, ga)).Reserved, (await harness.InventoryAsync(slotId, vip)).Reserved));
        Assert.Equal("OfferPendingContact", (await harness.CreateServices().Waitlist.GetStatusAsync(gaOnly.Reference, default)).Status);
    }

    [Fact]
    public async Task Complete_sellout_allows_a_request_per_type_and_rejects_closed_departures()
    {
        var (harness, _, slotId, ga, vip) = await CiderAsync();
        await using var scope = harness;
        await BuyAsync(harness, "all", slotId, new TicketQuantityRequest(ga, 20));
        await BuyAsync(harness, "rest", slotId, new TicketQuantityRequest(ga, 10), new TicketQuantityRequest(vip, 2));

        Assert.True((await JoinAsync(harness, "ga-w", slotId, new TicketQuantityRequest(ga, 2))).Created);
        Assert.True((await JoinAsync(harness, "vip-w", slotId, new TicketQuantityRequest(vip, 1))).Created);

        harness.Clock.Advance(TimeSpan.FromDays(60));
        Assert.Equal("slot_closed", (await Assert.ThrowsAsync<BookingException>(() => JoinAsync(harness, "late", slotId, new TicketQuantityRequest(vip, 1)))).Code);
        Assert.Equal(0, harness.Gateway.CreateCalls);
    }
}
