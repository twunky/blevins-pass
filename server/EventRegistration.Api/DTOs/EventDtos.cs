namespace EventRegistration.Api.DTOs;

// All timestamps are UTC (serialized with a trailing "Z"); the client displays venue time
// (America/New_York). All money is integer cents in USD.

public record TimeSlotDto(int Id, DateTime StartUtc, DateTime EndUtc, bool IsBookable);

public record EventSummaryDto(
    int Id,
    string Name,
    string Description,
    string Venue,
    string RegistrationType,
    string? ImagePath,
    int? FromPriceCents,
    IReadOnlyList<TimeSlotDto> TimeSlots);

public record TicketAvailabilityDto(
    int TicketTypeId,
    string Name,
    string Description,
    int PriceCents,
    int Capacity,
    int Remaining);

public record SlotAvailabilityDto(
    int EventId,
    int TimeSlotId,
    string Currency,
    bool IsBookable,
    IReadOnlyList<TicketAvailabilityDto> TicketTypes);

public record SeatDto(int Id, string Label, string Row, int SeatNumber, int SvgX, int SvgY, bool Active);

public record SeatingSectionDto(int Id, string Code, string Name, int PriceCents, IReadOnlyList<SeatDto> Seats);

public record SeatingChartDto(int EventId, string Currency, IReadOnlyList<SeatingSectionDto> Sections);

/// <summary>Status is one of: available, held, sold, inactive.</summary>
public record SeatStatusDto(int SeatId, string Status);

public record SectionRemainingDto(int SectionId, int Remaining, int Total);

public record SeatingAvailabilityDto(
    int EventId,
    int TimeSlotId,
    bool IsBookable,
    IReadOnlyList<SeatStatusDto> Seats,
    IReadOnlyList<SectionRemainingDto> Sections);

public record ThemeDto(string ActiveTheme);
