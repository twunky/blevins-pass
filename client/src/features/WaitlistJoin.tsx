import { useId, useState, type FormEvent } from 'react';
import { api } from '../services/api';
import type { TicketAvailability, WaitlistStatus } from '../types/api';

const maxPerType = 20;

interface WaitlistJoinProps {
  timeSlotId: number;
  ticketTypes: TicketAvailability[];
  waitingCount: number;
  /** Heading, e.g. "Join VIP waitlist" for a single-type request. */
  title?: string;
}

/**
 * Sold-out form. Joining only records the request: no card, no checkout and no charge. The form's
 * state is local and transient; the server holds the entry, reachable by its private link.
 */
export function WaitlistJoin({ timeSlotId, ticketTypes, waitingCount, title = 'Join the waitlist' }: WaitlistJoinProps) {
  const headingId = useId();
  const [email, setEmail] = useState('');
  const [quantities, setQuantities] = useState<Record<number, number>>({});
  // One key per form instance, so a retried submit can never create a second entry.
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [joined, setJoined] = useState<WaitlistStatus | null>(null);

  const tickets = Object.entries(quantities)
    .filter(([, quantity]) => quantity > 0)
    .map(([ticketTypeId, quantity]) => ({ ticketTypeId: Number(ticketTypeId), quantity }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      setJoined(await api.joinWaitlist({ timeSlotId, email, tickets }, idempotencyKey));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not join the waitlist.');
    } finally {
      setSubmitting(false);
    }
  };

  if (joined) {
    const link = `${window.location.origin}/?waitlist=${joined.reference}`;
    return (
      <section className="waitlist-panel" aria-live="polite">
        <h4>You’re on the waitlist</h4>
        <p>
          {joined.positionInLine !== null ? `You are number ${joined.positionInLine} in line. ` : ''}
          Nothing has been charged. If places open up, the organizer will contact you personally, and you can then
          choose to buy.
        </p>
        <p className="muted small">Keep this private link to check your status:</p>
        <p>
          <a className="mono" href={link}>
            {link}
          </a>
        </p>
      </section>
    );
  }

  return (
    <form className="waitlist-panel" onSubmit={(event) => void submit(event)} aria-labelledby={headingId}>
      <h4 id={headingId}>{title}</h4>
      <p className="muted small">
        {waitingCount > 0 ? `${waitingCount} ${waitingCount === 1 ? 'person is' : 'people are'} already waiting. ` : ''}
        First come, first served. Joining is free and takes no payment details; you pay only if you later accept an offer.
      </p>
      {ticketTypes.map((ticket) => {
        const quantity = quantities[ticket.ticketTypeId] ?? 0;
        const setQuantity = (next: number) =>
          setQuantities({ ...quantities, [ticket.ticketTypeId]: Math.max(0, Math.min(next, maxPerType)) });
        return (
          <div key={ticket.ticketTypeId} className="waitlist-row">
            <span>{ticket.name}</span>
            <div className="stepper" role="group" aria-label={`Waitlist ${ticket.name} quantity`}>
              <button type="button" aria-label={`Fewer ${ticket.name} on waitlist`} disabled={quantity === 0} onClick={() => setQuantity(quantity - 1)}>
                −
              </button>
              <output aria-live="polite">{quantity}</output>
              <button type="button" aria-label={`More ${ticket.name} on waitlist`} disabled={quantity >= maxPerType} onClick={() => setQuantity(quantity + 1)}>
                +
              </button>
            </div>
          </div>
        );
      })}
      <label className="waitlist-email">
        Email for the organizer to contact you
        <input type="email" required autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} />
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" className="primary" disabled={submitting || tickets.length === 0 || email.trim() === ''}>
        {submitting ? 'Joining…' : 'Join waitlist (no charge)'}
      </button>
    </form>
  );
}
