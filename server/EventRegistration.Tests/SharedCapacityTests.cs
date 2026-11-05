using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>
/// Phase 2: the hayride holds 25 people per departure across Adult, Child and Family Wagon tickets
/// (each ticket is one person), while each type keeps its own cap (25/25/3).
/// </summary>
public class SharedCapacityTests
{
    private const string Hayride = "Pumpkin Patch Hayride";
    private const string LanternWalk = "Harvest Lantern Walk";

    private static async Task<(TestHarness Harness, int EventId, int SlotId, int Adult, int Child, int Wagon)> HayrideAsync(int slotIndex = 0)
    {
        var harness = await TestHarness.CreateAsync();
        var (eventId, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride, slotIndex);
        int Id(string name) => inventory.Single(i => i.TicketType!.Name == name).TicketTypeId;
        return (harness, eventId, slotId, Id("Adult"), Id("Child"), Id("Family Wagon"));
    }

    private static Task<(OrderDto Order, bool Created)> OrderAsync(TestHarness harness, int slotId, params TicketQuantityRequest[] tickets) =>
        harness.CreateServices().Orders.CreateOrderAsync(Guid.NewGuid().ToString(), new CreateOrderRequest(slotId, tickets, null), default);

    private static async Task<int> LivePeopleAsync(TestHarness harness, int slotId)
    {
        await using var db = harness.CreateContext();
        return await db.SlotInventories.Where(i => i.TimeSlotId == slotId).SumAsync(i => i.Reserved + i.Sold);
    }

