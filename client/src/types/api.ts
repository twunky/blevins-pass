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
  /** People waiting who compete for this type (whole departure when it has a shared limit). */
  waitlistWaiting: number;
}

export interface SlotAvailability {
  eventId: number;
  timeSlotId: number;
  currency: string;
  isBookable: boolean;
  /** Each type's remaining is already limited by the shared departure limit. */
  ticketTypes: TicketAvailability[];
  /** Shared head-count limit for the departure (every ticket is one person); null = none. */
  sharedCapacity: number | null;
  /** Places left across all ticket types (0 while anyone is on the waitlist); null = no shared limit. */
  sharedRemaining: number | null;
  waitlistWaiting: number;
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

export type WaitlistCustomerStatus =
  | 'Waiting'
  | 'OfferPendingContact'
  | 'OfferAvailable'
  | 'CheckoutStarted'
  | 'Purchased'
  | 'Expired'
  | 'Cancelled'
  | 'Lapsed'
  | 'Refunded';

export interface WaitlistItem {
  description: string;
  quantity: number;
}

/** Customer view of a waitlist entry (GET /api/waitlist/{reference}); never includes contact details. */
export interface WaitlistStatus {
  reference: string;
  eventId: number;
  eventName: string;
  timeSlotId: number;
  slotStartUtc: string;
  status: WaitlistCustomerStatus;
  positionInLine: number | null;
  offerExpiresUtc: string | null;
  orderId: string | null;
  canAccept: boolean;
  canCancel: boolean;
  /** Accepted offer whose order still awaits payment: reopen that same order's checkout. */
  canResumeCheckout: boolean;
  items: WaitlistItem[];
}

export interface JoinWaitlistRequest {
  timeSlotId: number;
  email: string;
  tickets: { ticketTypeId: number; quantity: number }[];
}

export type OperatorStatus =
  | 'Queued'
  | 'OfferReadyToNotify'
  | 'ManuallyNotified'
  | 'CheckoutStarted'
  | 'Purchased'
  | 'Expired'
  | 'Cancelled'
  | 'Lapsed'
  | 'Refunded';

/** Development-only operator outbox row (contains contact details). */
export interface OperatorWaitlistEntry {
  queueNumber: number;
  reference: string;
  privateLink: string;
  customerEmail: string;
  eventName: string;
  timeSlotId: number;
  slotStartUtc: string;
  operatorStatus: OperatorStatus;
  createdUtc: string;
  offeredUtc: string | null;
  notifiedUtc: string | null;
  offerExpiresUtc: string | null;
  orderId: string | null;
  items: WaitlistItem[];
}

export interface OperatorDeparture {
  timeSlotId: number;
  eventName: string;
  startUtc: string;
  sharedCapacity: number | null;
  sharedCommitted: number;
  waitingCount: number;
  ticketTypes: { ticketTypeId: number; name: string; capacity: number; reserved: number; sold: number }[];
}

export interface IncreaseCapacityRequest {
  sharedCapacity: number | null;
  ticketTypes: { ticketTypeId: number; capacity: number }[];
}
