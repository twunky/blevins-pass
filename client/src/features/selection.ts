import type { AppState } from '../context/appState';

export interface SelectionLine {
  key: string;
  description: string;
  quantity: number;
  unitPriceCents: number;
}

/**
 * Display-only summary of the current selection. The server re-prices everything when the order
 * is created; these numbers are never sent as payment amounts.
 */
export function selectionLines(state: AppState): SelectionLine[] {
  if (state.seatingChart && state.selectedSeatIds.length > 0) {
    return state.seatingChart.sections.flatMap((section) =>
      section.seats
        .filter((seat) => state.selectedSeatIds.includes(seat.id))
        .map((seat) => ({
          key: `seat-${seat.id}`,
          description: `${section.name} · Row ${seat.row}, Seat ${seat.seatNumber} (${seat.label})`,
          quantity: 1,
          unitPriceCents: section.priceCents,
        })),
    );
  }
  if (!state.availability) return [];
  return state.availability.ticketTypes
    .filter((ticket) => (state.ticketQuantities[ticket.ticketTypeId] ?? 0) > 0)
    .map((ticket) => ({
      key: `ticket-${ticket.ticketTypeId}`,
      description: ticket.name,
      quantity: state.ticketQuantities[ticket.ticketTypeId] ?? 0,
      unitPriceCents: ticket.priceCents,
    }));
}

export function selectionTotal(lines: SelectionLine[]): number {
  return lines.reduce((sum, line) => sum + line.unitPriceCents * line.quantity, 0);
}
