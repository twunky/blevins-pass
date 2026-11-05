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
    /// <summary>
    /// Moves quantities from available to Reserved. Throws a 409 if any line no longer fits, either its
    /// own ticket-type cap or the departure's shared head-count limit. Earlier lines are undone by the
    /// caller's transaction rollback, so a mixed order is all-or-nothing.
    /// </summary>
    /// <param name="requireEmptyWaitlist">
    /// Public purchases pass true: the hold also fails while anyone is Waiting for this departure.
    /// That check is part of the same UPDATE, so a waitlist entry committed just before it always wins.
    /// Waitlist promotion passes false (it is serving the waiting entry itself).
    /// </param>
    public async Task HoldTicketsAsync(int timeSlotId, IEnumerable<(int TicketTypeId, int Quantity, string Name)> lines, bool requireEmptyWaitlist = false)
    {
        foreach (var (ticketTypeId, quantity, name) in lines)
        {
            // One statement checks every rule and writes, so no other writer can slip in between the
            // check and the increment. The shared usage is the live sum of every type's Reserved + Sold
            // for this departure (including lines already held earlier in this transaction). When the
            // slot has no shared limit the subquery is NULL and only the per-type cap applies.
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "SlotInventories" SET "Reserved" = "Reserved" + {quantity}
                WHERE "TimeSlotId" = {timeSlotId}
                  AND "TicketTypeId" = {ticketTypeId}
                  AND "Capacity" - "Reserved" - "Sold" >= {quantity}
                  AND COALESCE(
                        (SELECT t."SharedCapacity"
                                - (SELECT COALESCE(SUM(s."Reserved" + s."Sold"), 0) FROM "SlotInventories" s WHERE s."TimeSlotId" = t."Id")
                         FROM "TimeSlots" t WHERE t."Id" = {timeSlotId}),
                        {quantity}) >= {quantity}
                  AND ({requireEmptyWaitlist} = 0
                       OR NOT EXISTS (
                           SELECT 1 FROM "WaitlistEntries" w
                           WHERE w."TimeSlotId" = {timeSlotId} AND w."Status" = 'Waiting'
                             -- Shared departures: anyone waiting blocks. Independent caps: only waiters for this type.
                             AND ((SELECT t2."SharedCapacity" FROM "TimeSlots" t2 WHERE t2."Id" = {timeSlotId}) IS NOT NULL
                                  OR EXISTS (SELECT 1 FROM "WaitlistItems" wi WHERE wi."WaitlistEntryId" = w."Id" AND wi."TicketTypeId" = {ticketTypeId}))));
                """);

            if (affected != 1)
            {
                var sharedSlot = await db.TimeSlots.AnyAsync(t => t.Id == timeSlotId && t.SharedCapacity != null);
                if (requireEmptyWaitlist && await db.WaitlistEntries.AnyAsync(w => w.TimeSlotId == timeSlotId && w.Status == WaitlistStatus.Waiting
                        && (sharedSlot || w.Items.Any(i => i.TicketTypeId == ticketTypeId))))
                {
                    throw BookingException.Conflict("waitlist_active", "People are already waiting for this departure. Join the waitlist to keep your place in line.");
                }
                throw BookingException.Conflict("insufficient_capacity", await DescribeShortfallAsync(timeSlotId, ticketTypeId, quantity, name));
            }
        }
    }

    /// <summary>Returns quantities from Reserved to available (used when a waitlist offer ends unaccepted).</summary>
    public async Task ReleaseReservedTicketsAsync(int timeSlotId, IEnumerable<(int TicketTypeId, int Quantity)> lines, string owner)
    {
        foreach (var (ticketTypeId, quantity) in lines)
        {
            var affected = await db.SlotInventories
                .Where(i => i.TimeSlotId == timeSlotId && i.TicketTypeId == ticketTypeId && i.Reserved >= quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Reserved, i => i.Reserved - quantity));
            if (affected != 1)
            {
                throw new InvalidOperationException($"Inventory ledger mismatch while releasing {owner}.");
            }
        }
    }

    /// <summary>Explains which limit refused the line (message only; the guarded UPDATE already decided).</summary>
    private async Task<string> DescribeShortfallAsync(int timeSlotId, int ticketTypeId, int quantity, string name)
    {
        var typeRemaining = await db.SlotInventories.AsNoTracking()
            .Where(i => i.TimeSlotId == timeSlotId && i.TicketTypeId == ticketTypeId)
            .Select(i => i.Capacity - i.Reserved - i.Sold)
            .FirstOrDefaultAsync();
        return typeRemaining >= quantity
            ? "Not enough places remain on this departure for your whole group (every ticket counts toward the shared limit)."
            : $"Not enough {name} tickets remain for this time slot.";
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
