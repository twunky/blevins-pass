import { useApp } from '../context/appState';
import { formatCents } from '../services/format';

const maxPerType = 20;

/** Ticket types for the selected slot with live remaining counts and quantity steppers. */
export function TicketSelector() {
  const { state, dispatch } = useApp();
  const availability = state.availability;
  if (!availability) return null;

  return (
    <div className="ticket-list">
      <h3>Tickets</h3>
      {availability.ticketTypes.map((ticket) => {
        const quantity = state.ticketQuantities[ticket.ticketTypeId] ?? 0;
        const limit = Math.min(ticket.remaining, maxPerType);
        const soldOut = ticket.remaining === 0;
        const setQuantity = (next: number) =>
          dispatch({ type: 'ticketQuantityChanged', ticketTypeId: ticket.ticketTypeId, quantity: Math.max(0, Math.min(next, limit)) });

        return (
          <div key={ticket.ticketTypeId} className={`ticket-row${soldOut ? ' sold-out' : ''}`}>
            <div>
              <strong>{ticket.name}</strong> <span className="price">{formatCents(ticket.priceCents)}</span>
              <p className="muted small">{ticket.description}</p>
              <p className={`remaining${ticket.remaining <= 3 ? ' low' : ''}`}>
                {soldOut ? 'Sold out' : `${ticket.remaining} of ${ticket.capacity} left`}
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
          </div>
        );
      })}
    </div>
  );
}
