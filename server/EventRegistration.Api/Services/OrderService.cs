using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

/// <summary>Validates, prices and holds new orders, and reads order summaries.</summary>
public class OrderService(
    AppDbContext db,
    InventoryService inventory,
    PaymentService payments,
    TimeProvider clock,
    IOptions<CheckoutOptions> checkoutOptions)
{
    public const int MaxIdempotencyKeyLength = 100;

    /// <summary>
    /// Creates a held, server-priced order. Retrying with the same Idempotency-Key and the same
    /// selection returns the original order; reusing the key with a different selection is a 409.
    /// </summary>
    public async Task<(OrderDto Order, bool Created)> CreateOrderAsync(string? idempotencyKey, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw BookingException.Validation($"An Idempotency-Key header (1-{MaxIdempotencyKeyLength} characters) is required.");
        }

        var selection = OrderSelection.Parse(request);

        var existing = await FindByIdempotencyKeyAsync(idempotencyKey, selection.Fingerprint);
        if (existing is not null)
        {
            return (existing, false);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event)
            .FirstOrDefaultAsync(s => s.Id == request.TimeSlotId, cancellationToken)
            ?? throw BookingException.NotFound("That time slot does not exist.");
        var slotEvent = slot.Event!;

        if (!slot.Active || !slotEvent.Active || slot.StartUtc <= now)
        {
            throw BookingException.Conflict("slot_closed", "This time slot is no longer open for booking.");
        }

        var expectedType = selection.IsSeating ? RegistrationType.ReservedSeating : RegistrationType.GeneralAdmission;
        if (slotEvent.RegistrationType != expectedType)
        {
            throw BookingException.Validation(selection.IsSeating
                ? "This event uses General Admission tickets, not seats."
                : "This event uses reserved seating; select seats instead of ticket quantities.");
        }

        // Free inventory held by abandoned or expired orders before deciding there is no room.
        await payments.ReconcileStaleOrdersAsync(slot.Id, cancellationToken);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            EventId = slotEvent.Id,
            TimeSlotId = slot.Id,
            RegistrationType = expectedType,
            Status = OrderStatus.PendingCheckout,
            RequestIdempotencyKey = idempotencyKey,
            RequestFingerprint = selection.Fingerprint,
            CreatedUtc = now,
            PreCheckoutHoldExpiresUtc = now.AddMinutes(checkoutOptions.Value.PreCheckoutHoldMinutes),
        };

        order.Items = selection.IsSeating
            ? await PriceSeatsAsync(slotEvent.Id, selection.SeatIds, cancellationToken)
            : await PriceTicketsAsync(slot.Id, selection.Tickets, cancellationToken);
        order.TotalCents = order.Items.Sum(item => checked((long)item.UnitPriceCents * item.Quantity));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Claims first, then the order row, all in one transaction: either everything is held or nothing is.
            if (selection.IsSeating)
            {
                await inventory.HoldSeatsAsync(slot.Id, order.Id, selection.SeatIds);
            }
            else
            {
                await inventory.HoldTicketsAsync(slot.Id, order.Items.Select(i => (i.TicketTypeId!.Value, i.Quantity, i.Description)));
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // A concurrent request with the same Idempotency-Key won the insert; our holds roll back.
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var winner = await FindByIdempotencyKeyAsync(idempotencyKey, selection.Fingerprint)
                ?? throw BookingException.Conflict("order_conflict", "The order could not be created. Please try again.");
            return (winner, false);
        }

        return (await GetOrderAsync(order.Id), true);
    }

    public async Task<OrderDto> GetOrderAsync(Guid orderId)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw BookingException.NotFound("Order not found.");
        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event).FirstAsync(s => s.Id == order.TimeSlotId);

        return new OrderDto(
            order.Id,
            order.EventId,
            slot.Event!.Name,
            order.TimeSlotId,
            slot.StartUtc,
            order.RegistrationType.ToString(),
            order.Status.ToString(),
            order.ReleaseReason,
            order.Currency,
            order.TotalCents,
            order.CustomerEmail,
            order.StripeSessionExpiresUtc,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDto(i.Description, i.Quantity, i.UnitPriceCents, (long)i.UnitPriceCents * i.Quantity))
                .ToList());
    }

    private async Task<OrderDto?> FindByIdempotencyKeyAsync(string idempotencyKey, string fingerprint)
    {
        var match = await db.Orders.AsNoTracking()
            .Where(o => o.RequestIdempotencyKey == idempotencyKey)
            .Select(o => new { o.Id, o.RequestFingerprint })
            .FirstOrDefaultAsync();

        if (match is null)
        {
            return null;
        }
        if (match.RequestFingerprint != fingerprint)
        {
            throw BookingException.Conflict("idempotency_key_reused", "This Idempotency-Key was already used for a different selection.");
        }
        return await GetOrderAsync(match.Id);
    }

    private async Task<List<OrderItem>> PriceTicketsAsync(int timeSlotId, IReadOnlyList<TicketQuantityRequest> tickets, CancellationToken cancellationToken)
    {
        var requestedIds = tickets.Select(t => t.TicketTypeId).ToList();
        var offered = await db.SlotInventories.AsNoTracking()
            .Where(i => i.TimeSlotId == timeSlotId && requestedIds.Contains(i.TicketTypeId) && i.TicketType!.Active)
            .Select(i => new { i.TicketTypeId, i.TicketType!.Name, i.TicketType.PriceCents })
            .ToListAsync(cancellationToken);

        return tickets.Select(ticket =>
        {
            var match = offered.FirstOrDefault(o => o.TicketTypeId == ticket.TicketTypeId)
                ?? throw BookingException.Validation($"Ticket type {ticket.TicketTypeId} is not offered for this time slot.");
            return new OrderItem
            {
                TicketTypeId = match.TicketTypeId,
                Description = match.Name,
                Quantity = ticket.Quantity,
                UnitPriceCents = match.PriceCents,
            };
        }).ToList();
    }

    private async Task<List<OrderItem>> PriceSeatsAsync(int eventId, IReadOnlyList<int> seatIds, CancellationToken cancellationToken)
    {
        var seats = await db.Seats.AsNoTracking()
            .Where(s => seatIds.Contains(s.Id) && s.SeatingSection!.EventId == eventId)
            .Select(s => new { s.Id, s.Label, s.Active, SectionName = s.SeatingSection!.Name, s.SeatingSection.PriceCents, s.Row, s.SeatNumber })
            .ToListAsync(cancellationToken);

        if (seats.Count != seatIds.Count)
        {
            throw BookingException.Validation("One or more seats do not belong to this event.");
        }
        if (seats.Any(s => !s.Active))
        {
            throw BookingException.Conflict("seat_unavailable", "One or more selected seats are not available.");
        }

        return seats
            .OrderBy(s => s.Label, StringComparer.Ordinal)
            .Select(s => new OrderItem
            {
                SeatId = s.Id,
                Description = $"{s.SectionName} - Row {s.Row}, Seat {s.SeatNumber} ({s.Label})",
                Quantity = 1,
                UnitPriceCents = s.PriceCents,
            })
            .ToList();
    }
}

