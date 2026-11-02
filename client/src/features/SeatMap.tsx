import type { KeyboardEvent } from 'react';
import { useApp } from '../context/appState';
import { formatCents } from '../services/format';
import type { Seat, SeatStatus } from '../types/api';

type SeatDisplayState = SeatStatus | 'selected';

const statusText: Record<SeatDisplayState, string> = {
  available: 'available',
  selected: 'selected',
  held: 'temporarily held',
  sold: 'sold',
  inactive: 'unavailable',
};

const maxSeats = 8;
const seatRadius = 15;

/**
 * Native SVG seating chart. It only displays server state: availability comes from the API and
 * the server re-checks every seat when the order is created.
 */
export function SeatMap() {
  const { state, dispatch } = useApp();
  const chart = state.seatingChart;
  const availability = state.seatingAvailability;
  if (!chart || !availability) return null;

  const statusBySeat = new Map(availability.seats.map((s) => [s.seatId, s.status]));
  const remainingBySection = new Map(availability.sections.map((s) => [s.sectionId, s]));

  const displayState = (seat: Seat): SeatDisplayState => {
    if (state.selectedSeatIds.includes(seat.id)) return 'selected';
    return statusBySeat.get(seat.id) ?? 'inactive';
  };

  const toggle = (seat: Seat) => {
    const current = displayState(seat);
    if (current === 'selected' || (current === 'available' && state.selectedSeatIds.length < maxSeats)) {
      dispatch({ type: 'seatToggled', seatId: seat.id });
    }
  };

  const onSeatKeyDown = (event: KeyboardEvent, seat: Seat) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      toggle(seat);
    }
  };

  return (
    <div className="seat-map">
      <h3>Choose your seats</h3>
      <ul className="seat-legend" aria-label="Seat colors">
        {(['available', 'selected', 'held', 'sold', 'inactive'] as const).map((status) => (
          <li key={status}>
            <span className={`legend-dot seat-${status}`} aria-hidden="true" /> {statusText[status]}
          </li>
        ))}
      </ul>
      <div className="seat-map-scroll">
        <svg viewBox="0 0 600 340" role="group" aria-label="Seating chart, stage at the top">
          <rect x="150" y="16" width="300" height="44" rx="8" className="stage" />
          <text x="300" y="44" textAnchor="middle" className="stage-label">
            STAGE
          </text>

          {chart.sections.map((section) => {
            const xs = section.seats.map((s) => s.svgX);
            const ys = section.seats.map((s) => s.svgY);
            // Pad the seat block: room for row letters on the left, name on top, counts below.
            const left = Math.min(...xs) - 42;
            const top = Math.min(...ys) - 40;
            const width = Math.max(...xs) - Math.min(...xs) + 70;
            const height = Math.max(...ys) - Math.min(...ys) + 82;
            const remaining = remainingBySection.get(section.id);
            const rows = [...new Set(section.seats.map((s) => s.row))];

            return (
              <g key={section.id} aria-label={`${section.name}, ${formatCents(section.priceCents)} per seat`} role="group">
                <rect x={left} y={top} width={width} height={height} rx="10" className="section-outline" />
                <text x={left + width / 2} y={top + 16} textAnchor="middle" className="section-label">
                  {section.name} · {formatCents(section.priceCents)}
                </text>
                {remaining && (
                  <text x={left + width / 2} y={top + height - 8} textAnchor="middle" className="section-remaining">
                    {remaining.remaining} of {remaining.total} open
                  </text>
                )}
                {rows.map((row) => {
                  const rowSeat = section.seats.find((s) => s.row === row)!;
                  return (
                    <text key={row} x={left + 10} y={rowSeat.svgY + 4} className="row-label" aria-hidden="true">
                      {row}
                    </text>
                  );
                })}
                {section.seats.map((seat) => {
                  const seatState = displayState(seat);
                  const selectable = seatState === 'available' || seatState === 'selected';
                  return (
                    <g
                      key={seat.id}
                      className={`seat seat-${seatState}`}
                      role="button"
                      tabIndex={selectable ? 0 : -1}
                      aria-pressed={seatState === 'selected'}
                      aria-disabled={!selectable}
                      aria-label={`${section.name}, row ${seat.row}, seat ${seat.seatNumber}, ${formatCents(section.priceCents)}, ${statusText[seatState]}`}
                      onClick={() => toggle(seat)}
                      onKeyDown={(event) => onSeatKeyDown(event, seat)}
                    >
                      <rect className="seat-hit" x={seat.svgX - 20} y={seat.svgY - 20} width="40" height="40" aria-hidden="true" />
                      <circle cx={seat.svgX} cy={seat.svgY} r={seatRadius} />
                      <text x={seat.svgX} y={seat.svgY + 4} textAnchor="middle" aria-hidden="true">
                        {seat.seatNumber}
                      </text>
                    </g>
                  );
                })}
              </g>
            );
          })}
        </svg>
      </div>
      {state.selectedSeatIds.length >= maxSeats && <p className="muted small">You can select up to {maxSeats} seats per order.</p>}
    </div>
  );
}
