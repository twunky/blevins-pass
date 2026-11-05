using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>
/// Regression evidence for the reported "wrong time on a confirmation". The server stores and returns
/// UTC instants (ISO-8601 with "Z") and converts venue wall-clock times with the America/New_York rules,
/// independent of the server's or purchaser's own time zone. The browser formats those instants in venue
/// time (client/scripts/check-venue-time.ts covers that half).
/// </summary>
public class VenueTimeTests
{
    [Theory]
    [InlineData(2026, 7, 15, 10, 0, "2026-07-15T14:00:00Z")]  // summer, EDT (UTC-4)
    [InlineData(2026, 12, 15, 10, 0, "2026-12-15T15:00:00Z")] // winter, EST (UTC-5)
    [InlineData(2026, 3, 8, 10, 0, "2026-03-08T14:00:00Z")]   // day DST starts
    [InlineData(2026, 11, 1, 10, 0, "2026-11-01T15:00:00Z")]  // day DST ends
    [InlineData(2026, 12, 31, 23, 30, "2027-01-01T04:30:00Z")] // venue evening is the next UTC day (and year)
    public void Venue_wall_clock_times_convert_to_the_correct_utc_instant(int year, int month, int day, int hour, int minute, string expectedUtc)
    {
        var utc = SeedData.VenueLocalToUtc(new DateTime(year, month, day, hour, minute, 0));
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(expectedUtc, utc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [Fact]
    public async Task Seed_dates_follow_the_venue_calendar_day_not_the_utc_day()
    {
        // 03:30 UTC on Nov 1 is still Oct 31 at the venue (11:30 p.m. EDT); the first event is a week after the venue date.
        await using var harness = await TestHarnessAt(new DateTime(2026, 11, 1, 3, 30, 0, DateTimeKind.Utc));
        var (_, slotId, _) = await harness.GeneralAdmissionSlotAsync("Harvest Lantern Walk");
        await using var db = harness.CreateContext();
        var start = (await db.TimeSlots.SingleAsync(s => s.Id == slotId)).StartUtc;

        // Nov 7 2026 at 5:00 p.m. EST = 22:00 UTC.
        Assert.Equal(new DateTime(2026, 11, 7, 22, 0, 0, DateTimeKind.Utc), start);
    }

    [Fact]
    public async Task Timestamps_read_from_sqlite_serialize_as_utc_with_z()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (eventId, slotId, inventory) = await harness.GeneralAdmissionSlotAsync("Harvest Lantern Walk");
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("tz",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 1)], null), default);

        // Read back through a new connection, exactly as the API does for a confirmation page.
        var reloaded = await harness.CreateServices().Orders.GetOrderAsync(order.Id);
        var json = JsonSerializer.Serialize(reloaded, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        var slotStart = document.RootElement.GetProperty("slotStartUtc").GetString()!;

        Assert.EndsWith("Z", slotStart);
        Assert.Equal(DateTimeKind.Utc, reloaded.SlotStartUtc.Kind);
        var events = await harness.CreateServices().Catalog.GetEventsAsync(default);
        Assert.Equal(reloaded.SlotStartUtc, events.Single(e => e.Id == eventId).TimeSlots.Single(s => s.Id == slotId).StartUtc);
    }

    private static async Task<TestHarness> TestHarnessAt(DateTime utcNow) => await TestHarness.CreateAsync(utcNow);
}
