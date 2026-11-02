import type {
  CheckoutSession,
  CreateOrderRequest,
  DemoResetResult,
  EventSummary,
  Order,
  SeatingAvailability,
  SeatingChart,
  SlotAvailability,
  ThemeName,
} from '../types/api';

/** An API failure carrying the server's structured error code and HTTP status. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch {
    throw new ApiError(0, 'network_error', 'Cannot reach the Blevins Pass server. Is the API running?');
  }

  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: { code: string; message: string } } | null;
    throw new ApiError(
      response.status,
      body?.error?.code ?? 'http_error',
      body?.error?.message ?? `Request failed (${response.status}).`,
    );
  }
  return (await response.json()) as T;
}

export const api = {
  getEvents: () => request<EventSummary[]>('/api/events'),
  getAvailability: (eventId: number, timeSlotId: number) =>
    request<SlotAvailability>(`/api/events/${eventId}/availability?timeSlotId=${timeSlotId}`),
  getSeatingChart: (eventId: number) => request<SeatingChart>(`/api/events/${eventId}/seating`),
  getSeatingAvailability: (eventId: number, timeSlotId: number) =>
    request<SeatingAvailability>(`/api/events/${eventId}/seating/availability?timeSlotId=${timeSlotId}`),
  createOrder: (body: CreateOrderRequest, idempotencyKey: string) =>
    request<Order>('/api/orders', {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  startCheckout: (orderId: string) => request<CheckoutSession>(`/api/orders/${orderId}/checkout`, { method: 'POST' }),
  confirmOrder: (orderId: string, cancel: boolean) =>
    request<Order>(`/api/orders/${orderId}/confirm`, { method: 'POST', body: JSON.stringify({ cancel }) }),
  /** Development-only; the API answers 404 unless Development + DemoReset:Enabled on loopback. */
  resetDemo: async (): Promise<DemoResetResult> => {
    // The API also checks the Origin header (sent by the browser) against the local app.
    const response = await fetch('/api/dev/demo-reset', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Demo-Reset': 'confirm' },
      body: JSON.stringify({ confirm: true }),
    });
    const body = (await response.json().catch(() => null)) as (DemoResetResult & { error?: { message: string } }) | null;
    if (response.ok && body) return body;
    if (response.status === 409 && body?.blockers) return body; // honest "not reset" result with reasons
    throw new ApiError(response.status, 'demo_reset_failed', body?.error?.message ?? `Reset failed (${response.status}).`);
  },
  getTheme: () => request<{ activeTheme: ThemeName }>('/api/settings/theme'),
  setTheme: (activeTheme: ThemeName) =>
    request<{ activeTheme: ThemeName }>('/api/settings/theme', { method: 'PUT', body: JSON.stringify({ activeTheme }) }),
};
