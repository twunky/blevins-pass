# API contract (implemented; verification evidence in the final handoff)

Current build/test evidence and provider limits: [FINAL-HANDOFF](../FINAL-HANDOFF.md). Compiling or testing a route does not establish that a real purchase or refund was executed.

Base: `http://127.0.0.1:5050/api` (the client uses `/api` through the Vite proxy). JSON uses camelCase. Money is integer cents in USD. Timestamps are ISO-8601 UTC with a trailing `Z`.

**Errors:** `{ "error": { "code": "string", "message": "human readable" } }`. 400 validation (`validation_failed`), 404 `not_found`, 409 conflicts (`insufficient_capacity`, `seat_unavailable`, `slot_closed`, `idempotency_key_reused`, `order_released`, `order_already_confirmed`, `checkout_expired`, `checkout_rejected`, `payment_verification_failed`), 503 (`payment_provider_unavailable`, `payment_not_configured`; retryable).

| Route | Response |
|---|---|
| `GET /health` | `{ "status": "ok" }` |
| `GET /events` | `EventSummary[]`: `{ id, name, description, venue, registrationType: "GeneralAdmission"|"ReservedSeating", imagePath, fromPriceCents, timeSlots: [{ id, startUtc, endUtc, isBookable }] }` |
| `GET /events/{id}` | One `EventSummary` |
| `GET /events/{id}/availability?timeSlotId=` | `{ eventId, timeSlotId, currency, isBookable, ticketTypes: [{ ticketTypeId, name, description, priceCents, capacity, remaining }] }` (GA events only) |
| `GET /events/{id}/seating` | `{ eventId, currency, sections: [{ id, code, name, priceCents, seats: [{ id, label, row, seatNumber, svgX, svgY, active }] }] }` |
| `GET /events/{id}/seating/availability?timeSlotId=` | `{ eventId, timeSlotId, isBookable, seats: [{ seatId, status: available|held|sold|inactive }], sections: [{ sectionId, remaining, total }] }` |
| `POST /orders` | Header `Idempotency-Key` (1–100 chars). Body `{ timeSlotId, tickets: [{ ticketTypeId, quantity }] }` **or** `{ timeSlotId, seatIds: [..] }`. Returns `201 Order` when created, or `200 Order` for a retry of the same key and selection |
| `POST /orders/{id}/checkout` | `{ orderId, checkoutUrl, expiresUtc }`: creates or recovers the order's single Stripe session. 409 `order_released` if the order is no longer awaiting checkout. 503 while an earlier attempt's outcome is unresolved (safe to retry) |
| `POST /orders/{id}/confirm` | Body `{ "cancel": false }`. Reconciles with Stripe, returns `Order` |
| `GET /orders/{id}` and `GET /orders/{id}/confirmation` | `Order` (read-only; `status` shows whether it is confirmed) |
| `POST /dev/demo-reset` | **Development only** (404 otherwise). Requires `Origin: http://127.0.0.1:5173` (403), header `X-Demo-Reset: confirm` (403), `Content-Type: application/json` (415), and body `{ "confirm": true }` (400). `200` with `{ completed: true, message, refundedOrders: [{ orderId, checkoutSessionId, paymentIntentId, refundIds, amountRefundedCents, providerStatus }], releasedPendingOrders, blockers: [] }`, or `409` with the same shape, `completed: false`, and `blockers: [{ orderId, reason }]` (availability unchanged). See [booking rules](booking-rules.md#demo-reset-development-only-exception) |
| `GET /settings/theme`, `PUT /settings/theme` | `{ "activeTheme": "Fall"|"Winter" }` |

`Order` = `{ id, eventId, eventName, timeSlotId, slotStartUtc, registrationType, status: PendingCheckout|PendingPayment|Confirmed|Released|RefundedByDemoReset, releaseReason, currency, totalCents, customerEmail, paymentDeadlineUtc, items: [{ description, quantity, unitPriceCents, lineTotalCents }] }`.

Stripe redirects to `{ClientBaseUrl}/?orderId={id}&checkout=success|cancel`. The query string only tells the client which order to verify. It is never treated as proof of payment. Rules: [booking-rules.md](booking-rules.md).
