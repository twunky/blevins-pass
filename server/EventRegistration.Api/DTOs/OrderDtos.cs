namespace EventRegistration.Api.DTOs;

/// <summary>
/// Exactly one of <see cref="Tickets"/> (General Admission) or <see cref="SeatIds"/> (Reserved Seating)
/// must be provided. There are deliberately no price fields: the server prices every order.
/// </summary>
public record CreateOrderRequest(int TimeSlotId, IReadOnlyList<TicketQuantityRequest>? Tickets, IReadOnlyList<int>? SeatIds);

public record TicketQuantityRequest(int TicketTypeId, int Quantity);

public record ConfirmOrderRequest(bool Cancel);

public record OrderItemDto(string Description, int Quantity, int UnitPriceCents, long LineTotalCents);

public record OrderDto(
    Guid Id,
    int EventId,
    string EventName,
    int TimeSlotId,
    DateTime SlotStartUtc,
    string RegistrationType,
    string Status,
    string? ReleaseReason,
    string Currency,
    long TotalCents,
    string? CustomerEmail,
    DateTime? PaymentDeadlineUtc,
    IReadOnlyList<OrderItemDto> Items);

public record CheckoutSessionDto(Guid OrderId, string CheckoutUrl, DateTime ExpiresUtc);

/// <summary>Uniform error body: { "error": { "code": "...", "message": "..." } }.</summary>
public record ApiErrorBody(ApiError Error);

public record ApiError(string Code, string Message);
