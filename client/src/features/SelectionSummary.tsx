import { useApp } from '../context/appState';
import { formatCents } from '../services/format';
import { selectionLines, selectionTotal } from './selection';

export function SelectionSummary() {
  const { state, dispatch } = useApp();
  const lines = selectionLines(state);
  const isSeating = state.seatingChart !== null && state.availability === null;

  return (
    <aside className="summary" aria-live="polite">
      <h3>Your selection</h3>
      {lines.length === 0 ? (
        <p className="muted">{isSeating ? 'Select seats on the chart.' : 'Choose ticket quantities above.'}</p>
      ) : (
        <ul>
          {lines.map((line) => (
            <li key={line.key}>
              <span>
                {line.quantity} × {line.description}
              </span>
              <span>{formatCents(line.unitPriceCents * line.quantity)}</span>
              {line.key.startsWith('seat-') && (
                <button
                  type="button"
                  className="link-button"
                  aria-label={`Remove ${line.description}`}
                  onClick={() => dispatch({ type: 'seatToggled', seatId: Number(line.key.slice(5)) })}
                >
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div className="summary-total">
        <span>Total</span>
        <strong>{formatCents(selectionTotal(lines))}</strong>
      </div>
      <button type="button" className="primary" disabled={lines.length === 0} onClick={() => dispatch({ type: 'reviewOpened' })}>
        Review order
      </button>
    </aside>
  );
}
