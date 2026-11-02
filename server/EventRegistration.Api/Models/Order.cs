namespace EventRegistration.Api.Models;

public class Order
{
    /// <summary>Random GUID; acts as the unguessable order reference (there are no accounts).</summary>
    public Guid Id { get; set; }

    public int EventId { get; set; }
    public int TimeSlotId { get; set; }
    public RegistrationType RegistrationType { get; set; }
    public OrderStatus Status { get; set; }
    public string? ReleaseReason { get; set; }

    /// <summary>Client-supplied Idempotency-Key header of the POST that created this order.</summary>
    public required string RequestIdempotencyKey { get; set; }

    /// <summary>Normalized selection, used to reject reuse of a key with a different selection.</summary>
    public required string RequestFingerprint { get; set; }

    public string Currency { get; set; } = "usd";
    public long TotalCents { get; set; }
    public string? CustomerEmail { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>After this time an order that never attempted checkout may be released.</summary>
    public DateTime PreCheckoutHoldExpiresUtc { get; set; }

    /// <summary>
    /// Set when a Stripe session creation call starts. While set and no session is stored,
    /// the outcome is uncertain and the hold must not be released without asking Stripe.
    /// </summary>
    public DateTime? CheckoutAttemptStartedUtc { get; set; }

    /// <summary>Expiry sent to Stripe; fixed on the first attempt so retries send an identical payload.</summary>
    public DateTime? PlannedSessionExpiresUtc { get; set; }

    public string? StripeSessionId { get; set; }
    public string? StripeCheckoutUrl { get; set; }
    public DateTime? StripeSessionExpiresUtc { get; set; }

    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? ReleasedUtc { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>Stable key sent to Stripe so a retried creation returns the same session.</summary>
    public string StripeIdempotencyKey => $"blevinspass-order-{Id:N}-checkout";
}

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>Set for General Admission lines.</summary>
    public int? TicketTypeId { get; set; }

    /// <summary>Set for Reserved Seating lines (one seat per line).</summary>
    public int? SeatId { get; set; }

    /// <summary>Snapshot of what was bought, e.g. "Adult" or "VIP Center A-01".</summary>
    public required string Description { get; set; }
    public int Quantity { get; set; }

    /// <summary>Server price snapshot at hold time.</summary>
    public int UnitPriceCents { get; set; }
}
