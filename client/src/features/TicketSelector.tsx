import { useApp } from '../context/appState';
import { formatCents } from '../services/format';
import { WaitlistJoin } from './WaitlistJoin';

const maxPerType = 20;

/**
 * Ticket types for the selected slot with live remaining counts and quantity steppers. When the
 * departure has a shared limit, the steppers also keep the whole group within the places left.
 */
export function TicketSelector() {
  const { state, dispatch } = useApp();
  const availability = state.availability;
  if (!availability) return null;

  const selectedTotal = Object.values(state.ticketQuantities).reduce((sum, quantity) => sum + quantity, 0);
  const sharedRemaining = availability.sharedRemaining;
  const soldOut = availability.ticketTypes.every((ticket) => ticket.remaining === 0);
  // Independent caps (no shared limit) queue per ticket type: each unavailable type gets its own request.
  const perTypeWaitlist = availability.sharedCapacity === null;

  return (
    <div className="ticket-list">
      <h3>Tickets</h3>
      {availability.sharedCapacity !== null && (
        <p className={`shared-capacity${sharedRemaining !== null && sharedRemaining <= 3 ? ' low' : ''}`}>
          {sharedRemaining === 0
            ? `This departure is full (${availability.sharedCapacity} people maximum).`
            : `${sharedRemaining} of ${availability.sharedCapacity} places left on this departure. Every ticket, including Family Wagon, counts as one person.`}
        </p>
      )}
      {availability.ticketTypes.map((ticket) => {
        const quantity = state.ticketQuantities[ticket.ticketTypeId] ?? 0;
        const sharedLimit = sharedRemaining === null ? Infinity : sharedRemaining - (selectedTotal - quantity);
        const limit = Math.min(ticket.remaining, maxPerType, sharedLimit);
        const typeSoldOut = ticket.remaining === 0;
        const setQuantity = (next: number) =>
          dispatch({ type: 'ticketQuantityChanged', ticketTypeId: ticket.ticketTypeId, quantity: Math.max(0, Math.min(next, limit)) });

        return (
          <div key={ticket.ticketTypeId} className={`ticket-row${typeSoldOut ? ' sold-out' : ''}`}>
            <div>
              <strong>{ticket.name}</strong> <span className="price">{formatCents(ticket.priceCents)}</span>
              <p className="muted small">{ticket.description}</p>
              <p className={`remaining${ticket.remaining <= 3 ? ' low' : ''}`}>
                {typeSoldOut ? 'Sold out' : `${ticket.remaining} available`}
              </p>
            </div>
            <div className="stepper" role="group" aria-label={`${ticket.name} quantity`}>
              <button type="button" aria-label={`Fewer ${ticket.name}`} disabled={quantity === 0} onClick={() => setQuantity(quantity - 1)}>
                −
              </button>
              <output aria-live="polite">{quantity}</output>
              <button type="button" aria-label={`More ${ticket.name}`} disabled={quantity >= limit} onClick={() => setQuantity(quantity + 1)}>
                +
              </button>
            </div>
            {perTypeWaitlist && availability.isBookable && typeSoldOut && (
              <div className="ticket-waitlist">
                <WaitlistJoin
                  key={`${availability.timeSlotId}-${ticket.ticketTypeId}`}
                  timeSlotId={availability.timeSlotId}
                  ticketTypes={[ticket]}
                  waitingCount={ticket.waitlistWaiting}
                  title={`Join ${ticket.name} waitlist`}
                />
              </div>
            )}
          </div>
        );
      })}
      {!perTypeWaitlist && availability.isBookable && (soldOut || availability.waitlistWaiting > 0) && (
        <WaitlistJoin
          key={availability.timeSlotId}
          timeSlotId={availability.timeSlotId}
          ticketTypes={availability.ticketTypes}
          waitingCount={availability.waitlistWaiting}
        />
      )}
    </div>
  );
}
