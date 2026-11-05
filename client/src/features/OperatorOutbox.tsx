import { useCallback, useEffect, useState } from 'react';
import { useApp } from '../context/appState';
import { api } from '../services/api';
import { formatVenueDateTime } from '../services/format';
import type { OperatorDeparture, OperatorStatus, OperatorWaitlistEntry } from '../types/api';

const statusLabels: Record<OperatorStatus, string> = {
  Queued: 'Queued — waiting for places',
  OfferReadyToNotify: 'Places reserved — contact this customer, then mark notified',
  ManuallyNotified: 'Manually notified — customer may buy',
  CheckoutStarted: 'Customer started checkout',
  Purchased: 'Purchased (payment verified)',
  Expired: 'Offer expired — places passed on',
  Cancelled: 'Cancelled',
  Lapsed: 'Checkout not completed — places passed on',
  Refunded: 'Test purchase refunded by demo reset',
};

/**
 * Development-only local operator page (?operator, Vite dev server only). This app sends no messages:
 * the owner contacts each customer personally, then records it here. Contact details and private links
 * appear only on this page, which the API serves only to a loopback Development caller.
 */
export function OperatorOutbox() {
  const { resetBooking } = useApp();
  const [entries, setEntries] = useState<OperatorWaitlistEntry[]>([]);
  const [departures, setDepartures] = useState<OperatorDeparture[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [outbox, slots] = await Promise.all([api.operator.getOutbox(), api.operator.getDepartures()]);
      setEntries(outbox);
      setDepartures(slots);
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Operator tools are unavailable.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const run = async (action: () => Promise<void>, done: string) => {
    setError(null);
    setMessage(null);
    try {
      await action();
      setMessage(done);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The change failed.');
    }
    await load();
  };

  return (
    <section className="card operator" aria-labelledby="operator-heading">
      <p className="eyebrow">Development tool · local owner only</p>
      <h2 id="operator-heading">Waitlist outbox</h2>
      <p className="muted">
        Nothing here sends email. For each “Places reserved” row, contact the customer yourself (copy the private link
        into your own message), then press “Mark notified”. Only then can they buy. Offers expire automatically.
      </p>
      {message && <p role="status">{message}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="actions">
        <button type="button" onClick={() => void load()}>
          Refresh
        </button>
        <button type="button" onClick={resetBooking}>
          Back to events
        </button>
      </div>

      {entries.length === 0 ? (
        <p className="muted">No one is on a waitlist.</p>
      ) : (
        <ol className="operator-list">
          {entries.map((entry) => (
            <li key={entry.reference} className={`operator-entry status-${entry.operatorStatus}`}>
              <p>
                <strong>#{entry.queueNumber}</strong> · {entry.eventName} · {formatVenueDateTime(entry.slotStartUtc)}
              </p>
              <p className="operator-status">{statusLabels[entry.operatorStatus]}</p>
              <p>
                {entry.items.map((item) => `${item.quantity} × ${item.description}`).join(', ')} · {entry.customerEmail}
              </p>
              {entry.offerExpiresUtc && ['OfferReadyToNotify', 'ManuallyNotified'].includes(entry.operatorStatus) && (
                <p className="muted small">Offer ends {formatVenueDateTime(entry.offerExpiresUtc)}</p>
              )}
              <p className="small">
                Private link: <span className="mono">{entry.privateLink}</span>
              </p>
              {entry.operatorStatus === 'OfferReadyToNotify' && (
                <button
                  type="button"
                  className="primary"
                  onClick={() => void run(() => api.operator.markNotified(entry.reference), `Recorded manual notification for #${entry.queueNumber}.`)}
                >
                  Mark notified (I contacted them)
                </button>
              )}
            </li>
          ))}
        </ol>
      )}

      <h3>Open more places</h3>
      <p className="muted small">Raise a departure’s limits; the waitlist claims new places first, in order. Limits can only go up.</p>
      {departures.map((departure) => (
        <CapacityForm
          key={`${departure.timeSlotId}:${departure.sharedCapacity}:${departure.ticketTypes.map((t) => t.capacity).join(',')}`}
          departure={departure}
          onSubmit={(sharedCapacity, typeCapacities) =>
            run(
              () =>
                api.operator.increaseCapacity(departure.timeSlotId, {
                  sharedCapacity,
                  ticketTypes: typeCapacities,
                }),
              `Capacity updated for ${departure.eventName}.`,
            )
          }
        />
      ))}
    </section>
  );
}

interface CapacityFormProps {
  departure: OperatorDeparture;
  onSubmit: (sharedCapacity: number | null, typeCapacities: { ticketTypeId: number; capacity: number }[]) => Promise<void>;
}

function CapacityForm({ departure, onSubmit }: CapacityFormProps) {
  const [shared, setShared] = useState(departure.sharedCapacity?.toString() ?? '');
  const [types, setTypes] = useState<Record<number, string>>(
    Object.fromEntries(departure.ticketTypes.map((t) => [t.ticketTypeId, t.capacity.toString()])),
  );

  const submit = () => {
    const sharedValue = shared.trim() === '' ? null : Number(shared);
    const changed = departure.ticketTypes
      .filter((t) => Number(types[t.ticketTypeId]) !== t.capacity)
      .map((t) => ({ ticketTypeId: t.ticketTypeId, capacity: Number(types[t.ticketTypeId]) }));
    void onSubmit(sharedValue === departure.sharedCapacity ? null : sharedValue, changed);
  };

  return (
    <fieldset className="capacity-form">
      <legend>
        {departure.eventName} · {formatVenueDateTime(departure.startUtc)}
      </legend>
      <p className="small muted">
        {departure.sharedCommitted} held or sold
        {departure.sharedCapacity !== null ? ` of ${departure.sharedCapacity} shared places` : ''} · {departure.waitingCount} waiting
      </p>
      <label>
        Shared limit
        <input type="number" min={1} value={shared} onChange={(event) => setShared(event.target.value)} />
      </label>
      {departure.ticketTypes.map((ticket) => (
        <label key={ticket.ticketTypeId}>
          {ticket.name} ({ticket.reserved + ticket.sold} held or sold)
          <input
            type="number"
            min={1}
            value={types[ticket.ticketTypeId] ?? ''}
            onChange={(event) => setTypes({ ...types, [ticket.ticketTypeId]: event.target.value })}
          />
        </label>
      ))}
      <button type="button" onClick={submit}>
        Save capacity
      </button>
    </fieldset>
  );
}
