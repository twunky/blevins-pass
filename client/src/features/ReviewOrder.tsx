import { selectedEvent, useApp } from '../context/appState';
import { formatCents, formatVenueDateTime } from '../services/format';
import { selectionLines, selectionTotal } from './selection';

/** Final review before handing off to Stripe-hosted Checkout. */
export function ReviewOrder() {
  const { state, dispatch, submitCheckout } = useApp();
  const event = selectedEvent(state);
  const slot = event?.timeSlots.find((s) => s.id === state.selectedSlotId);
  const lines = selectionLines(state);
  const busy = state.checkout.status === 'submitting' || state.checkout.status === 'redirecting';

  if (!event || !slot || lines.length === 0) {
    return (
      <section className="card">
        <p>Your selection is empty.</p>
        <button type="button" onClick={() => dispatch({ type: 'reviewClosed' })}>
          Back to events
        </button>
      </section>
    );
  }

  return (
    <section className="card review" aria-labelledby="review-heading">
      <p className="eyebrow">02 / A moment, reserved for you</p>
      <h2 id="review-heading">Review your order</h2>
      <p>
        <strong>{event.name}</strong>
        <br />
        {formatVenueDateTime(slot.startUtc)} (venue time) · {event.venue}
      </p>
      <table className="order-table">
        <thead>
          <tr>
            <th scope="col">Item</th>
            <th scope="col">Qty</th>
            <th scope="col">Price</th>
            <th scope="col">Subtotal</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.key}>
              <td>{line.description}</td>
              <td>{line.quantity}</td>
              <td>{formatCents(line.unitPriceCents)}</td>
              <td>{formatCents(line.unitPriceCents * line.quantity)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colSpan={3}>
              Total (USD)
            </th>
            <td>
              <strong>{formatCents(selectionTotal(lines))}</strong>
            </td>
          </tr>
        </tfoot>
      </table>
      <p className="muted small">
        Your tickets are held while you pay. You will enter your email on the secure Stripe page (test mode: use card 4242 4242
        4242 4242, any future date, any CVC).
      </p>
      {state.checkout.status === 'error' && (
        <p className="error" role="alert">
          {state.checkout.error}
        </p>
      )}
      <div className="actions">
        <button type="button" disabled={busy} onClick={() => dispatch({ type: 'reviewClosed' })}>
          Change selection
        </button>
        <button type="button" className="primary" disabled={busy} onClick={() => void submitCheckout()}>
          {busy ? 'Opening secure checkout…' : 'Pay with Stripe'}
        </button>
      </div>
    </section>
  );
}
