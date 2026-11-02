// Shapes returned by the ASP.NET Core API (see docs/contracts/api-contract.md).
// Money is integer cents (USD). Timestamps are ISO-8601 UTC strings.

export type RegistrationType = 'GeneralAdmission' | 'ReservedSeating';
export type ThemeName = 'Fall' | 'Winter';
export type OrderStatus = 'PendingCheckout' | 'PendingPayment' | 'Confirmed' | 'Released' | 'RefundedByDemoReset';
export type SeatStatus = 'available' | 'held' | 'sold' | 'inactive';

export interface TimeSlot {
  id: number;
  startUtc: string;
  endUtc: string;
  isBookable: boolean;
}

export interface EventSummary {
  id: number;
  name: string;
  description: string;
  venue: string;
  registrationType: RegistrationType;
  imagePath: string | null;
  fromPriceCents: number | null;
  timeSlots: TimeSlot[];
}

export interface TicketAvailability {
  ticketTypeId: number;
  name: string;
  description: string;
  priceCents: number;
  capacity: number;
  remaining: number;
}

export interface SlotAvailability {
  eventId: number;
  timeSlotId: number;
  currency: string;
  isBookable: boolean;
  ticketTypes: TicketAvailability[];
}

export interface Seat {
  id: number;
  label: string;
  row: string;
  seatNumber: number;
  svgX: number;
  svgY: number;
  active: boolean;
}

export interface SeatingSection {
  id: number;
  code: string;
  name: string;
  priceCents: number;
  seats: Seat[];
}

export interface SeatingChart {
  eventId: number;
  currency: string;
  sections: SeatingSection[];
}

export interface SeatingAvailability {
  eventId: number;
  timeSlotId: number;
  isBookable: boolean;
  seats: { seatId: number; status: SeatStatus }[];
  sections: { sectionId: number; remaining: number; total: number }[];
}

export interface OrderItem {
  description: string;
  quantity: number;
  unitPriceCents: number;
  lineTotalCents: number;
}

export interface Order {
  id: string;
  eventId: number;
  eventName: string;
  timeSlotId: number;
  slotStartUtc: string;
  registrationType: RegistrationType;
  status: OrderStatus;
  releaseReason: string | null;
  currency: string;
  totalCents: number;
  customerEmail: string | null;
  paymentDeadlineUtc: string | null;
  items: OrderItem[];
}

export interface CheckoutSession {
  orderId: string;
  checkoutUrl: string;
  expiresUtc: string;
}

export type CreateOrderRequest =
  | { timeSlotId: number; tickets: { ticketTypeId: number; quantity: number }[] }
  | { timeSlotId: number; seatIds: number[] };

/** Development demo reset outcome (POST /api/dev/demo-reset). */
export interface DemoResetResult {
  completed: boolean;
  message: string;
  refundedOrders: { orderId: string; checkoutSessionId: string; paymentIntentId: string; refundIds: string[]; amountRefundedCents: number; providerStatus: string }[];
  releasedPendingOrders: number;
  blockers: { orderId: string; reason: string }[];
}
