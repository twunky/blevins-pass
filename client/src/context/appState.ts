import { createContext, useContext, type Dispatch } from 'react';
import type {
  EventSummary,
  Order,
  SeatingAvailability,
  SeatingChart,
  SlotAvailability,
  ThemeName,
} from '../types/api';

/**
 * The single shared client state. The server remains the authority for prices and inventory;
 * everything here is a display copy that is refreshed after conflicts and purchases.
 */
export interface AppState {
  events: EventSummary[];
  eventsStatus: 'loading' | 'ready' | 'error';
  eventsError: string | null;

  theme: ThemeName;

  selectedEventId: number | null;
  selectedSlotId: number | null;

  availability: SlotAvailability | null;
  seatingChart: SeatingChart | null;
  seatingAvailability: SeatingAvailability | null;
  availabilityStatus: 'idle' | 'loading' | 'ready' | 'error';
  availabilityError: string | null;
  /** Bumped to force an availability reload (after a conflict or purchase). */
  availabilityVersion: number;

  /** General Admission selection: ticketTypeId -> quantity. */
  ticketQuantities: Record<number, number>;
  /** Reserved Seating selection. */
  selectedSeatIds: number[];

  view: 'browse' | 'review' | 'confirmation';
  checkout: CheckoutState;
}

export interface CheckoutState {
  status: 'idle' | 'submitting' | 'redirecting' | 'verifying' | 'error';
  /** Reused for retries of the same review so the server never creates a duplicate order. */
  idempotencyKey: string | null;
  order: Order | null;
  error: string | null;
  /** Order being verified after the Stripe redirect; kept so a failed verification can be retried. */
  returnOrderId: string | null;
}

export type AppAction =
  | { type: 'eventsLoaded'; events: EventSummary[] }
  | { type: 'eventsFailed'; error: string }
  | { type: 'themeChanged'; theme: ThemeName }
  | { type: 'eventSelected'; eventId: number }
  | { type: 'slotSelected'; slotId: number }
  | { type: 'availabilityLoading' }
  | { type: 'availabilityLoaded'; availability: SlotAvailability }
  | { type: 'seatingLoaded'; chart: SeatingChart; availability: SeatingAvailability }
  | { type: 'availabilityFailed'; error: string }
  | { type: 'availabilityRefreshRequested' }
  | { type: 'ticketQuantityChanged'; ticketTypeId: number; quantity: number }
  | { type: 'seatToggled'; seatId: number }
  | { type: 'reviewOpened' }
  | { type: 'reviewClosed' }
  | { type: 'checkoutSubmitting' }
  | { type: 'checkoutRedirecting'; order: Order }
  | { type: 'checkoutFailed'; error: string; conflict: boolean }
  | { type: 'returnVerifying'; orderId: string }
  | { type: 'returnVerified'; order: Order }
  | { type: 'bookingReset' }
  /** Server availability was reset: drop stale selections/checkout, keep theme and catalog, reload counts. */
  | { type: 'demoAvailabilityReset' };

export const initialState: AppState = {
  events: [],
  eventsStatus: 'loading',
  eventsError: null,
  theme: 'Fall',
  selectedEventId: null,
  selectedSlotId: null,
  availability: null,
  seatingChart: null,
  seatingAvailability: null,
  availabilityStatus: 'idle',
  availabilityError: null,
  availabilityVersion: 0,
  ticketQuantities: {},
  selectedSeatIds: [],
  view: 'browse',
  checkout: { status: 'idle', idempotencyKey: null, order: null, error: null, returnOrderId: null },
};

const idleCheckout: CheckoutState = initialState.checkout;

