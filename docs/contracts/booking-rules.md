# Booking and payment rules (canonical)

Implemented in `server/EventRegistration.Api/Services/`; tested in `server/EventRegistration.Tests/`.

## Orders
- One order = one time slot and one registration type: `tickets` (General Admission) **or** `seatIds` (Reserved Seating), never both, never empty.
- Rejected: duplicate ticket types or seats, quantities outside 1–20, more than 8 seats, ticket types or seats from another event, inactive/past slots, wrong registration type, non-integer values.
- Prices come only from the server (integer cents, USD), snapshotted per order item. Totals use checked `long` arithmetic. No taxes, fees, or discounts.
- `POST /api/orders` requires `Idempotency-Key`. Same key + same selection returns the original order; same key + different selection is `409 idempotency_key_reused`.

## General Admission inventory
- One `SlotInventory` row per (slot, ticket type): `Remaining = Capacity − Reserved − Sold`, with a check constraint `Reserved + Sold <= Capacity`.
- Each line is claimed with a conditional UPDATE and must affect exactly one row. Any failure rolls back the whole order (all-or-nothing).

## Seats
- 18 physical seats (3 sections × 2 rows × 3), reused for each performance.
- A claim is a `SeatReservation` (Held/Sold/Released). A filtered unique index on (TimeSlotId, SeatId) for Held/Sold allows at most one live claim per seat per performance. Released rows don't block rebooking.
- Section remaining counts are derived from seat states. There is no second ledger.

## Lifecycle
`PendingCheckout → PendingPayment → Confirmed`, or `PendingCheckout/PendingPayment → Released(reason)`. Confirmed never releases.

| Situation | Action |
|---|---|
| Checkout started | One guarded UPDATE claims the attempt (`PendingCheckout` and no attempt yet) and freezes the payload's expiry. Every caller then reloads the order and sends exactly that frozen payload with the order's idempotency key. Stripe is called outside any transaction |
| Order already released or confirmed when checkout starts | No Stripe call. 409 |
| Stripe refuses the **first** attempt (4xx other than 409/429/idempotency error) | Release (`CheckoutCreationRejected`) |
| Stripe refuses a **recovery** attempt (an earlier attempt's outcome is unknown) | Keep the hold and return 503. A refusal does not prove no session exists |
| Timeout, network error, 5xx, 409, 429 or idempotency error | Keep the hold and return 503. A retry replays the same key and recovers the same session |
| Session created but the order is no longer `PendingCheckout` (defensive) | Expire the session at Stripe, return 409, never hand out the URL |
| Stripe not configured | 503 `payment_not_configured`. Hold kept, attempt marker cleared |
| Return, or `confirm` call | Retrieve the session server-side. If **paid** and it matches the order, session, test mode, payment mode, amount and currency: confirm |
| Session expired and unpaid | Release (`CheckoutExpired`) |
| Session still open | Keep the hold (a declined card can still retry) |
| `confirm` with `cancel: true` | Expire the session at Stripe first. Paid means confirm, expired means release (`CancelledByCustomer`) |
| Never reached checkout, older than 15 min | Release (`AbandonedBeforeCheckout`). The release UPDATE also requires "no attempt claimed", so it loses to a checkout that started meanwhile |
| Provider unreachable during reconciliation | Keep the hold. Elapsed time alone never releases |

Confirm and release each flip the order status with a guarded UPDATE inside a transaction. Only the request that flips the status moves inventory, so duplicate or concurrent calls are no-ops. Reconciliation runs on return, before new holds for a slot, and on availability reads for orders whose local deadline has passed.

Known limitation: if a recovery attempt keeps being refused, the hold stays in place until Stripe answers the replay or an operator resolves it. We chose to keep inventory held rather than risk selling seats that may already be paid for.

## Client return flow
Stripe redirects to `/?orderId=…&checkout=success|cancel`. The client keeps `?orderId=` in the URL until "Book another event", so refreshing re-verifies the same order with the server. The `cancel` flag is used once to request cancellation and then dropped. Neither flag is ever treated as proof of payment.

## Demo reset (development-only exception)

`POST /api/dev/demo-reset` exists only when **all** of these hold: the API runs in Development, `DemoReset:Enabled=true` (set in `appsettings.Development.json`; off in `appsettings.json`), and the caller is on loopback. Otherwise it returns 404. The footer button is rendered only by the Vite dev server.

Request boundary (checked in the controller before any reset or Stripe work; there is no CORS policy for this route):
- `Origin` must equal `Checkout:ClientBaseUrl` (`http://127.0.0.1:5173`). A missing or foreign origin gets 403.
- The header `X-Demo-Reset: confirm` is required, otherwise 403.
- `Content-Type: application/json` is required, otherwise 415.
- The body must be exactly `{ "confirm": true }`. A missing, false or malformed body gets 400.

Order of work (`DemoResetService`):
1. **Pending orders**: each goes through the normal cancel path. The Stripe session is expired first. If it was paid, the order becomes Confirmed; if expired, it is released. Unresolved or uncertain outcomes block the reset.
2. **Paid orders**: with `DemoReset:RefundCompletedTestPurchases=true` (the owner's decision), every Confirmed order made in this app is refunded.
   - Each refund is bound to that order's own stored Checkout Session and verified first: paid, test mode, client reference = order id, amount and currency.
   - The remaining unrefunded amount is refunded with the stable idempotency key `blevinspass-order-{id}-demo-reset-refund`.
   - Success is decided by re-reading the refunds at Stripe: only succeeded refunds count, and a pending refund is not success.
   - Live-mode payments are refused, and no other account payments are touched.
   - With the setting off, any paid order blocks the reset.
3. **Local reset**: only if nothing is blocked, one transaction does all of the following:
   - Refunded orders become `RefundedByDemoReset`; their refund ids go in `ReleaseReason` and the time in `ReleasedUtc`.
   - It re-checks that no Pending or Confirmed orders remain.
   - Every `SlotInventory` is set to Reserved=0 and Sold=0, with Capacity unchanged.
   - Every Held or Sold seat claim becomes Released.

   Catalog, prices, seats, theme, orders, items and Stripe references are kept. Nothing is deleted or reseeded.

`RefundedByDemoReset` is terminal. It is the only way a Confirmed order ever leaves Confirmed, and only through this development endpoint. It is never confirmed again, never offered checkout, and shown as "refunded" in the client.

Refunds cannot be rolled back. If a later step blocks, the response says `completed: false`. The message counts only `refunded` results as completed and names `refund_pending`/`not_fully_refunded` results separately, as not refunded. The per-order list is returned and shown in the UI, and leaves availability unchanged. A retry is safe: an already fully refunded payment is a no-op. A process-memory `BookingOperationGate` makes the reset wait for running booking requests and holds new ones back. That only coordinates the single local API process; the database guarded updates remain the real protection.
