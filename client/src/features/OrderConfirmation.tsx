import { useApp } from '../context/appState';
import { formatCents, formatLongDate, formatTime } from '../services/format';

/** Shown after returning from Stripe. Status comes from the server's verification, never the URL. */
export function OrderConfirmation() {
  const { state, verifyReturn, resetBooking } = useApp();
  const { order, status, error, returnOrderId } = state.checkout;

  if (status === 'verifying') {
    return (
      <section className="card" aria-busy="true">
        <h2>Verifying your payment…</h2>
        <p className="muted">Checking with Stripe. This takes a moment.</p>
      </section>
    );
  }

  if (!order) {
    return (
      <section className="card">
        <h2>We could not verify your order yet</h2>
        {error && <p className="error" role="alert">{error}</p>}
        <p className="muted">If you paid, your tickets are still held. Checking again is safe.</p>
        <div className="actions">
          <button type="button" onClick={resetBooking}>
            Back to events
          </button>
          {returnOrderId && (
            <button type="button" className="primary" onClick={() => void verifyReturn(returnOrderId, false)}>
              Try again
            </button>
          )}
        </div>
      </section>
    );
  }

  const heading = {
    Confirmed: 'You’re booked!',
    PendingPayment: 'Payment not completed yet',
    PendingCheckout: 'Checkout not completed',
    Released: 'This order was not completed',
    RefundedByDemoReset: 'This test purchase was refunded',
  }[order.status];

  return (
    <section className="card confirmation" aria-labelledby="confirmation-heading">
      <h2 id="confirmation-heading">{heading}</h2>
      {order.status === 'Confirmed' && (
        <p>
          Payment verified with Stripe{order.customerEmail ? (
            <>
              {' '}for <strong>{order.customerEmail}</strong>
            </>
          ) : null}
          . Keep your order reference below; this page can be refreshed or bookmarked.
        </p>
      )}
      {(order.status === 'PendingPayment' || order.status === 'PendingCheckout') && (
        <p>
          Your tickets are still held. If you paid, verification may take a moment.{' '}
          <button type="button" className="link-button" onClick={() => void verifyReturn(order.id, false)}>
            Check again
          </button>{' '}
          or{' '}
          <button type="button" className="link-button" onClick={() => void verifyReturn(order.id, true)}>
            cancel this order
          </button>
          .
        </p>
      )}
      {order.status === 'Released' && <p>No payment was taken and the tickets were returned. You can start a new booking.</p>}
      {order.status === 'RefundedByDemoReset' && (
        <p>This was a Stripe test-mode purchase. It was refunded by a development demo reset and its tickets were returned.</p>
      )}
      {error && <p className="error" role="alert">{error}</p>}

      <dl className="order-facts">
        <dt>Event</dt>
        <dd>{order.eventName}</dd>
        <dt>When</dt>
        <dd>
          {formatLongDate(order.slotStartUtc)} at {formatTime(order.slotStartUtc)} (Eastern)
        </dd>
        <dt>Order reference</dt>
        <dd className="mono">{order.id}</dd>
      </dl>
      <ul className="order-lines">
        {order.items.map((item, index) => (
          <li key={index}>
            <span>
              {item.quantity} × {item.description}
            </span>
            <span>{formatCents(item.lineTotalCents)}</span>
          </li>
        ))}
        <li className="total">
          <span>Total</span>
          <strong>{formatCents(order.totalCents)}</strong>
        </li>
      </ul>
      <button type="button" className="primary" onClick={resetBooking}>
        Book another event
      </button>
    </section>
  );
}
