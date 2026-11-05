using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;

namespace EventRegistration.Tests;

public class GeneralAdmissionOrderTests
{
    private const string LanternWalk = "Harvest Lantern Walk";
    private const string Hayride = "Pumpkin Patch Hayride";

    [Fact]
    public async Task Order_is_priced_by_the_server_and_holds_inventory()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var adult = inventory[0];
        var child = inventory[1];

        var (order, created) = await harness.CreateServices().Orders.CreateOrderAsync("key-1",
            new CreateOrderRequest(slotId, [new(adult.TicketTypeId, 2), new(child.TicketTypeId, 1)], null), default);

        Assert.True(created);
        Assert.Equal("PendingCheckout", order.Status);
        Assert.Equal(2 * 2400 + 1200, order.TotalCents);
        Assert.Equal(2, (await harness.InventoryAsync(slotId, adult.TicketTypeId)).Reserved);
        Assert.Equal(1, (await harness.InventoryAsync(slotId, child.TicketTypeId)).Reserved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Invalid_quantities_are_rejected(int quantity)
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("key",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, quantity)], null), default));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Empty_duplicate_and_mixed_selections_are_rejected()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var orders = harness.CreateServices().Orders;
        var typeId = inventory[0].TicketTypeId;

        CreateOrderRequest[] invalidRequests =
        [
            new(slotId, [], null),
            new(slotId, [new(typeId, 1), new(typeId, 1)], null),
            new(slotId, [new(typeId, 1)], [1]),
        ];

        foreach (var request in invalidRequests)
        {
            var error = await Assert.ThrowsAsync<BookingException>(() => orders.CreateOrderAsync(Guid.NewGuid().ToString(), request, default));
            Assert.Equal(400, error.StatusCode);
        }
    }

    [Fact]
    public async Task Ticket_type_from_another_event_is_rejected()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, lanternSlot, _) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var (_, _, hayrideInventory) = await harness.GeneralAdmissionSlotAsync(Hayride);

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("key",
            new CreateOrderRequest(lanternSlot, [new(hayrideInventory[0].TicketTypeId, 1)], null), default));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Over_capacity_request_is_rejected_without_changing_inventory()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        var familyWagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon"); // capacity 3

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("key",
            new CreateOrderRequest(slotId, [new(familyWagon.TicketTypeId, 4)], null), default));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, familyWagon.TicketTypeId)).Reserved);
    }

    [Fact]
    public async Task Multi_line_order_is_all_or_nothing()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        var adult = inventory.Single(i => i.TicketType!.Name == "Adult");
        var familyWagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon");

        await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("key",
            new CreateOrderRequest(slotId, [new(adult.TicketTypeId, 2), new(familyWagon.TicketTypeId, 4)], null), default));

        Assert.Equal(0, (await harness.InventoryAsync(slotId, adult.TicketTypeId)).Reserved);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, familyWagon.TicketTypeId)).Reserved);
    }

    [Fact]
    public async Task Capacity_is_independent_per_slot_and_ticket_type()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, firstSlot, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride, slotIndex: 0);
        var (_, secondSlot, _) = await harness.GeneralAdmissionSlotAsync(Hayride, slotIndex: 1);
        var familyWagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon");
        var adult = inventory.Single(i => i.TicketType!.Name == "Adult");

        // Sell out Family Wagon in the first slot.
        await harness.CreateServices().Orders.CreateOrderAsync("first",
            new CreateOrderRequest(firstSlot, [new(familyWagon.TicketTypeId, 3)], null), default);

        // The same type in another slot, and another type in the same slot, are unaffected.
        var (secondSlotOrder, _) = await harness.CreateServices().Orders.CreateOrderAsync("second",
            new CreateOrderRequest(secondSlot, [new(familyWagon.TicketTypeId, 3)], null), default);
        var (sameSlotOtherType, _) = await harness.CreateServices().Orders.CreateOrderAsync("third",
            new CreateOrderRequest(firstSlot, [new(adult.TicketTypeId, 5)], null), default);

        Assert.Equal("PendingCheckout", secondSlotOrder.Status);
        Assert.Equal("PendingCheckout", sameSlotOtherType.Status);
        var availability = await harness.CreateServices().Catalog.GetTicketAvailabilityAsync(eventId, firstSlot, default);
        Assert.Equal(0, availability.TicketTypes.Single(t => t.TicketTypeId == familyWagon.TicketTypeId).Remaining);
        // Phase 2 owner rule: the hayride also has a shared limit of 25 people per departure, so the
        // 3 wagon + 5 adult tickets leave 17 places (the Adult type cap alone would leave 20).
        Assert.Equal(17, availability.TicketTypes.Single(t => t.TicketTypeId == adult.TicketTypeId).Remaining);
        Assert.Equal(17, availability.SharedRemaining);
    }

    [Fact]
    public async Task Competing_requests_for_the_final_ticket_sell_it_exactly_once()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        var familyWagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon");
        await harness.CreateServices().Orders.CreateOrderAsync("setup",
            new CreateOrderRequest(slotId, [new(familyWagon.TicketTypeId, 2)], null), default);

        // Ten concurrent buyers, each on its own connection, race for the one remaining wagon.
        var attempts = Enumerable.Range(0, 10).Select(async index =>
        {
            try
            {
                await harness.CreateServices().Orders.CreateOrderAsync($"race-{index}",
                    new CreateOrderRequest(slotId, [new(familyWagon.TicketTypeId, 1)], null), default);
                return true;
            }
            catch (BookingException exception) when (exception.StatusCode == 409)
            {
                return false;
            }
        }).ToList();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(succeeded => succeeded));
        var final = await harness.InventoryAsync(slotId, familyWagon.TicketTypeId);
        Assert.Equal(3, final.Reserved + final.Sold);
    }

    [Fact]
    public async Task Retried_request_reuses_its_order_and_changed_selection_conflicts()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        var typeId = inventory[0].TicketTypeId;

        var (first, firstCreated) = await harness.CreateServices().Orders.CreateOrderAsync("same-key",
            new CreateOrderRequest(slotId, [new(typeId, 2)], null), default);
        var (retry, retryCreated) = await harness.CreateServices().Orders.CreateOrderAsync("same-key",
            new CreateOrderRequest(slotId, [new(typeId, 2)], null), default);
        var changed = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("same-key",
            new CreateOrderRequest(slotId, [new(typeId, 3)], null), default));

        Assert.True(firstCreated);
        Assert.False(retryCreated);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(409, changed.StatusCode);
        Assert.Equal(2, (await harness.InventoryAsync(slotId, typeId)).Reserved);
    }

    [Fact]
    public async Task Closed_or_past_slot_cannot_be_booked()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(LanternWalk);
        harness.Clock.Advance(TimeSpan.FromDays(30));

        var error = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Orders.CreateOrderAsync("key",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 1)], null), default));

        Assert.Equal(409, error.StatusCode);
    }
}
