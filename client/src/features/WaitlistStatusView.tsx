import { useCallback, useEffect, useState } from 'react';
import { useApp } from '../context/appState';
import { api } from '../services/api';
import { formatVenueDateTime } from '../services/format';
import type { WaitlistCustomerStatus, WaitlistStatus } from '../types/api';

const headings: Record<WaitlistCustomerStatus, string> = {
  Waiting: 'You’re on the waitlist',
  OfferPendingContact: 'Places are being set aside for you',
  OfferAvailable: 'Your tickets are ready to buy',
  CheckoutStarted: 'Checkout started',
  Purchased: 'Purchased — you’re booked',
  Expired: 'This offer has expired',
  Cancelled: 'You left the waitlist',
  Lapsed: 'This offer was not completed',
  Refunded: 'This test purchase was refunded',
};

/** Private status page reached from a ?waitlist= link. Status always comes from the server. */
export function WaitlistStatusView() {
  const { state, resetBooking } = useApp();
  const reference = state.waitlistReference;
  const [entry, setEntry] = useState<WaitlistStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!reference) return;
    try {
      setEntry(await api.getWaitlist(reference));
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not load this waitlist entry.');
    }
  }, [reference]);

  useEffect(() => {
    void load();
  }, [load]);

  // Accepting is retry-safe on the server (same order), and checkout for an order always reuses its one
  // Stripe session, so a double click, a failed redirect or a reload followed by "Resume" is harmless.
  const buy = async () => {
    if (!reference) return;
    setBusy(true);
    setError(null);
    try {
      const orderId = entry?.canResumeCheckout && entry.orderId ? entry.orderId : (await api.acceptWaitlistOffer(reference)).id;
      const session = await api.startCheckout(orderId);
      window.location.assign(session.checkoutUrl); // Stripe-hosted Checkout (test mode)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not start checkout.');
      setBusy(false);
      void load();
    }
  };

  const leave = async () => {
    if (!reference) return;
    setBusy(true);
    try {
      setEntry(await api.cancelWaitlist(reference));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not leave the waitlist.');
    } finally {
      setBusy(false);
    }
  };

  if (!entry) {
    return (
      <section className="card" aria-busy={!error}>
        <h2>{error ? 'Waitlist entry unavailable' : 'Loading your waitlist status…'}</h2>
        {error && <p className="error" role="alert">{error}</p>}
        <button type="button" onClick={resetBooking}>
          Back to events
        </button>
      </section>
    );
  }

  return (
    <section className="card confirmation" aria-labelledby="waitlist-status-heading">
      <p className="eyebrow">Waitlist · not a ticket until purchased</p>
      <h2 id="waitlist-status-heading">{headings[entry.status]}</h2>
      <StatusExplanation entry={entry} />
      {error && <p className="error" role="alert">{error}</p>}

      <dl className="order-facts">
        <dt>Event</dt>
        <dd>{entry.eventName}</dd>
        <dt>When (venue time)</dt>
        <dd>{formatVenueDateTime(entry.slotStartUtc)}</dd>
        <dt>Requested</dt>
        <dd>{entry.items.map((item) => `${item.quantity} × ${item.description}`).join(', ')}</dd>
        {entry.offerExpiresUtc && (
          <>
            <dt>Offer held until</dt>
            <dd>{formatVenueDateTime(entry.offerExpiresUtc)}</dd>
          </>
        )}
      </dl>

      <div className="actions">
        {entry.canAccept && (
          <button type="button" className="primary" disabled={busy} onClick={() => void buy()}>
            {busy ? 'Opening checkout…' : 'Buy these tickets'}
          </button>
        )}
        {entry.canResumeCheckout && (
          <button type="button" className="primary" disabled={busy} onClick={() => void buy()}>
            {busy ? 'Opening checkout…' : 'Resume checkout'}
          </button>
        )}
        {entry.status === 'CheckoutStarted' && entry.orderId && (
          <a className="button" href={`/?orderId=${entry.orderId}`}>
            I already paid — check payment status
          </a>
        )}
        {entry.canCancel && (
          <button type="button" disabled={busy} onClick={() => void leave()}>
            Leave the waitlist
          </button>
        )}
        <button type="button" onClick={() => void load()} disabled={busy}>
          Refresh status
        </button>
        <button type="button" onClick={resetBooking}>
          Back to events
        </button>
      </div>
    </section>
  );
}

function StatusExplanation({ entry }: { entry: WaitlistStatus }) {
  switch (entry.status) {
    case 'Waiting':
      return (
        <p>
          You are number {entry.positionInLine} in line. Nothing has been charged and no payment details were taken. If
          places open up, the organizer will contact you personally.
        </p>
      );
    case 'OfferPendingContact':
      return <p>Places are reserved for your group. The organizer will contact you; you can buy once they have.</p>;
    case 'OfferAvailable':
      return <p>The organizer has contacted you. Buy before the offer ends, or the places go to the next person in line. You are charged only when you complete checkout.</p>;
    case 'CheckoutStarted':
      return (
        <p>
          Your order was created from this offer and your places are still held. If checkout did not open or was
          interrupted, use Resume checkout: it reopens the same order, so you cannot be charged twice.
        </p>
      );
    case 'Purchased':
      return <p>Payment verified. Your order reference is <span className="mono">{entry.orderId}</span>.</p>;
    case 'Expired':
      return <p>The offer ran out before purchase, so the places were passed to the next person in line. No payment was taken.</p>;
    case 'Lapsed':
      return <p>Checkout was not completed, so the places were passed on. No payment was taken.</p>;
    case 'Cancelled':
      return <p>This waitlist entry is closed. No payment was taken.</p>;
    case 'Refunded':
      return <p>This Stripe test-mode purchase was refunded by a development demo reset.</p>;
  }
}
