import { useState } from 'react';
import { useApp } from '../context/appState';
import { api } from '../services/api';
import type { DemoResetResult } from '../types/api';

type ResetPhase = 'idle' | 'confirming' | 'running' | 'done' | 'failed';

/**
 * Development/demo tool in the footer. Rendered only by the Vite dev server (import.meta.env.DEV);
 * the API separately requires Development mode, an opt-in setting and a loopback caller.
 */
export function DemoResetControl() {
  const { resetBooking, dispatch } = useApp();
  const [phase, setPhase] = useState<ResetPhase>('idle');
  const [message, setMessage] = useState('');
  const [details, setDetails] = useState<DemoResetResult | null>(null);

  const runReset = async () => {
    setPhase('running');
    setMessage('Resetting… checking Stripe and restoring availability.');
    setDetails(null);
    try {
      const result = await api.resetDemo();
      if (result.completed) {
        resetBooking(); // clears selections/checkout and the URL order reference; theme and events stay
        dispatch({ type: 'demoAvailabilityReset' });
        setPhase('done');
        setMessage(summarize(result));
      } else {
        setPhase('failed');
        setMessage(result.message);
        setDetails(result); // keep per-order outcomes so the owner can see what already happened at Stripe
      }
    } catch (error) {
      setPhase('failed');
      setMessage(error instanceof Error ? error.message : 'Reset failed.');
    }
  };

  return (
    <div className="demo-reset" aria-label="Development tool">
      <span className="demo-reset-label">Development tool</span>
      {phase === 'confirming' ? (
        <div className="demo-reset-confirm" role="group" aria-labelledby="demo-reset-explainer">
          <p id="demo-reset-explainer">
            Restore all ticket and seat availability, expire unpaid checkouts, and refund every completed Stripe test
            purchase made in this app. Order history is kept.
          </p>
          <div className="demo-reset-actions">
            <button type="button" onClick={() => setPhase('idle')}>
              Cancel
            </button>
            <button type="button" className="demo-reset-go" onClick={() => void runReset()}>
              Reset and refund
            </button>
          </div>
        </div>
      ) : (
        <button type="button" className="demo-reset-button" disabled={phase === 'running'} onClick={() => setPhase('confirming')}>
          {phase === 'running' ? 'Resetting…' : 'Reset demo availability'}
        </button>
      )}
      <p className={`demo-reset-status${phase === 'failed' ? ' failed' : ''}`} role="status" aria-live="polite">
        {message}
      </p>
      {details && (details.refundedOrders.length > 0 || details.blockers.length > 0) && (
        <ul className="demo-reset-details">
          {details.refundedOrders.map((r) => (
            <li key={`r-${r.orderId}`}>
              Order {r.orderId.slice(0, 8)}: {statusLabel(r.providerStatus)} ({r.refundIds.join(', ') || 'no refund id'})
            </li>
          ))}
          {details.blockers.map((b) => (
            <li key={`b-${b.orderId}`}>
              Order {b.orderId.slice(0, 8)}: {b.reason}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function statusLabel(providerStatus: string): string {
  if (providerStatus === 'refunded') return 'refund completed at Stripe';
  if (providerStatus === 'refund_pending') return 'refund pending at Stripe (not counted)';
  return 'refund incomplete (not counted)';
}

function summarize(result: DemoResetResult): string {
  const refunds = result.refundedOrders.length;
  return `Availability restored. ${refunds} test purchase${refunds === 1 ? '' : 's'} refunded, ${result.releasedPendingOrders} unpaid checkout${result.releasedPendingOrders === 1 ? '' : 's'} closed.`;
}
