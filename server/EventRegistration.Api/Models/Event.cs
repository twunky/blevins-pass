namespace EventRegistration.Api.Models;

public class Event
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Venue { get; set; }
    public RegistrationType RegistrationType { get; set; }

    /// <summary>Path of a bundled client image, e.g. "/images/lantern-walk.svg".</summary>
    public string? ImagePath { get; set; }

    public bool Active { get; set; } = true;

    public List<TimeSlot> TimeSlots { get; set; } = [];
    public List<SeatingSection> SeatingSections { get; set; } = [];
}

public class TimeSlot
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool Active { get; set; } = true;

    public List<SlotInventory> Inventories { get; set; } = [];
}

/// <summary>A General Admission ticket kind (Adult, Child, VIP...). Price is server-owned.</summary>
public class TicketType
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int PriceCents { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// Independent capacity for one (time slot, ticket type) pair.
/// Invariant: 0 &lt;= Reserved, 0 &lt;= Sold, Reserved + Sold &lt;= Capacity.
/// Changes are made only with guarded conditional UPDATE statements (see InventoryService).
/// </summary>
public class SlotInventory
{
    public int Id { get; set; }
    public int TimeSlotId { get; set; }
    public TimeSlot? TimeSlot { get; set; }
    public int TicketTypeId { get; set; }
    public TicketType? TicketType { get; set; }
    public int Capacity { get; set; }
    public int Reserved { get; set; }
    public int Sold { get; set; }
}

public class SiteSettings
{
    public int Id { get; set; }
    public ThemeName ActiveTheme { get; set; } = ThemeName.Fall;
}
