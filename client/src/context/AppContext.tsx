import { useCallback, useEffect, useMemo, useReducer, useRef, type ReactNode } from 'react';
import { api, ApiError } from '../services/api';
import type { CreateOrderRequest, ThemeName } from '../types/api';
import { AppContext, appReducer, initialState, selectedEvent, type AppContextValue } from './appState';

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.';
}

/** Owns shared state (useReducer) and the async operations that update it. */
export function AppProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(appReducer, initialState);

  // Initial load: events and the persisted theme.
  useEffect(() => {
    api.getEvents()
      .then((events) => dispatch({ type: 'eventsLoaded', events }))
      .catch((error: unknown) => dispatch({ type: 'eventsFailed', error: errorMessage(error) }));
    api.getTheme()
      .then((theme) => dispatch({ type: 'themeChanged', theme: theme.activeTheme }))
      .catch(() => undefined); // Keep the default theme if settings are unavailable.
  }, []);

  // Load availability whenever the selected slot changes or a refresh is requested.
  const event = selectedEvent(state);
  const eventId = event?.id ?? null;
  const registrationType = event?.registrationType ?? null;
  const { selectedSlotId, availabilityVersion } = state;
  useEffect(() => {
    if (eventId === null || selectedSlotId === null) return;
    let cancelled = false;
    dispatch({ type: 'availabilityLoading' });

    const load = async () => {
      try {
        if (registrationType === 'ReservedSeating') {
          const [chart, availability] = await Promise.all([
            api.getSeatingChart(eventId),
            api.getSeatingAvailability(eventId, selectedSlotId),
          ]);
          if (!cancelled) dispatch({ type: 'seatingLoaded', chart, availability });
        } else {
          const availability = await api.getAvailability(eventId, selectedSlotId);
          if (!cancelled) dispatch({ type: 'availabilityLoaded', availability });
        }
      } catch (error) {
        if (!cancelled) dispatch({ type: 'availabilityFailed', error: errorMessage(error) });
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [eventId, registrationType, selectedSlotId, availabilityVersion]);

  const changeTheme = useCallback(
    async (theme: ThemeName) => {
      const previous = state.theme;
      dispatch({ type: 'themeChanged', theme });
      try {
        await api.setTheme(theme);
      } catch {
        dispatch({ type: 'themeChanged', theme: previous });
      }
    },
    [state.theme],
  );

  const submitCheckout = useCallback(async () => {
    const { selectedSlotId: slotId, checkout, ticketQuantities, selectedSeatIds } = state;
    if (slotId === null || checkout.idempotencyKey === null) return;

    const body: CreateOrderRequest =
      registrationType === 'ReservedSeating'
        ? { timeSlotId: slotId, seatIds: selectedSeatIds }
        : {
            timeSlotId: slotId,
            tickets: Object.entries(ticketQuantities).map(([ticketTypeId, quantity]) => ({
              ticketTypeId: Number(ticketTypeId),
              quantity,
            })),
          };

    dispatch({ type: 'checkoutSubmitting' });
    try {
      const order = await api.createOrder(body, checkout.idempotencyKey);
      const session = await api.startCheckout(order.id);
      dispatch({ type: 'checkoutRedirecting', order });
      window.location.assign(session.checkoutUrl); // Stripe-hosted Checkout (test mode)
    } catch (error) {
      const conflict = error instanceof ApiError && error.status === 409;
      dispatch({ type: 'checkoutFailed', error: errorMessage(error), conflict });
    }
  }, [state, registrationType]);

  // Only the most recent verification may update state, so a slow earlier response
  // (e.g. a double click on "Check again") can never overwrite a newer result.
  const latestVerification = useRef(0);
  const verifyReturn = useCallback(async (orderId: string, cancel: boolean) => {
    const requestNumber = ++latestVerification.current;
    dispatch({ type: 'returnVerifying', orderId });
    try {
      const order = await api.confirmOrder(orderId, cancel);
      if (requestNumber === latestVerification.current) dispatch({ type: 'returnVerified', order });
    } catch (error) {
      if (requestNumber === latestVerification.current) {
        dispatch({ type: 'checkoutFailed', error: errorMessage(error), conflict: false });
      }
    }
  }, []);

  // Returning from Stripe (or refreshing the confirmation page): the URL identifies the order,
  // and the server re-checks payment every time. The success/cancel flag is only a hint that the
  // buyer asked to cancel; it is dropped after first use so a refresh simply re-verifies.
  const returnHandled = useRef(false);
  useEffect(() => {
    if (returnHandled.current) return; // effects can run twice in development (StrictMode)
    returnHandled.current = true;
    const params = new URLSearchParams(window.location.search);
    const waitlistReference = params.get('waitlist');
    if (waitlistReference) {
      dispatch({ type: 'waitlistOpened', reference: waitlistReference });
      return;
    }
    if (import.meta.env.DEV && params.has('operator')) {
      dispatch({ type: 'operatorOpened' });
      return;
    }
    const orderId = params.get('orderId');
    if (!orderId) return;
    const cancelRequested = params.get('checkout') === 'cancel';
    window.history.replaceState(null, '', `${window.location.pathname}?orderId=${encodeURIComponent(orderId)}`);
    void verifyReturn(orderId, cancelRequested);
  }, [verifyReturn]);

  const resetBooking = useCallback(() => {
    window.history.replaceState(null, '', window.location.pathname);
    dispatch({ type: 'bookingReset' });
  }, []);

  const value = useMemo<AppContextValue>(
    () => ({ state, dispatch, changeTheme, submitCheckout, verifyReturn, resetBooking }),
    [state, changeTheme, submitCheckout, verifyReturn, resetBooking],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}