    [Fact]
    public async Task Mixed_ticket_types_stop_at_the_shared_limit_of_25()
    {
        var (harness, eventId, slotId, adult, child, wagon) = await HayrideAsync();
        await using var scope = harness;

        await OrderAsync(harness, slotId, new TicketQuantityRequest(adult, 10), new(child, 10));
        await OrderAsync(harness, slotId, new TicketQuantityRequest(wagon, 3), new(child, 2));

        // Every type is below its own cap (12/25 children, 3/3 wagons, 10/25 adults) but the departure is full.
        var refused = await Assert.ThrowsAsync<BookingException>(() => OrderAsync(harness, slotId, new TicketQuantityRequest(adult, 1)));
        Assert.Equal("insufficient_capacity", refused.Code);
        Assert.Equal(25, await LivePeopleAsync(harness, slotId));

        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, slotId, default);
        Assert.Equal(25, availability.SharedCapacity);
        Assert.Equal(0, availability.SharedRemaining);
        Assert.All(availability.TicketTypes, t => Assert.Equal(0, t.Remaining));
    }

    [Fact]
    public async Task Per_type_caps_still_apply_when_shared_places_remain()
    {
        var (harness, _, slotId, _, _, wagon) = await HayrideAsync();
        await using var scope = harness;

        var refused = await Assert.ThrowsAsync<BookingException>(() => OrderAsync(harness, slotId, new TicketQuantityRequest(wagon, 4)));
        Assert.Equal("insufficient_capacity", refused.Code);
        Assert.Equal(0, await LivePeopleAsync(harness, slotId));
    }

    [Fact]
    public async Task A_mixed_order_that_overflows_on_a_later_line_holds_nothing()
    {
        var (harness, _, slotId, adult, child, _) = await HayrideAsync();
        await using var scope = harness;
        await OrderAsync(harness, slotId, new TicketQuantityRequest(adult, 15));

        // Adults (5) fit on their own; the 6 children then exceed the shared limit (15 + 5 + 6 = 26).
        await Assert.ThrowsAsync<BookingException>(() => OrderAsync(harness, slotId, new TicketQuantityRequest(adult, 5), new(child, 6)));

        Assert.Equal(15, (await harness.InventoryAsync(slotId, adult)).Reserved);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, child)).Reserved);
    }

    [Fact]
    public async Task Concurrent_mixed_orders_never_exceed_the_shared_limit()
    {
        var (harness, _, slotId, adult, child, _) = await HayrideAsync();
        await using var scope = harness;

        // 12 mixed groups of 5 people, each on its own connection, released at the same moment.
        // Per-type caps alone would allow far more (adults <= 15 and children <= 20 here), so only the shared limit stops them.
        var start = new TaskCompletionSource();
        var attempts = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            var services = harness.CreateServices();
            await start.Task;
            try
            {
                TicketQuantityRequest[] group = index % 2 == 0 ? [new(adult, 3), new(child, 2)] : [new(child, 4), new(adult, 1)];
                await services.Orders.CreateOrderAsync($"race-{index}", new CreateOrderRequest(slotId, group, null), default);
                return true;
            }
            catch (BookingException exception) when (exception.Code == "insufficient_capacity")
            {
                return false;
            }
        })).ToList();
        start.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(succeeded => succeeded));
        Assert.Equal(25, await LivePeopleAsync(harness, slotId));
    }

    [Fact]
    public async Task Departures_and_other_events_are_independent()
    {
        var (harness, eventId, firstSlot, adult, child, _) = await HayrideAsync();
        await using var scope = harness;
        var (_, secondSlot, _) = await harness.GeneralAdmissionSlotAsync(Hayride, slotIndex: 1);
        await OrderAsync(harness, firstSlot, new TicketQuantityRequest(adult, 20), new(child, 5));

        var second = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, secondSlot, default);
        Assert.Equal(25, second.SharedRemaining);

        // An event without a shared limit keeps only its per-type caps.
        var (lanternEvent, lanternSlot, lanternInventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        await OrderAsync(harness, lanternSlot, new TicketQuantityRequest(lanternInventory[0].TicketTypeId, 20), new(lanternInventory[1].TicketTypeId, 15));
        var lantern = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(lanternEvent, lanternSlot, default);
        Assert.Null(lantern.SharedCapacity);
        Assert.Null(lantern.SharedRemaining);
    }

    [Fact]
    public async Task Confirmed_tickets_keep_counting_and_released_holds_return_shared_places()
    {
        var (harness, eventId, slotId, adult, child, _) = await HayrideAsync();
        await using var scope = harness;

        var (paid, _) = await OrderAsync(harness, slotId, new TicketQuantityRequest(adult, 10));
        await harness.CreateServices().Payments.StartCheckoutAsync(paid.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(paid.Id)).StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(paid.Id, cancel: false, default);
        await harness.CreateServices().Payments.ConfirmAsync(paid.Id, cancel: false, default); // idempotent

        var (cancelled, _) = await OrderAsync(harness, slotId, new TicketQuantityRequest(child, 15));
        await harness.CreateServices().Payments.ConfirmAsync(cancelled.Id, cancel: true, default);
        await harness.CreateServices().Payments.ConfirmAsync(cancelled.Id, cancel: true, default); // no double release

        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, slotId, default);
        Assert.Equal(15, availability.SharedRemaining);
        Assert.Equal(10, (await harness.InventoryAsync(slotId, adult)).Sold);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, child)).Reserved);
    }

    [Fact]
    public async Task Legacy_overfull_departure_fails_closed_without_touching_history()
    {
        var (harness, eventId, slotId, adult, child, wagon) = await HayrideAsync();
        await using var scope = harness;
        // Simulates Phase 1 sales: every type at its own cap, 53 people on a 25-person departure.
        await using (var db = harness.CreateContext())
        {
            await db.SlotInventories.Where(i => i.TimeSlotId == slotId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Sold, i => i.Capacity));
        }

        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, slotId, default);
        Assert.Equal(0, availability.SharedRemaining);
        Assert.All(availability.TicketTypes, t => Assert.Equal(0, t.Remaining));
        await Assert.ThrowsAsync<BookingException>(() => OrderAsync(harness, slotId, new TicketQuantityRequest(child, 1)));

        // Even after a type cap is raised, the shared limit still refuses new tickets.
        await harness.CreateServices().Waitlist.IncreaseCapacityAsync(slotId, new IncreaseCapacityRequest(null, [new(child, 30)]), default);
        await Assert.ThrowsAsync<BookingException>(() => OrderAsync(harness, slotId, new TicketQuantityRequest(child, 1)));
        Assert.Equal(53, await LivePeopleAsync(harness, slotId));
        Assert.Equal(3, (await harness.InventoryAsync(slotId, wagon)).Sold);
        Assert.Equal(25, (await harness.InventoryAsync(slotId, adult)).Sold);
    }
}
