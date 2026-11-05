namespace EventRegistration.Api.Models;

/// <summary>
/// Waitlist lifecycle. Allowed transitions:
/// Waiting -> Offered (capacity reserved) -> Notified (owner contacted the customer) -> Accepted (order created);
/// Waiting/Offered/Notified -> Cancelled; Offered/Notified -> Expired. Accepted, Cancelled and Expired are terminal
/// for the entry itself; after Accepted the linked order's own lifecycle decides purchased vs. lapsed.
/// </summary>
public enum WaitlistStatus
{
    /// <summary>In line. Nothing is reserved and nothing is charged.</summary>
    Waiting,

    /// <summary>Capacity is reserved for this entry; the owner has not yet recorded a notification.</summary>
    Offered,

    /// <summary>Owner recorded that they contacted the customer; the customer may now buy until the offer expires.</summary>
    Notified,

    /// <summary>The customer accepted; reserved capacity moved to an ordinary order (see OrderId).</summary>
    Accepted,

    /// <summary>The offer ran out before acceptance; its reserved capacity was returned.</summary>
    Expired,

    /// <summary>The customer left the waitlist (or the demo reset closed the offer).</summary>
    Cancelled,
}

/// <summary>
/// A request to buy General Admission tickets for a sold-out departure. Id order is the
/// first-come-first-served queue order. Joining never contacts the payment provider.
/// </summary>
public class WaitlistEntry
{
    /// <summary>Auto-increment; defines queue order within a departure.</summary>
    public int Id { get; set; }

    /// <summary>Random GUID given only to the customer (status/claim link). Never listed publicly.</summary>
    public Guid PublicReference { get; set; }

    public int EventId { get; set; }
    public int TimeSlotId { get; set; }
    public required string CustomerEmail { get; set; }
    public WaitlistStatus Status { get; set; }

    /// <summary>Client-supplied Idempotency-Key of the join request, so a retried join returns the same entry.</summary>
    public required string RequestIdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? OfferedUtc { get; set; }
    public DateTime? NotifiedUtc { get; set; }

    /// <summary>When an Offered/Notified entry stops holding capacity.</summary>
    public DateTime? OfferExpiresUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string? CloseReason { get; set; }

    /// <summary>Order created on acceptance. Its id is the order's own unguessable reference.</summary>
    public Guid? OrderId { get; set; }

    public List<WaitlistItem> Items { get; set; } = [];
}

/// <summary>One requested ticket type and quantity. Prices are taken only when the offer is accepted.</summary>
public class WaitlistItem
{
    public int Id { get; set; }
    public int WaitlistEntryId { get; set; }
    public int TicketTypeId { get; set; }
    public int Quantity { get; set; }
}
