namespace EventRegistration.Api.Models;

public class SeatingSection
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>Stable code used in seat identifiers, e.g. "VIP-CENTER".</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int PriceCents { get; set; }
    public int DisplayOrder { get; set; }

    public List<Seat> Seats { get; set; } = [];
}

/// <summary>A physical seat. The same seat is reused for every performance of its event.</summary>
public class Seat
{
    public int Id { get; set; }
    public int SeatingSectionId { get; set; }
    public SeatingSection? SeatingSection { get; set; }

    /// <summary>Human-readable identifier, e.g. "VIP-CENTER-A-01".</summary>
    public required string Label { get; set; }
    public required string Row { get; set; }
    public int SeatNumber { get; set; }
    public int SvgX { get; set; }
    public int SvgY { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// A claim on one seat for one performance. At most one Held or Sold reservation may exist
/// per (TimeSlotId, SeatId); a filtered unique index enforces this so Released history
/// does not block rebooking.
/// </summary>
public class SeatReservation
{
    public int Id { get; set; }
    public int TimeSlotId { get; set; }
    public int SeatId { get; set; }
    public Seat? Seat { get; set; }
    public Guid OrderId { get; set; }
    public SeatReservationStatus Status { get; set; }
}
