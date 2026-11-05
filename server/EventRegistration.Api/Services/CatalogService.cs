using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>Read-only event, availability and seating queries, plus the persisted theme setting.</summary>
public class CatalogService(AppDbContext db, PaymentService payments, WaitlistService waitlist, TimeProvider clock)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<EventSummaryDto>> GetEventsAsync(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking()
            .Where(e => e.Active)
            .Include(e => e.TimeSlots)
            .Include(e => e.SeatingSections)
            .ToListAsync(cancellationToken);
        var ticketPrices = await db.TicketTypes.AsNoTracking()
            .Where(t => t.Active)
            .Select(t => new { t.EventId, t.PriceCents })
            .ToListAsync(cancellationToken);

        return events
            .Select(e => ToSummary(e, e.RegistrationType == RegistrationType.GeneralAdmission
                ? ticketPrices.Where(p => p.EventId == e.Id).Select(p => (int?)p.PriceCents).Min()
                : e.SeatingSections.Select(s => (int?)s.PriceCents).Min()))
            .OrderBy(e => e.TimeSlots.FirstOrDefault()?.StartUtc)
            .ToList();
    }

    public async Task<EventSummaryDto> GetEventAsync(int eventId, CancellationToken cancellationToken)
    {
        var events = await GetEventsAsync(cancellationToken);
        return events.FirstOrDefault(e => e.Id == eventId) ?? throw BookingException.NotFound("Event not found.");
    }

    public async Task<SlotAvailabilityDto> GetTicketAvailabilityAsync(int eventId, int timeSlotId, CancellationToken cancellationToken)
    {
        var slot = await GetSlotAsync(eventId, timeSlotId, RegistrationType.GeneralAdmission, cancellationToken);
        await payments.ReconcileStaleOrdersAsync(slot.Id, cancellationToken);
        await waitlist.PromoteAsync(slot.Id, cancellationToken);

        var rows = await db.SlotInventories.AsNoTracking()
            .Where(i => i.TimeSlotId == slot.Id)
            .Select(i => new { i.TicketTypeId, i.TicketType!.Name, i.TicketType.Description, i.TicketType.PriceCents, i.TicketType.Active, i.Capacity, i.Reserved, i.Sold })
            .ToListAsync(cancellationToken);

        // Shared usage counts every type (active or not) because every held or sold ticket is a person.
        // A legacy departure that is already over its limit reports 0, never a negative number.
        int? sharedRemaining = slot.SharedCapacity is int shared
            ? Math.Max(0, shared - rows.Sum(r => r.Reserved + r.Sold))
            : null;
        var waitingCount = await waitlist.CountWaitingAsync(slot.Id, cancellationToken);
        // Ticket types someone is waiting for (per waiting entry), matching the public hold guard.
        var waitingByType = await db.WaitlistItems.AsNoTracking()
            .Where(i => db.WaitlistEntries.Any(w => w.Id == i.WaitlistEntryId && w.TimeSlotId == slot.Id && w.Status == WaitlistStatus.Waiting))
            .GroupBy(i => i.TicketTypeId)
            .Select(g => new { TicketTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TicketTypeId, g => g.Count, cancellationToken);
        var sharedDeparture = slot.SharedCapacity is not null;
        if (sharedDeparture && waitingCount > 0)
        {
            // Shared departures: strict departure-wide first come, first served.
            sharedRemaining = 0;
        }

        var ticketTypes = rows
            .Where(r => r.Active)
            .OrderBy(r => r.PriceCents)
            .Select(r =>
            {
                // Independent caps: only the types someone is waiting for are held back.
                var blocked = sharedDeparture ? waitingCount > 0 : waitingByType.ContainsKey(r.TicketTypeId);
                var typeRemaining = blocked ? 0 : Math.Max(0, r.Capacity - r.Reserved - r.Sold);
                return new TicketAvailabilityDto(
                    r.TicketTypeId, r.Name, r.Description, r.PriceCents, r.Capacity,
                    Math.Min(typeRemaining, sharedRemaining ?? typeRemaining),
                    sharedDeparture ? waitingCount : waitingByType.GetValueOrDefault(r.TicketTypeId));
            })
            .ToList();

        return new SlotAvailabilityDto(eventId, slot.Id, "usd", IsBookable(slot), ticketTypes, slot.SharedCapacity, sharedRemaining, waitingCount);
    }

    public async Task<SeatingChartDto> GetSeatingChartAsync(int eventId, CancellationToken cancellationToken)
    {
        var sections = await db.SeatingSections.AsNoTracking()
            .Where(s => s.EventId == eventId)
            .OrderBy(s => s.DisplayOrder)
            .Include(s => s.Seats)
            .ToListAsync(cancellationToken);
        if (sections.Count == 0)
        {
            throw BookingException.NotFound("This event has no seating chart.");
        }

        return new SeatingChartDto(eventId, "usd", sections
            .Select(s => new SeatingSectionDto(s.Id, s.Code, s.Name, s.PriceCents, s.Seats
                .OrderBy(seat => seat.Row).ThenBy(seat => seat.SeatNumber)
                .Select(seat => new SeatDto(seat.Id, seat.Label, seat.Row, seat.SeatNumber, seat.SvgX, seat.SvgY, seat.Active))
                .ToList()))
            .ToList());
    }

    public async Task<SeatingAvailabilityDto> GetSeatingAvailabilityAsync(int eventId, int timeSlotId, CancellationToken cancellationToken)
    {
        var slot = await GetSlotAsync(eventId, timeSlotId, RegistrationType.ReservedSeating, cancellationToken);
        await payments.ReconcileStaleOrdersAsync(slot.Id, cancellationToken);

        var seats = await db.Seats.AsNoTracking()
            .Where(s => s.SeatingSection!.EventId == eventId)
            .Select(s => new { s.Id, s.Active, s.SeatingSectionId })
            .ToListAsync(cancellationToken);
        var liveClaims = await db.SeatReservations.AsNoTracking()
            .Where(r => r.TimeSlotId == slot.Id && (r.Status == SeatReservationStatus.Held || r.Status == SeatReservationStatus.Sold))
            .ToDictionaryAsync(r => r.SeatId, r => (SeatReservationStatus?)r.Status, cancellationToken);

        var seatStatuses = seats
            .Select(seat => new SeatStatusDto(seat.Id, DescribeSeat(seat.Active, liveClaims.GetValueOrDefault(seat.Id))))
            .ToList();

        // Section counts are derived from seat states; there is no separate quantity ledger to drift.
        var sectionCounts = seats
            .GroupBy(s => s.SeatingSectionId)
            .Select(group => new SectionRemainingDto(
                group.Key,
                group.Count(s => seatStatuses.First(status => status.SeatId == s.Id).Status == "available"),
                group.Count()))
            .ToList();

        return new SeatingAvailabilityDto(eventId, slot.Id, IsBookable(slot), seatStatuses, sectionCounts);
    }

    public async Task<ThemeDto> GetThemeAsync()
    {
        var settings = await db.SiteSettings.AsNoTracking().FirstAsync();
        return new ThemeDto(settings.ActiveTheme.ToString());
    }

    public async Task<ThemeDto> SetThemeAsync(string? themeName)
    {
        if (!Enum.TryParse<ThemeName>(themeName, ignoreCase: true, out var theme) || !Enum.IsDefined(theme))
        {
            throw BookingException.Validation("activeTheme must be \"Fall\" or \"Winter\".");
        }
        var settings = await db.SiteSettings.FirstAsync();
        settings.ActiveTheme = theme;
        await db.SaveChangesAsync();
        return new ThemeDto(theme.ToString());
    }

    private async Task<TimeSlot> GetSlotAsync(int eventId, int timeSlotId, RegistrationType expectedType, CancellationToken cancellationToken)
    {
        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event)
            .FirstOrDefaultAsync(s => s.Id == timeSlotId && s.EventId == eventId, cancellationToken)
            ?? throw BookingException.NotFound("That time slot does not exist for this event.");
        if (slot.Event!.RegistrationType != expectedType)
        {
            throw BookingException.Validation(expectedType == RegistrationType.ReservedSeating
                ? "This event does not use reserved seating."
                : "This event uses reserved seating; use the seating availability endpoint.");
        }
        return slot;
    }

    private static string DescribeSeat(bool active, SeatReservationStatus? liveClaim) => (active, liveClaim) switch
    {
        (false, _) => "inactive",
        (_, SeatReservationStatus.Sold) => "sold",
        (_, SeatReservationStatus.Held) => "held",
        _ => "available",
    };

    private bool IsBookable(TimeSlot slot) => slot.Active && slot.StartUtc > UtcNow;

    private EventSummaryDto ToSummary(Event e, int? fromPriceCents) => new(
        e.Id,
        e.Name,
        e.Description,
        e.Venue,
        e.RegistrationType.ToString(),
        e.ImagePath,
        fromPriceCents,
        e.TimeSlots
            .Where(s => s.Active)
            .OrderBy(s => s.StartUtc)
            .Select(s => new TimeSlotDto(s.Id, s.StartUtc, s.EndUtc, IsBookable(s)))
            .ToList());
}
