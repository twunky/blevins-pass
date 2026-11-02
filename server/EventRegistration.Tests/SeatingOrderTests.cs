using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;

namespace EventRegistration.Tests;

public class SeatingOrderTests
{
    [Fact]
    public async Task Seed_has_eighteen_seats_in_three_priced_sections()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, slotIds, seats) = await harness.ConcertAsync();

        var chart = await harness.CreateServices().Catalog.GetSeatingChartAsync(eventId, default);

        Assert.Equal(18, seats.Count);
        Assert.Equal(2, slotIds.Length);
        Assert.Equal(new[] { "Orchestra Left", "VIP Center", "Orchestra Right" }, chart.Sections.Select(s => s.Name));
        Assert.All(chart.Sections, section => Assert.Equal(6, section.Seats.Count));
        Assert.Contains(seats, s => s.Label == "VIP-CENTER-A-01");
    }

    [Fact]
    public async Task Seat_order_uses_section_prices()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotIds, seats) = await harness.ConcertAsync();
        var vip = seats.First(s => s.SeatingSection!.Code == "VIP-CENTER");
        var left = seats.First(s => s.SeatingSection!.Code == "ORCHESTRA-LEFT");

        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("seat-key",
            new CreateOrderRequest(slotIds[0], null, [vip.Id, left.Id]), default);

        Assert.Equal(12500 + 8500, order.TotalCents);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public async Task Held_or_sold_seat_cannot_be_claimed_again_for_the_same_performance()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, slotIds, seats) = await harness.ConcertAsync();
        var seat = seats[0];

        var (first, _) = await harness.CreateServices().Orders.CreateOrderAsync("a", new CreateOrderRequest(slotIds[0], null, [seat.Id]), default);
        var heldError = await Assert.ThrowsAsync<BookingException>(() =>
            harness.CreateServices().Orders.CreateOrderAsync("b", new CreateOrderRequest(slotIds[0], null, [seat.Id]), default));

        await harness.CreateServices().Payments.StartCheckoutAsync(first.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(first.Id)).StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(first.Id, cancel: false, default);
        var soldError = await Assert.ThrowsAsync<BookingException>(() =>
            harness.CreateServices().Orders.CreateOrderAsync("c", new CreateOrderRequest(slotIds[0], null, [seat.Id]), default));

        var availability = await harness.CreateServices().Catalog.GetSeatingAvailabilityAsync(eventId, slotIds[0], default);
        Assert.Equal(409, heldError.StatusCode);
        Assert.Equal(409, soldError.StatusCode);
        Assert.Equal("sold", availability.Seats.Single(s => s.SeatId == seat.Id).Status);
    }

    [Fact]
    public async Task Same_seat_sells_independently_for_each_performance()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, slotIds, seats) = await harness.ConcertAsync();
        var seat = seats[0];

        var (matinee, _) = await harness.CreateServices().Orders.CreateOrderAsync("m", new CreateOrderRequest(slotIds[0], null, [seat.Id]), default);
        var (evening, _) = await harness.CreateServices().Orders.CreateOrderAsync("e", new CreateOrderRequest(slotIds[1], null, [seat.Id]), default);

        var eveningAvailability = await harness.CreateServices().Catalog.GetSeatingAvailabilityAsync(eventId, slotIds[1], default);
        Assert.Equal("PendingCheckout", matinee.Status);
        Assert.Equal("PendingCheckout", evening.Status);
        Assert.Equal("held", eveningAvailability.Seats.Single(s => s.SeatId == seat.Id).Status);
        Assert.Equal(17, eveningAvailability.Sections.Sum(s => s.Remaining));
    }

    [Fact]
    public async Task Multi_seat_claim_is_atomic()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, slotIds, seats) = await harness.ConcertAsync();

        await harness.CreateServices().Orders.CreateOrderAsync("taken", new CreateOrderRequest(slotIds[0], null, [seats[1].Id]), default);
        await Assert.ThrowsAsync<BookingException>(() =>
            harness.CreateServices().Orders.CreateOrderAsync("mixed", new CreateOrderRequest(slotIds[0], null, [seats[0].Id, seats[1].Id]), default));

        var availability = await harness.CreateServices().Catalog.GetSeatingAvailabilityAsync(eventId, slotIds[0], default);
        Assert.Equal("available", availability.Seats.Single(s => s.SeatId == seats[0].Id).Status);
    }

    [Fact]
    public async Task Released_seat_can_be_rebooked()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotIds, seats) = await harness.ConcertAsync();
        var (first, _) = await harness.CreateServices().Orders.CreateOrderAsync("first", new CreateOrderRequest(slotIds[0], null, [seats[0].Id]), default);

        await harness.CreateServices().Payments.ConfirmAsync(first.Id, cancel: true, default);
        var (second, created) = await harness.CreateServices().Orders.CreateOrderAsync("second", new CreateOrderRequest(slotIds[0], null, [seats[0].Id]), default);

        Assert.Equal("Released", (await harness.OrderAsync(first.Id)).Status.ToString());
        Assert.True(created);
        Assert.Equal("PendingCheckout", second.Status);
    }

    [Fact]
    public async Task Seat_from_another_event_is_rejected()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotIds, _) = await harness.ConcertAsync();

        var error = await Assert.ThrowsAsync<BookingException>(() =>
            harness.CreateServices().Orders.CreateOrderAsync("bad", new CreateOrderRequest(slotIds[0], null, [99999]), default));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Concurrent_claims_for_one_seat_succeed_exactly_once()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (_, slotIds, seats) = await harness.ConcertAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            try
            {
                await harness.CreateServices().Orders.CreateOrderAsync($"race-{index}",
                    new CreateOrderRequest(slotIds[0], null, [seats[5].Id]), default);
                return true;
            }
            catch (BookingException exception) when (exception.StatusCode == 409)
            {
                return false;
            }
        }));

        Assert.Equal(1, results.Count(succeeded => succeeded));
    }
}
