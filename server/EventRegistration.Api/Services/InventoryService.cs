using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Low-level inventory moves. Every method must run inside the caller's database transaction so
/// that a multi-line order is all-or-nothing. Counts change only through guarded conditional
/// UPDATEs whose affected-row count is checked; a read-then-write is never trusted for capacity.
/// </summary>
public class InventoryService(AppDbContext db)
{
    /// <summary>Moves quantities from available to Reserved. Throws a 409 if any line no longer fits.</summary>
    public async Task HoldTicketsAsync(int timeSlotId, IEnumerable<(int TicketTypeId, int Quantity, string Name)> lines)
    {
        foreach (var (ticketTypeId, quantity, name) in lines)
        {
            var affected = await db.SlotInventories
                .Where(i => i.TimeSlotId == timeSlotId
                    && i.TicketTypeId == ticketTypeId
                    && i.Capacity - i.Reserved - i.Sold >= quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Reserved, i => i.Reserved + quantity));

            if (affected != 1)
            {
                throw BookingException.Conflict("insufficient_capacity", $"Not enough {name} tickets remain for this time slot.");
            }
        }
    }

    /// <summary>Inserts live Held claims for the seats. The filtered unique index rejects any seat already held or sold.</summary>
    public async Task HoldSeatsAsync(int timeSlotId, Guid orderId, IEnumerable<int> seatIds)
    {
        foreach (var seatId in seatIds)
        {
            db.SeatReservations.Add(new SeatReservation
            {
                TimeSlotId = timeSlotId,
                SeatId = seatId,
                OrderId = orderId,
                Status = SeatReservationStatus.Held,
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw BookingException.Conflict("seat_unavailable", "One or more selected seats were just taken. Please choose again.");
        }
    }

    /// <summary>Converts an order's held inventory to sold. Caller guarantees this runs once per order.</summary>
    public async Task ConvertHoldsToSoldAsync(Order order)
    {
        if (order.RegistrationType == RegistrationType.GeneralAdmission)
        {
            foreach (var item in order.Items.Where(i => i.TicketTypeId is not null))
            {
                var affected = await db.SlotInventories
                    .Where(i => i.TimeSlotId == order.TimeSlotId && i.TicketTypeId == item.TicketTypeId && i.Reserved >= item.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(i => i.Reserved, i => i.Reserved - item.Quantity)
                        .SetProperty(i => i.Sold, i => i.Sold + item.Quantity));
                EnsureInventoryMoved(affected, order.Id);
            }
            return;
        }

        await db.SeatReservations
            .Where(r => r.OrderId == order.Id && r.Status == SeatReservationStatus.Held)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, SeatReservationStatus.Sold));
    }

    /// <summary>Returns an order's held inventory to the pool. Caller guarantees this runs once per order.</summary>
    public async Task ReleaseHoldsAsync(Order order)
    {
        if (order.RegistrationType == RegistrationType.GeneralAdmission)
        {
            foreach (var item in order.Items.Where(i => i.TicketTypeId is not null))
            {
                var affected = await db.SlotInventories
                    .Where(i => i.TimeSlotId == order.TimeSlotId && i.TicketTypeId == item.TicketTypeId && i.Reserved >= item.Quantity)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Reserved, i => i.Reserved - item.Quantity));
                EnsureInventoryMoved(affected, order.Id);
            }
            return;
        }

        await db.SeatReservations
            .Where(r => r.OrderId == order.Id && r.Status == SeatReservationStatus.Held)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, SeatReservationStatus.Released));
    }

    private static void EnsureInventoryMoved(int affectedRows, Guid orderId)
    {
        if (affectedRows != 1)
        {
            // Would mean the ledger no longer matches the order; abort the whole transaction.
            throw new InvalidOperationException($"Inventory ledger mismatch while updating order {orderId}.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
