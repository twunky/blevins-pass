using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Data;

/// <summary>
/// Idempotent startup seed. Runs only when the database has no events, so restarting the API
/// never resets purchases or settings. Dates are relative to the first initialization.
/// </summary>
public static class SeedData
{
    private static readonly TimeZoneInfo VenueTimeZone = FindVenueTimeZone();

    /// <summary>IANA id works on Linux/macOS and modern Windows (ICU); the Windows id is a fallback.</summary>
    private static TimeZoneInfo FindVenueTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    public static async Task EnsureSeededAsync(AppDbContext db, DateTime utcNow)
    {
        if (!await db.SiteSettings.AnyAsync())
        {
            db.SiteSettings.Add(new SiteSettings { Id = 1, ActiveTheme = ThemeName.Fall });
            await db.SaveChangesAsync();
        }

        if (await db.Events.AnyAsync())
        {
            return;
        }

        // First event day is one week out (venue-local date) so every seeded slot is in the future.
        var firstDay = TimeZoneInfo.ConvertTimeFromUtc(utcNow, VenueTimeZone).Date.AddDays(7);

        AddGeneralAdmissionEvent(db, firstDay.AddDays(0),
            "Harvest Lantern Walk",
            "A guided evening stroll through the orchard by lantern light, with cider at the barn.",
            "Brushfire Orchard Trail", "/images/lantern-walk.svg",
            slotStarts: [(17, 0), (19, 0)], durationMinutes: 90,
            tickets: [("Adult", "Ages 13 and up", 2400, 20), ("Child", "Ages 3-12", 1200, 15)]);

        AddGeneralAdmissionEvent(db, firstDay.AddDays(2),
            "Pumpkin Patch Hayride",
            "Ride the wagon to the patch and pick your own pumpkin to take home.",
            "Brushfire Farm Fields", "/images/hayride.svg",
            slotStarts: [(10, 0), (13, 0), (15, 30)], durationMinutes: 75,
            tickets: [("Adult", "Ages 13 and up", 1800, 25), ("Child", "Ages 3-12", 1000, 25), ("Family Wagon", "Private wagon seating for up to 5", 6500, 3)]);

        AddGeneralAdmissionEvent(db, firstDay.AddDays(5),
            "Orchard Cider Tasting",
            "Sample small-batch ciders paired with local cheese in the cellar barn. 21+.",
            "Cellar Barn", "/images/cider.svg",
            slotStarts: [(14, 0), (18, 0)], durationMinutes: 120,
            tickets: [("General Admission", "Five tasting pours", 3000, 30), ("VIP", "Reserved table and a take-home bottle", 5500, 2)]);

        AddGeneralAdmissionEvent(db, firstDay.AddDays(9),
            "Winter Market & Skating",
            "Browse the holiday makers' market and skate the pond rink. Skate rental included.",
            "Village Green", "/images/market.svg",
            slotStarts: [(11, 0), (15, 0)], durationMinutes: 180,
            tickets: [("Adult", "Ages 13 and up", 2000, 40), ("Child", "Ages 3-12", 1200, 30)]);

        AddGeneralAdmissionEvent(db, firstDay.AddDays(12),
            "Snowlight Forest Trail",
            "A mile-long woodland trail glowing with thousands of lights and warming fires.",
            "North Woods", "/images/forest-trail.svg",
            slotStarts: [(17, 30), (19, 30)], durationMinutes: 90,
            tickets: [("General Admission", "Timed trail entry", 2600, 35), ("VIP Fireside", "Trail entry plus reserved fire pit and cocoa", 4800, 6)]);

        AddWinterLightsConcert(db, firstDay.AddDays(14));

        await db.SaveChangesAsync();
    }

    private static void AddGeneralAdmissionEvent(
        AppDbContext db,
        DateTime localDay,
        string name,
        string description,
        string venue,
        string imagePath,
        (int Hour, int Minute)[] slotStarts,
        int durationMinutes,
        (string Name, string Description, int PriceCents, int Capacity)[] tickets)
    {
        var newEvent = new Event
        {
            Name = name,
            Description = description,
            Venue = venue,
            ImagePath = imagePath,
            RegistrationType = RegistrationType.GeneralAdmission,
        };
        db.Events.Add(newEvent);

        var ticketTypes = tickets
            .Select(t => new TicketType { Event = newEvent, Name = t.Name, Description = t.Description, PriceCents = t.PriceCents })
            .ToList();
        db.TicketTypes.AddRange(ticketTypes);

        foreach (var (hour, minute) in slotStarts)
        {
            var startUtc = VenueLocalToUtc(localDay.AddHours(hour).AddMinutes(minute));
            var slot = new TimeSlot { StartUtc = startUtc, EndUtc = startUtc.AddMinutes(durationMinutes) };
            for (var index = 0; index < tickets.Length; index++)
            {
                slot.Inventories.Add(new SlotInventory { TicketType = ticketTypes[index], Capacity = tickets[index].Capacity });
            }
            newEvent.TimeSlots.Add(slot);
        }
    }

    private static void AddWinterLightsConcert(AppDbContext db, DateTime localDay)
    {
        var concert = new Event
        {
            Name = "Winter Lights Concert",
            Description = "An intimate candlelit chamber concert of seasonal favorites. Reserved seating: choose your seats.",
            Venue = "Evergreen Hall",
            ImagePath = "/images/concert.svg",
            RegistrationType = RegistrationType.ReservedSeating,
        };

        foreach (var hour in new[] { 15, 19 })
        {
            var startUtc = VenueLocalToUtc(localDay.AddHours(hour));
            concert.TimeSlots.Add(new TimeSlot { StartUtc = startUtc, EndUtc = startUtc.AddMinutes(100) });
        }

        // 18 physical seats: three sections, two rows (A, B) of three seats each.
        // SVG coordinates match the client chart's 600 x 340 viewBox.
        concert.SeatingSections.Add(CreateSection("ORCHESTRA-LEFT", "Orchestra Left", 8500, 1, originX: 60, originY: 200));
        concert.SeatingSections.Add(CreateSection("VIP-CENTER", "VIP Center", 12500, 2, originX: 240, originY: 120));
        concert.SeatingSections.Add(CreateSection("ORCHESTRA-RIGHT", "Orchestra Right", 8500, 3, originX: 420, originY: 200));

        db.Events.Add(concert);
    }

    private static SeatingSection CreateSection(string code, string name, int priceCents, int displayOrder, int originX, int originY)
    {
        var section = new SeatingSection { Code = code, Name = name, PriceCents = priceCents, DisplayOrder = displayOrder };
        string[] rows = ["A", "B"];
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var seatNumber = 1; seatNumber <= 3; seatNumber++)
            {
                section.Seats.Add(new Seat
                {
                    Label = $"{code}-{rows[rowIndex]}-{seatNumber:00}",
                    Row = rows[rowIndex],
                    SeatNumber = seatNumber,
                    SvgX = originX + 30 + (seatNumber - 1) * 40,
                    SvgY = originY + 30 + rowIndex * 44,
                });
            }
        }
        return section;
    }

    private static DateTime VenueLocalToUtc(DateTime venueLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(venueLocal, DateTimeKind.Unspecified), VenueTimeZone);
}
