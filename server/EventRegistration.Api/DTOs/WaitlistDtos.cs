namespace EventRegistration.Api.DTOs;

/// <summary>Body of POST /api/waitlist (with an Idempotency-Key header). No payment details are accepted.</summary>
public record JoinWaitlistRequest(int TimeSlotId, string? Email, IReadOnlyList<TicketQuantityRequest>? Tickets);

public record WaitlistItemDto(string Description, int Quantity);

/// <summary>
/// Customer view of one waitlist entry, reached only through its private reference.
/// Status values: Waiting, OfferPendingContact, OfferAvailable, CheckoutStarted, Purchased, Expired,
/// Cancelled, Lapsed, Refunded. Contact details are never returned here.
/// </summary>
public record WaitlistStatusDto(
    Guid Reference,
    int EventId,
    string EventName,
    int TimeSlotId,
    DateTime SlotStartUtc,
    string Status,
    int? PositionInLine,
    DateTime? OfferExpiresUtc,
    Guid? OrderId,
    bool CanAccept,
    bool CanCancel,
    bool CanResumeCheckout,
    IReadOnlyList<WaitlistItemDto> Items);

/// <summary>
/// Development-only operator view (contains contact details; never served outside the local operator tool).
/// OperatorStatus: Queued, OfferReadyToNotify, ManuallyNotified, CheckoutStarted, Purchased, Expired, Cancelled, Lapsed, Refunded.
/// </summary>
public record OperatorWaitlistEntryDto(
    int QueueNumber,
    Guid Reference,
    string PrivateLink,
    string CustomerEmail,
    string EventName,
    int TimeSlotId,
    DateTime SlotStartUtc,
    string OperatorStatus,
    DateTime CreatedUtc,
    DateTime? OfferedUtc,
    DateTime? NotifiedUtc,
    DateTime? OfferExpiresUtc,
    Guid? OrderId,
    IReadOnlyList<WaitlistItemDto> Items);

public record OperatorTicketCapacityDto(int TicketTypeId, string Name, int Capacity, int Reserved, int Sold);

public record OperatorDepartureDto(
    int TimeSlotId,
    string EventName,
    DateTime StartUtc,
    int? SharedCapacity,
    int SharedCommitted,
    int WaitingCount,
    IReadOnlyList<OperatorTicketCapacityDto> TicketTypes);

public record TicketCapacityChange(int TicketTypeId, int Capacity);

/// <summary>Raise a departure's shared and/or per-type limits. Values may only go up and never below live commitments.</summary>
public record IncreaseCapacityRequest(int? SharedCapacity, IReadOnlyList<TicketCapacityChange>? TicketTypes);