/// <summary>A validated, normalized order selection.</summary>
internal sealed record OrderSelection(bool IsSeating, IReadOnlyList<TicketQuantityRequest> Tickets, IReadOnlyList<int> SeatIds, string Fingerprint)
{
    public static OrderSelection Parse(CreateOrderRequest request)
    {
        if (request.TimeSlotId <= 0)
        {
            throw BookingException.Validation("timeSlotId is required.");
        }

        var hasTickets = request.Tickets is { Count: > 0 };
        var hasSeats = request.SeatIds is { Count: > 0 };
        if (hasTickets == hasSeats)
        {
            throw BookingException.Validation("Provide either ticket quantities or seat selections (exactly one, and not empty).");
        }

        if (hasSeats)
        {
            var seatIds = request.SeatIds!;
            if (seatIds.Count > CheckoutOptions.MaxSeatsPerOrder)
            {
                throw BookingException.Validation($"You can select at most {CheckoutOptions.MaxSeatsPerOrder} seats per order.");
            }
            if (seatIds.Distinct().Count() != seatIds.Count || seatIds.Any(id => id <= 0))
            {
                throw BookingException.Validation("Seat selections must be distinct, valid seat ids.");
            }
            var sortedSeats = seatIds.Order().ToList();
            return new OrderSelection(true, [], sortedSeats, $"slot:{request.TimeSlotId}|seats:{string.Join(',', sortedSeats)}");
        }

        var tickets = request.Tickets!;
        if (tickets.Select(t => t.TicketTypeId).Distinct().Count() != tickets.Count)
        {
            throw BookingException.Validation("Each ticket type may appear only once.");
        }
        if (tickets.Any(t => t.Quantity < 1 || t.Quantity > CheckoutOptions.MaxTicketsPerLine))
        {
            throw BookingException.Validation($"Ticket quantities must be whole numbers from 1 to {CheckoutOptions.MaxTicketsPerLine}.");
        }
        var sortedTickets = tickets.OrderBy(t => t.TicketTypeId).ToList();
        var fingerprint = $"slot:{request.TimeSlotId}|tickets:{string.Join(',', sortedTickets.Select(t => $"{t.TicketTypeId}x{t.Quantity}"))}";
        return new OrderSelection(false, sortedTickets, [], fingerprint);
    }
}
