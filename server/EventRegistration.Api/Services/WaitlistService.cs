using System.Net.Mail;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Services;

/// <summary>
/// First-come-first-served waitlist for sold-out General Admission departures.
///
/// Rules (see docs/contracts/booking-rules.md, "Waitlist"):
/// - Joining stores a request only. No payment provider call, no card, no hold on funds.
/// - Promotion is strict FIFO per departure: the oldest Waiting entry is offered first, and if its whole
///   group does not fit, nobody behind it is offered (no skipping ahead with a smaller group).
/// - An offer reserves capacity through the same guarded ledger as orders (SlotInventory.Reserved), so
///   ordinary buyers and other offers can never take those places, and type/shared limits still apply.
/// - The customer can buy only after the owner records a manual notification. Accepting moves the
///   reservation into an ordinary order, which then goes through the normal server-priced Stripe flow.
/// - Expired or cancelled offers return their places, and the queue advances on the next promotion.
/// </summary>
public class WaitlistService(
    AppDbContext db,
    InventoryService inventory,
    TimeProvider clock,
    IOptions<WaitlistOptions> waitlistOptions,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<WaitlistService> logger)
{
    public const int MaxEmailLength = 254;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    private TimeSpan OfferLifetime => TimeSpan.FromMinutes(waitlistOptions.Value.OfferLifetimeMinutes);

    // ---- Customer actions -----------------------------------------------------------------

    public async Task<(WaitlistStatusDto Entry, bool Created)> JoinAsync(string? idempotencyKey, JoinWaitlistRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > OrderService.MaxIdempotencyKeyLength)
        {
            throw BookingException.Validation($"An Idempotency-Key header (1-{OrderService.MaxIdempotencyKeyLength} characters) is required.");
        }
        var email = NormalizeEmail(request.Email);
        if (request.Tickets is not { Count: > 0 })
        {
            throw BookingException.Validation("Choose at least one ticket quantity for the waitlist.");
        }
        var selection = OrderSelection.Parse(new CreateOrderRequest(request.TimeSlotId, request.Tickets, null));
        var fingerprint = $"{selection.Fingerprint}|email:{email}";

        var existing = await FindByIdempotencyKeyAsync(idempotencyKey, fingerprint, cancellationToken);
        if (existing is not null)
        {
            return (await GetStatusAsync(existing.Value, cancellationToken), false);
        }

        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event)
            .FirstOrDefaultAsync(s => s.Id == request.TimeSlotId, cancellationToken)
            ?? throw BookingException.NotFound("That time slot does not exist.");
        if (slot.Event!.RegistrationType != RegistrationType.GeneralAdmission)
        {
            throw BookingException.Validation("The waitlist is available for General Admission events only.");
        }
        if (!slot.Active || !slot.Event.Active || slot.StartUtc <= UtcNow)
        {
            throw BookingException.Conflict("slot_closed", "This time slot is no longer open for booking.");
        }
        await ValidateGroupCanEverFitAsync(slot, selection.Tickets, cancellationToken);
        if (slot.SharedCapacity is null && selection.Tickets.Count > 1)
        {
            // Independent caps queue per ticket type, so each new request names exactly one type.
            throw BookingException.Validation("Join the waitlist for one ticket type at a time on this departure.");
        }

        var entry = new WaitlistEntry
        {
            PublicReference = Guid.NewGuid(),
            EventId = slot.EventId,
            TimeSlotId = slot.Id,
            CustomerEmail = email,
            Status = WaitlistStatus.Waiting,
            RequestIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedUtc = UtcNow,
            Items = selection.Tickets.Select(t => new WaitlistItem { TicketTypeId = t.TicketTypeId, Quantity = t.Quantity }).ToList(),
        };
        // Insert first, then check eligibility in the same transaction. The insert takes SQLite's write lock,
        // which is held until commit/rollback, so no other request can change inventory or the queue
        // between the check and the commit (no read-then-write gap). No provider is called here.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.WaitlistEntries.Add(entry);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
            {
                // A concurrent retry with the same Idempotency-Key won the insert.
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                var winner = await FindByIdempotencyKeyAsync(idempotencyKey, fingerprint, cancellationToken)
                    ?? throw BookingException.Conflict("waitlist_conflict", "Could not join the waitlist. Please try again.");
                return (await GetStatusAsync(winner, cancellationToken), false);
            }

            if (await CouldBuyWholeGroupNowAsync(slot, entry.Id, selection.Tickets, cancellationToken))
            {
                // Nobody is ahead and everything fits: an ordinary purchase is possible, so the waitlist must
                // not reserve these places. The rollback leaves no entry (and no hold, order or session).
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw BookingException.Conflict("tickets_available",
                    "These tickets are available to buy now, so there is no need to join the waitlist. Please book them directly.");
            }
            await transaction.CommitAsync(cancellationToken);
        }

        await PromoteAsync(slot.Id, cancellationToken);
        return (await GetStatusAsync(entry.PublicReference, cancellationToken), true);
    }

    public async Task<WaitlistStatusDto> GetStatusAsync(Guid reference, CancellationToken cancellationToken)
    {
        var entry = await LoadByReferenceAsync(reference, cancellationToken);
        // A customer refresh moves the queue: lapsed offers are expired and freed places are offered to the
        // next waiting group (same guarded path as any other request). Accepted orders are not touched here;
        // their release still requires the payment reconciliation rules.
        await PromoteAsync(entry.TimeSlotId, cancellationToken);
        entry = await LoadByReferenceAsync(reference, cancellationToken);

        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event).FirstAsync(s => s.Id == entry.TimeSlotId, cancellationToken);
        var orderStatus = entry.OrderId is null
            ? (OrderStatus?)null
            : await db.Orders.AsNoTracking().Where(o => o.Id == entry.OrderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(cancellationToken);
        var entryTypes = entry.Items.Select(i => i.TicketTypeId).ToList();
        int? position = entry.Status == WaitlistStatus.Waiting
            ? await CompetingWaiting(entry.TimeSlotId, await IsSharedDepartureAsync(entry.TimeSlotId, cancellationToken), entryTypes)
                .CountAsync(w => w.Id < entry.Id, cancellationToken) + 1
            : null;

        return new WaitlistStatusDto(
            entry.PublicReference,
            entry.EventId,
            slot.Event!.Name,
            slot.Id,
            slot.StartUtc,
            CustomerStatus(entry.Status, orderStatus),
            position,
            entry.Status == WaitlistStatus.Notified ? entry.OfferExpiresUtc : null,
            entry.OrderId,
            CanAccept: entry.Status == WaitlistStatus.Notified,
            CanCancel: entry.Status is WaitlistStatus.Waiting or WaitlistStatus.Offered or WaitlistStatus.Notified,
            // An accepted offer whose order is still awaiting payment may reopen the same order's checkout
            // (same order, same frozen Stripe payload and idempotency key). Terminal orders never can.
            CanResumeCheckout: entry.Status == WaitlistStatus.Accepted
                && orderStatus is OrderStatus.PendingCheckout or OrderStatus.PendingPayment,
            await DescribeItemsAsync(entry.Items, cancellationToken));
    }

    /// <summary>
    /// Turns a notified offer into an ordinary held order (no new inventory is taken: the offer's
    /// reservation becomes the order's hold). Retrying returns the same order. Returns the order id;
    /// the caller then starts the normal Stripe checkout for it.
    /// </summary>
    public async Task<Guid> AcceptAsync(Guid reference, CancellationToken cancellationToken)
    {
        var entry = await LoadByReferenceAsync(reference, cancellationToken);
        await ExpireOffersAsync(entry.TimeSlotId, cancellationToken);
        entry = await LoadByReferenceAsync(reference, cancellationToken);

        switch (entry.Status)
        {
            case WaitlistStatus.Accepted:
                return entry.OrderId!.Value;
            case WaitlistStatus.Waiting or WaitlistStatus.Offered:
                throw BookingException.Conflict("offer_not_available", "Tickets are not available to buy yet. The organizer will contact you when your places are ready.");
            case WaitlistStatus.Expired:
                throw BookingException.Conflict("offer_expired", "This offer has expired and the places were passed to the next person in line.");
            case WaitlistStatus.Cancelled:
                throw BookingException.Conflict("waitlist_cancelled", "This waitlist entry was cancelled.");
        }

        var slot = await db.TimeSlots.AsNoTracking().FirstAsync(s => s.Id == entry.TimeSlotId, cancellationToken);
        if (!slot.Active || slot.StartUtc <= UtcNow)
        {
            throw BookingException.Conflict("slot_closed", "This time slot is no longer open for booking.");
        }

        var now = UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            EventId = entry.EventId,
            TimeSlotId = entry.TimeSlotId,
            RegistrationType = RegistrationType.GeneralAdmission,
            Status = OrderStatus.PendingCheckout,
            RequestIdempotencyKey = $"waitlist-{entry.PublicReference:N}",
            RequestFingerprint = entry.RequestFingerprint,
            CreatedUtc = now,
            PreCheckoutHoldExpiresUtc = now.AddMinutes(checkoutOptions.Value.PreCheckoutHoldMinutes),
            Items = await PriceItemsAsync(entry.Items, cancellationToken),
        };
        order.TotalCents = order.Items.Sum(item => checked((long)item.UnitPriceCents * item.Quantity));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Only a still-valid notified offer may become an order, and only once.
        var flipped = await db.WaitlistEntries
            .Where(w => w.Id == entry.Id && w.Status == WaitlistStatus.Notified && w.OfferExpiresUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Status, WaitlistStatus.Accepted)
                .SetProperty(w => w.OrderId, order.Id)
                .SetProperty(w => w.ClosedUtc, now), cancellationToken);
        if (flipped != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var current = await LoadByReferenceAsync(reference, cancellationToken);
            return current.Status == WaitlistStatus.Accepted
                ? current.OrderId!.Value
                : throw BookingException.Conflict("offer_expired", "This offer is no longer available.");
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return order.Id;
    }

    /// <summary>Leaves the waitlist. A pending offer returns its places and the queue advances. Repeating is harmless.</summary>
    public async Task<WaitlistStatusDto> CancelAsync(Guid reference, CancellationToken cancellationToken)
    {
        var entry = await LoadByReferenceAsync(reference, cancellationToken);
        if (entry.Status == WaitlistStatus.Accepted)
        {
            throw BookingException.Conflict("offer_already_accepted", "This offer was already accepted; manage the order instead.");
        }
        if (await CloseEntryAsync(entry, WaitlistStatus.Cancelled, "CancelledByCustomer", cancellationToken))
        {
            await PromoteAsync(entry.TimeSlotId, cancellationToken);
        }
        return await GetStatusAsync(reference, cancellationToken);
    }

    // ---- Queue movement -------------------------------------------------------------------

    /// <summary>
    /// Expires lapsed offers, then offers freed places to the oldest waiting entries in order.
    /// Safe to call from any request (no background scheduler). Each offer is its own transaction:
    /// the guarded hold and the Waiting->Offered flip commit together or not at all.
    /// </summary>
    public async Task PromoteAsync(int timeSlotId, CancellationToken cancellationToken)
    {
        await ExpireOffersAsync(timeSlotId, cancellationToken);
        var slotStart = await db.TimeSlots.AsNoTracking().Where(s => s.Id == timeSlotId).Select(s => s.StartUtc).FirstAsync(cancellationToken);
        if (slotStart <= UtcNow)
        {
            return;
        }

        var sharedDeparture = await IsSharedDepartureAsync(timeSlotId, cancellationToken);
        var waiting = await db.WaitlistEntries.AsNoTracking().Include(w => w.Items)
            .Where(w => w.TimeSlotId == timeSlotId && w.Status == WaitlistStatus.Waiting)
            .OrderBy(w => w.Id)
            .ToListAsync(cancellationToken);

        // Ticket types held back by an older entry that could not be served. Shared departures stop at the
        // first such entry (departure-wide FIFO). Independent-capacity departures skip only entries that
        // compete for a blocked type, so a VIP waiter never blocks General Admission. A multi-type entry
        // blocks all of its types, so overlapping later requests cannot bypass it.
        var blockedTypes = new HashSet<int>();
        foreach (var entry in waiting)
        {
            var types = entry.Items.Select(i => i.TicketTypeId).ToList();
            if (types.Any(blockedTypes.Contains))
            {
                blockedTypes.UnionWith(types);
                continue;
            }

            var now = UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await inventory.HoldTicketsAsync(timeSlotId, entry.Items.OrderBy(i => i.TicketTypeId).Select(i => (i.TicketTypeId, i.Quantity, "requested")));
            }
            catch (BookingException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (sharedDeparture)
                {
                    return; // strict departure-wide head-of-line
                }
                blockedTypes.UnionWith(types);
                continue;
            }

            var flipped = await db.WaitlistEntries
                .Where(w => w.Id == entry.Id && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.Status, WaitlistStatus.Offered)
                    .SetProperty(w => w.OfferedUtc, now)
                    .SetProperty(w => w.OfferExpiresUtc, now.Add(OfferLifetime)), cancellationToken);
            if (flipped != 1)
            {
                // Another request promoted or cancelled this entry first; undo our hold.
                await transaction.RollbackAsync(CancellationToken.None);
                continue;
            }
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Waitlist entry {EntryId} offered places on time slot {TimeSlotId}", entry.Id, timeSlotId);
        }
    }

    private Task<bool> IsSharedDepartureAsync(int timeSlotId, CancellationToken cancellationToken) =>
        db.TimeSlots.AnyAsync(t => t.Id == timeSlotId && t.SharedCapacity != null, cancellationToken);

    /// <summary>
    /// Waiting entries that compete with a request for <paramref name="ticketTypeIds"/>: everyone on a shared
    /// departure, otherwise only entries requesting at least one of the same ticket types.
    /// </summary>
    private IQueryable<WaitlistEntry> CompetingWaiting(int timeSlotId, bool sharedDeparture, IReadOnlyCollection<int> ticketTypeIds) =>
        db.WaitlistEntries.Where(w => w.TimeSlotId == timeSlotId && w.Status == WaitlistStatus.Waiting
            && (sharedDeparture || w.Items.Any(i => ticketTypeIds.Contains(i.TicketTypeId))));

    /// <summary>Returns the places of offers whose time ran out. Only the request that flips the status releases.</summary>
    public async Task ExpireOffersAsync(int timeSlotId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var expired = await db.WaitlistEntries.AsNoTracking().Include(w => w.Items)
            .Where(w => w.TimeSlotId == timeSlotId
                && (w.Status == WaitlistStatus.Offered || w.Status == WaitlistStatus.Notified)
                && w.OfferExpiresUtc <= now)
            .ToListAsync(cancellationToken);
        foreach (var entry in expired)
        {
            await CloseEntryAsync(entry, WaitlistStatus.Expired, "OfferExpired", cancellationToken);
        }
    }

    /// <summary>
    /// Closes a live entry. If it held an offer, its reserved places are returned in the same transaction.
    /// Guarded on the status we observed, so a concurrent accept/expire/cancel cannot release twice.
    /// </summary>
    private async Task<bool> CloseEntryAsync(WaitlistEntry entry, WaitlistStatus newStatus, string reason, CancellationToken cancellationToken)
    {
        if (entry.Status is not (WaitlistStatus.Waiting or WaitlistStatus.Offered or WaitlistStatus.Notified))
        {
            return false;
        }
        var now = UtcNow;
        var observed = entry.Status;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var flipped = await db.WaitlistEntries
            .Where(w => w.Id == entry.Id && w.Status == observed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Status, newStatus)
                .SetProperty(w => w.CloseReason, reason)
                .SetProperty(w => w.ClosedUtc, now), cancellationToken);
        if (flipped != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }
        if (observed is WaitlistStatus.Offered or WaitlistStatus.Notified)
        {
            await inventory.ReleaseReservedTicketsAsync(entry.TimeSlotId, entry.Items.Select(i => (i.TicketTypeId, i.Quantity)), $"waitlist entry {entry.Id}");
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // ---- Development-only operator actions ----------------------------------------------

    public async Task<IReadOnlyList<OperatorWaitlistEntryDto>> GetOutboxAsync(CancellationToken cancellationToken)
    {
        var slotIds = await db.WaitlistEntries.AsNoTracking().Select(w => w.TimeSlotId).Distinct().ToListAsync(cancellationToken);
        foreach (var slotId in slotIds)
        {
            await PromoteAsync(slotId, cancellationToken);
        }

        var entries = await db.WaitlistEntries.AsNoTracking().Include(w => w.Items).OrderBy(w => w.Id).ToListAsync(cancellationToken);
        var slots = await db.TimeSlots.AsNoTracking().Include(s => s.Event)
            .Where(s => slotIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var orderIds = entries.Where(e => e.OrderId is not null).Select(e => e.OrderId!.Value).ToList();
        var orderStatuses = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Status, cancellationToken);
        var ticketNames = await db.TicketTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        return entries.Select(e => new OperatorWaitlistEntryDto(
                e.Id,
                e.PublicReference,
                PrivateLink(e.PublicReference),
                e.CustomerEmail,
                slots[e.TimeSlotId].Event!.Name,
                e.TimeSlotId,
                slots[e.TimeSlotId].StartUtc,
                OperatorStatus(e.Status, e.OrderId is null ? null : orderStatuses.GetValueOrDefault(e.OrderId.Value)),
                e.CreatedUtc,
                e.OfferedUtc,
                e.NotifiedUtc,
                e.OfferExpiresUtc,
                e.OrderId,
                e.Items.OrderBy(i => i.TicketTypeId).Select(i => new WaitlistItemDto(ticketNames.GetValueOrDefault(i.TicketTypeId, "Ticket"), i.Quantity)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Owner records that they personally contacted the customer (this app sends nothing). Opens the
    /// purchase window and restarts the offer's expiry. Repeating it is harmless.
    /// </summary>
    public async Task MarkNotifiedAsync(Guid reference, CancellationToken cancellationToken)
    {
        var entry = await LoadByReferenceAsync(reference, cancellationToken);
        await ExpireOffersAsync(entry.TimeSlotId, cancellationToken);
        var now = UtcNow;
        var flipped = await db.WaitlistEntries
            .Where(w => w.Id == entry.Id && w.Status == WaitlistStatus.Offered && w.OfferExpiresUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Status, WaitlistStatus.Notified)
                .SetProperty(w => w.NotifiedUtc, now)
                .SetProperty(w => w.OfferExpiresUtc, now.Add(OfferLifetime)), cancellationToken);
        if (flipped == 1)
        {
            return;
        }
        var current = await LoadByReferenceAsync(reference, cancellationToken);
        if (current.Status is not (WaitlistStatus.Notified or WaitlistStatus.Accepted))
        {
            throw BookingException.Conflict("offer_not_ready", $"Only an offer with reserved places can be marked notified (current state: {current.Status}).");
        }
    }

    public async Task<IReadOnlyList<OperatorDepartureDto>> GetDeparturesAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var slots = await db.TimeSlots.AsNoTracking().Include(s => s.Event).Include(s => s.Inventories).ThenInclude(i => i.TicketType)
            .Where(s => s.Event!.RegistrationType == RegistrationType.GeneralAdmission && s.Active && s.StartUtc > now)
            .OrderBy(s => s.StartUtc)
            .ToListAsync(cancellationToken);
        var waiting = await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.Status == WaitlistStatus.Waiting)
            .GroupBy(w => w.TimeSlotId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return slots.Select(s => new OperatorDepartureDto(
                s.Id,
                s.Event!.Name,
                s.StartUtc,
                s.SharedCapacity,
                s.Inventories.Sum(i => i.Reserved + i.Sold),
                waiting.GetValueOrDefault(s.Id),
                s.Inventories.OrderBy(i => i.TicketTypeId)
                    .Select(i => new OperatorTicketCapacityDto(i.TicketTypeId, i.TicketType!.Name, i.Capacity, i.Reserved, i.Sold))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Raises a departure's limits (never lowers them, never below live held + sold tickets), then lets the
    /// queue claim the new places in order. Each limit change is a guarded UPDATE; all commit together.
    /// </summary>
    public async Task IncreaseCapacityAsync(int timeSlotId, IncreaseCapacityRequest request, CancellationToken cancellationToken)
    {
        var slot = await db.TimeSlots.AsNoTracking().Include(s => s.Event).Include(s => s.Inventories)
            .FirstOrDefaultAsync(s => s.Id == timeSlotId, cancellationToken)
            ?? throw BookingException.NotFound("That time slot does not exist.");
        if (slot.Event!.RegistrationType != RegistrationType.GeneralAdmission)
        {
            throw BookingException.Validation("Only General Admission departures have adjustable capacity.");
        }
        var typeChanges = request.TicketTypes ?? [];
        if (request.SharedCapacity is null && typeChanges.Count == 0)
        {
            throw BookingException.Validation("Provide a new shared capacity or at least one ticket type capacity.");
        }
        if (typeChanges.Select(c => c.TicketTypeId).Distinct().Count() != typeChanges.Count)
        {
            throw BookingException.Validation("Each ticket type may appear only once.");
        }
        foreach (var value in typeChanges.Select(c => c.Capacity).Append(request.SharedCapacity ?? 1))
        {
            if (value < 1 || value > WaitlistOptions.MaxCapacity)
            {
                throw BookingException.Validation($"Capacities must be whole numbers from 1 to {WaitlistOptions.MaxCapacity}.");
            }
        }
        if (request.SharedCapacity < slot.SharedCapacity)
        {
            throw BookingException.Validation($"The shared capacity can only be increased (currently {slot.SharedCapacity}).");
        }
        foreach (var change in typeChanges)
        {
            var row = slot.Inventories.FirstOrDefault(i => i.TicketTypeId == change.TicketTypeId)
                ?? throw BookingException.Validation($"Ticket type {change.TicketTypeId} is not offered for this time slot.");
            if (change.Capacity < row.Capacity)
            {
                throw BookingException.Validation($"Ticket type capacity can only be increased (currently {row.Capacity}).");
            }
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (request.SharedCapacity is int shared)
            {
                // Increase-only and live commitments are both checked inside the statement that writes, so a
                // request validated against an older value can never lower a limit another request just raised.
                var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "TimeSlots" SET "SharedCapacity" = {shared}
                    WHERE "Id" = {timeSlotId}
                      AND ("SharedCapacity" IS NULL OR "SharedCapacity" <= {shared})
                      AND (SELECT COALESCE(SUM("Reserved" + "Sold"), 0) FROM "SlotInventories" WHERE "TimeSlotId" = {timeSlotId}) <= {shared};
                    """, cancellationToken);
                if (updated != 1)
                {
                    var current = await db.TimeSlots.AsNoTracking().Where(s => s.Id == timeSlotId).Select(s => s.SharedCapacity).FirstAsync(cancellationToken);
                    throw current > shared
                        ? BookingException.Conflict("capacity_changed", $"The shared limit was already raised to {current} by another change; nothing was saved. Reload and try again.")
                        : BookingException.Conflict("below_commitments", "That capacity is below the tickets already held or sold for this departure.");
                }
            }
            foreach (var change in typeChanges)
            {
                var updated = await db.SlotInventories
                    .Where(i => i.TimeSlotId == timeSlotId && i.TicketTypeId == change.TicketTypeId
                        && i.Capacity <= change.Capacity && i.Reserved + i.Sold <= change.Capacity)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Capacity, change.Capacity), cancellationToken);
                if (updated != 1)
                {
                    // Throwing here rolls back any shared/type change already made in this transaction.
                    var current = await db.SlotInventories.AsNoTracking()
                        .Where(i => i.TimeSlotId == timeSlotId && i.TicketTypeId == change.TicketTypeId).Select(i => i.Capacity).FirstAsync(cancellationToken);
                    throw current > change.Capacity
                        ? BookingException.Conflict("capacity_changed", $"That ticket type was already raised to {current} by another change; nothing was saved. Reload and try again.")
                        : BookingException.Conflict("below_commitments", "That ticket type capacity is below the tickets already held or sold.");
                }
            }
            await transaction.CommitAsync(cancellationToken);
        }
        logger.LogWarning("Operator raised capacity on time slot {TimeSlotId}", timeSlotId);
        await PromoteAsync(timeSlotId, cancellationToken);
    }

    // ---- Helpers ------------------------------------------------------------------------

    /// <summary>People in line who have not been offered places yet.</summary>
    public Task<int> CountWaitingAsync(int timeSlotId, CancellationToken cancellationToken) =>
        db.WaitlistEntries.CountAsync(w => w.TimeSlotId == timeSlotId && w.Status == WaitlistStatus.Waiting, cancellationToken);

    public string PrivateLink(Guid reference) => $"{checkoutOptions.Value.ClientBaseUrl.TrimEnd('/')}/?waitlist={reference}";

    private static string CustomerStatus(WaitlistStatus status, OrderStatus? orderStatus) => status switch
    {
        WaitlistStatus.Waiting => "Waiting",
        WaitlistStatus.Offered => "OfferPendingContact",
        WaitlistStatus.Notified => "OfferAvailable",
        WaitlistStatus.Expired => "Expired",
        WaitlistStatus.Cancelled => "Cancelled",
        _ => AcceptedStatus(orderStatus),
    };

    private static string OperatorStatus(WaitlistStatus status, OrderStatus? orderStatus) => status switch
    {
        WaitlistStatus.Waiting => "Queued",
        WaitlistStatus.Offered => "OfferReadyToNotify",
        WaitlistStatus.Notified => "ManuallyNotified",
        WaitlistStatus.Expired => "Expired",
        WaitlistStatus.Cancelled => "Cancelled",
        _ => AcceptedStatus(orderStatus),
    };

    private static string AcceptedStatus(OrderStatus? orderStatus) => orderStatus switch
    {
        OrderStatus.Confirmed => "Purchased",
        OrderStatus.Released => "Lapsed",
        OrderStatus.RefundedByDemoReset => "Refunded",
        _ => "CheckoutStarted",
    };

    private static string NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxEmailLength || !MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
        {
            throw BookingException.Validation("Enter a valid email address so the organizer can contact you.");
        }
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// True when nobody else is Waiting for the departure and the whole group fits the current per-type
    /// and shared remaining capacity, i.e. the same group could be bought normally right now.
    /// Must run inside the join transaction after the insert (see <see cref="JoinAsync"/>).
    /// </summary>
    private async Task<bool> CouldBuyWholeGroupNowAsync(TimeSlot slot, int newEntryId, IReadOnlyList<TicketQuantityRequest> tickets, CancellationToken cancellationToken)
    {
        var sharedDeparture = await IsSharedDepartureAsync(slot.Id, cancellationToken);
        var requestedTypes = tickets.Select(t => t.TicketTypeId).ToList();
        if (await CompetingWaiting(slot.Id, sharedDeparture, requestedTypes).AnyAsync(w => w.Id != newEntryId, cancellationToken))
        {
            return false; // joining behind people already in line keeps the queue order
        }
        var rows = await db.SlotInventories.AsNoTracking()
            .Where(i => i.TimeSlotId == slot.Id)
            .Select(i => new { i.TicketTypeId, Remaining = i.Capacity - i.Reserved - i.Sold, Used = i.Reserved + i.Sold })
            .ToListAsync(cancellationToken);
        // Re-read inside the transaction: the limit may have been raised since the slot was loaded.
        var sharedCapacity = await db.TimeSlots.AsNoTracking().Where(s => s.Id == slot.Id).Select(s => s.SharedCapacity).FirstAsync(cancellationToken);
        if (sharedCapacity is int shared && shared - rows.Sum(r => r.Used) < tickets.Sum(t => t.Quantity))
        {
            return false;
        }
        return tickets.All(t => rows.Any(r => r.TicketTypeId == t.TicketTypeId && r.Remaining >= t.Quantity));
    }

    /// <summary>A group larger than the departure could ever hold would block the queue forever, so refuse it up front.</summary>
    private async Task ValidateGroupCanEverFitAsync(TimeSlot slot, IReadOnlyList<TicketQuantityRequest> tickets, CancellationToken cancellationToken)
    {
        var requestedIds = tickets.Select(t => t.TicketTypeId).ToList();
        var rows = await db.SlotInventories.AsNoTracking()
            .Where(i => i.TimeSlotId == slot.Id && requestedIds.Contains(i.TicketTypeId) && i.TicketType!.Active)
            .Select(i => new { i.TicketTypeId, i.Capacity, i.TicketType!.Name })
            .ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            var row = rows.FirstOrDefault(r => r.TicketTypeId == ticket.TicketTypeId)
                ?? throw BookingException.Validation($"Ticket type {ticket.TicketTypeId} is not offered for this time slot.");
            if (ticket.Quantity > row.Capacity)
            {
                throw BookingException.Validation($"This departure has only {row.Capacity} {row.Name} places in total.");
            }
        }
        if (slot.SharedCapacity is int shared && tickets.Sum(t => t.Quantity) > shared)
        {
            throw BookingException.Validation($"This departure holds at most {shared} people in total.");
        }
    }

    private async Task<List<OrderItem>> PriceItemsAsync(IEnumerable<WaitlistItem> items, CancellationToken cancellationToken)
    {
        var lines = items.OrderBy(i => i.TicketTypeId).ToList();
        var ids = lines.Select(i => i.TicketTypeId).ToList();
        var types = await db.TicketTypes.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        return lines.Select(i => new OrderItem
        {
            TicketTypeId = i.TicketTypeId,
            Description = types[i.TicketTypeId].Name,
            Quantity = i.Quantity,
            UnitPriceCents = types[i.TicketTypeId].PriceCents,
        }).ToList();
    }

    private async Task<IReadOnlyList<WaitlistItemDto>> DescribeItemsAsync(IEnumerable<WaitlistItem> items, CancellationToken cancellationToken)
    {
        var lines = items.OrderBy(i => i.TicketTypeId).ToList();
        var ids = lines.Select(i => i.TicketTypeId).ToList();
        var names = await db.TicketTypes.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return lines.Select(i => new WaitlistItemDto(names.GetValueOrDefault(i.TicketTypeId, "Ticket"), i.Quantity)).ToList();
    }

    private async Task<WaitlistEntry> LoadByReferenceAsync(Guid reference, CancellationToken cancellationToken) =>
        await db.WaitlistEntries.AsNoTracking().Include(w => w.Items).FirstOrDefaultAsync(w => w.PublicReference == reference, cancellationToken)
            ?? throw BookingException.NotFound("Waitlist entry not found.");

    private async Task<Guid?> FindByIdempotencyKeyAsync(string idempotencyKey, string fingerprint, CancellationToken cancellationToken)
    {
        var match = await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.RequestIdempotencyKey == idempotencyKey)
            .Select(w => new { w.PublicReference, w.RequestFingerprint })
            .FirstOrDefaultAsync(cancellationToken);
        if (match is null)
        {
            return null;
        }
        return match.RequestFingerprint == fingerprint
            ? match.PublicReference
            : throw BookingException.Conflict("idempotency_key_reused", "This Idempotency-Key was already used for a different waitlist request.");
    }
}