function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function appReducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case 'eventsLoaded':
      return { ...state, events: action.events, eventsStatus: 'ready', eventsError: null };
    case 'eventsFailed':
      return { ...state, eventsStatus: 'error', eventsError: action.error };

    // Theme changes deliberately leave every booking field untouched.
    case 'themeChanged':
      return { ...state, theme: action.theme };

    case 'eventSelected': {
      if (action.eventId === state.selectedEventId) return state;
      const selectedEvent = state.events.find((e) => e.id === action.eventId);
      const firstBookable = selectedEvent?.timeSlots.find((s) => s.isBookable) ?? null;
      // A different event invalidates the slot and any selections made for it.
      return {
        ...state,
        selectedEventId: action.eventId,
        selectedSlotId: firstBookable?.id ?? null,
        ...clearedSelection,
        view: 'browse',
        checkout: idleCheckout,
      };
    }
    case 'slotSelected':
      if (action.slotId === state.selectedSlotId) return state;
      return { ...state, selectedSlotId: action.slotId, ...clearedSelection, view: 'browse', checkout: idleCheckout };

    case 'availabilityLoading':
      return { ...state, availabilityStatus: 'loading', availabilityError: null };
    case 'availabilityLoaded': {
      // Drop quantities that no longer fit what the server says remains.
      const ticketQuantities: Record<number, number> = {};
      for (const ticket of action.availability.ticketTypes) {
        const quantity = Math.min(state.ticketQuantities[ticket.ticketTypeId] ?? 0, ticket.remaining);
        if (quantity > 0) ticketQuantities[ticket.ticketTypeId] = quantity;
      }
      return { ...state, availability: action.availability, ticketQuantities, availabilityStatus: 'ready' };
    }
    case 'seatingLoaded': {
      const available = new Set(action.availability.seats.filter((s) => s.status === 'available').map((s) => s.seatId));
      return {
        ...state,
        seatingChart: action.chart,
        seatingAvailability: action.availability,
        selectedSeatIds: state.selectedSeatIds.filter((id) => available.has(id)),
        availabilityStatus: 'ready',
      };
    }
    case 'availabilityFailed':
      return { ...state, availabilityStatus: 'error', availabilityError: action.error };
    case 'availabilityRefreshRequested':
      return { ...state, availabilityVersion: state.availabilityVersion + 1 };

    case 'ticketQuantityChanged': {
      const ticketQuantities = { ...state.ticketQuantities };
      if (action.quantity > 0) ticketQuantities[action.ticketTypeId] = action.quantity;
      else delete ticketQuantities[action.ticketTypeId];
      return { ...state, ticketQuantities, checkout: idleCheckout };
    }
    case 'seatToggled': {
      const selected = state.selectedSeatIds.includes(action.seatId)
        ? state.selectedSeatIds.filter((id) => id !== action.seatId)
        : [...state.selectedSeatIds, action.seatId];
      return { ...state, selectedSeatIds: selected, checkout: idleCheckout };
    }

    case 'reviewOpened':
      return {
        ...state,
        view: 'review',
        checkout: { ...idleCheckout, idempotencyKey: state.checkout.idempotencyKey ?? newIdempotencyKey() },
      };
    case 'reviewClosed':
      return { ...state, view: 'browse', checkout: { ...state.checkout, status: 'idle', error: null } };
    case 'checkoutSubmitting':
      return { ...state, checkout: { ...state.checkout, status: 'submitting', error: null } };
    case 'checkoutRedirecting':
      return { ...state, checkout: { ...state.checkout, status: 'redirecting', order: action.order } };
    case 'checkoutFailed':
      // A conflict means the selection no longer fits: go back, refresh counts, and use a fresh key next time.
      return action.conflict
        ? {
            ...state,
            view: 'browse',
            availabilityVersion: state.availabilityVersion + 1,
            checkout: { ...idleCheckout, status: 'error', error: action.error },
          }
        : { ...state, checkout: { ...state.checkout, status: 'error', error: action.error } };

    case 'returnVerifying':
      return {
        ...state,
        view: 'confirmation',
        checkout: { ...state.checkout, status: 'verifying', error: null, returnOrderId: action.orderId },
      };
    case 'returnVerified':
      return { ...state, view: 'confirmation', checkout: { ...state.checkout, status: 'idle', order: action.order } };

    case 'demoAvailabilityReset':
    case 'bookingReset':
      return {
        ...state,
        ...clearedSelection,
        view: 'browse',
        checkout: idleCheckout,
        availabilityVersion: state.availabilityVersion + 1,
      };
  }
}

const clearedSelection = {
  ticketQuantities: {},
  selectedSeatIds: [],
  availability: null,
  seatingChart: null,
  seatingAvailability: null,
  availabilityStatus: 'idle',
  availabilityError: null,
} satisfies Partial<AppState>;

export interface AppContextValue {
  state: AppState;
  dispatch: Dispatch<AppAction>;
  changeTheme: (theme: ThemeName) => Promise<void>;
  submitCheckout: () => Promise<void>;
  verifyReturn: (orderId: string, cancel: boolean) => Promise<void>;
  /** Leaves the confirmation (clears the order reference from the URL) and starts a new booking. */
  resetBooking: () => void;
}

export const AppContext = createContext<AppContextValue | null>(null);

export function useApp(): AppContextValue {
  const value = useContext(AppContext);
  if (!value) throw new Error('useApp must be used inside <AppProvider>.');
  return value;
}

/** Convenience selectors. */
export function selectedEvent(state: AppState): EventSummary | null {
  return state.events.find((e) => e.id === state.selectedEventId) ?? null;
}
