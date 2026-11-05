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

### Shared departure limit (Phase 2)
- A time slot may have `SharedCapacity` (null = none). Owner rule: **every GA ticket counts as one person**, including Family Wagon. The hayride has 25 per departure, and its type caps (Adult 25, Child 25, Family Wagon 3) still apply. No other event has a shared limit.
- Shared usage is not a second counter. It is the live sum of `Reserved + Sold` over all of the slot's ticket types. Every transition already moves those columns, so it cannot drift.
- The hold UPDATE checks the type cap **and** `SharedCapacity − sum(Reserved + Sold) >= quantity` in one statement. SQLite serializes writers, so concurrent mixed-type orders cannot both pass. Lines held earlier in the same order count, and a failed later line rolls the whole order back.
- Public `remaining` per type = min(type remaining, shared remaining), clamped at 0. A departure already over its shared limit (Phase 1 sales) shows 0 and refuses new holds. Historical orders are never cancelled or refunded.
- Family Wagon wording for future bookings: "One rider in a family wagon seat (counts as one person)". Order items keep their own description snapshot.

## Waitlist (Phase 2)

Customer/operator walkthrough and reviewed limits: [waitlist guide](../phase-2/WAITLIST-GUIDE.md). The API refuses joins for selections that can be bought now (F1, below). The UI offers a join form for the whole departure when a shared-capacity departure is full or queued, and a per-type "Join {type} waitlist" form under each unavailable type on independent-capacity departures.
- GA only, future active slots. Join = `{ timeSlotId, email, tickets }` + `Idempotency-Key`. The same key and request returns the same entry; a different request is `409 idempotency_key_reused`. **Joining never calls the payment provider and takes no card.** The email is stored for the owner only and is never returned by the public API. There is no listing or email lookup, so entries cannot be enumerated.
- **Only when buying is impossible (final review F1):** a new join is refused with `409 tickets_available` when no competing request is `Waiting` (anyone on a shared departure; the same type otherwise) and the whole group fits the current per-type and shared remaining capacity; the customer should book normally instead. The check runs inside the join transaction **after** the insert, which holds SQLite's write lock until commit or rollback, so a concurrent purchase, release or capacity change cannot slip between check and commit. A refusal leaves no entry, hold, order or payment call. Joining behind people already waiting is always allowed. A retry of an existing request (same key and request) returns its original entry even if places have since opened.
- Groups larger than a type's total capacity or the shared limit are rejected (they could never be served).
- **Queue order** = entry id (first come, first served). On **shared-capacity** departures (hayride) the queue is departure-wide: if the oldest waiting group does not fit, nobody behind it is offered, and while anyone is `Waiting` all public sales for that departure are closed. On **independent-capacity** departures (`SharedCapacity` null, e.g. cider VIP/GA) FIFO applies **per ticket type**: a waiter blocks public sales, offers and new joins only for the type(s) it requested, so a VIP waiter never blocks General Admission. New requests there name exactly one type; older multi-type entries stay linked, are offered all-or-nothing, and block every type they include so overlapping requests cannot pass them. Blocked public holds get `409 waitlist_active` and availability shows 0 for the blocked type(s). This rule is part of the public hold UPDATE itself (`NOT EXISTS` a competing `Waiting` entry), so a waitlist entry that commits just before a public hold always wins (review 001 fix: it was previously checked before the transaction).
- **Promotion** runs on availability reads, order creation, joins, cancels, **customer status refreshes**, capacity increases and outbox reads (there is no scheduler). **Limitation (final review F2):** customer status refreshes and the outbox only expire and promote *offers*; they do **not** reconcile abandoned accepted orders. Stale-order reconciliation runs on availability reads and new-order requests, and explicit order verification (`POST /orders/{id}/confirm`) follows the payment rules. Until one of those runs, places held by an abandoned accepted order stay held. Each offer is one transaction: the guarded hold (same UPDATE as orders, including the shared limit) plus the `Waiting → Offered` flip. If either fails, nothing changes.
- An offer reserves places in `SlotInventory.Reserved`, so neither public buyers nor other offers can take them.
- **Notification gate:** the operator sees an `Offered` entry as "places reserved, contact the customer". The owner contacts the customer personally and records it (`Offered → Notified`). Only `Notified` entries can be accepted.
- **Offer expiry:** `Waitlist:OfferLifetimeMinutes` (default 1440 = 24 h), counted from the offer and restarted at notification. An expired offer returns its places in the same transaction as the guarded status flip (exactly once), and the queue advances.
- **Accept:** a guarded `Notified → Accepted` flip (offer still unexpired) and a new ordinary `PendingCheckout` order commit in one transaction. The offer's reservation becomes the order's hold (no extra inventory move), priced by the server at that moment. Order idempotency key `waitlist-{reference}`; retries return the same order. Checkout then uses the normal order endpoints (one Stripe session per order).
- After acceptance the order lifecycle below applies unchanged. An unattempted checkout is released after the pre-checkout window, and a Stripe-confirmed expiry releases it. An **unknown provider outcome keeps the places held**, and the queue does not advance.
- **Resume checkout:** while the accepted order is `PendingCheckout` or `PendingPayment`, the status response has `canResumeCheckout: true`. The page then calls the normal order checkout again for the **same order**, which reuses its frozen Stripe payload and idempotency key (one session per order). Released, confirmed or refunded orders never offer it.
- **Cleanup boundary:** customer status and operator outbox refreshes promote/expire offers but do not reconcile abandoned accepted orders. Stale-order reconciliation runs on availability reads and order creation; explicit order confirmation reconciles that order. Do not assume a private status refresh alone frees an accepted order after its local deadline.
- Customer cancel: `Waiting`, `Offered` or `Notified` → `Cancelled` (offer places returned). Accepted entries cannot be cancelled here.
- Customer status labels: Waiting, OfferPendingContact, OfferAvailable, CheckoutStarted, Purchased, Expired, Cancelled, Lapsed (order released), Refunded. A waitlist entry is never a ticket; only a Confirmed order is.

### Local operator tools (Development only)
- `/api/dev/operator/...` answers 404 unless the app runs in Development with `Waitlist:OperatorToolsEnabled` and the caller is loopback. The `X-Local-Operator: owner` header is required (403), and changes also require `Origin` = `Checkout:ClientBaseUrl` (403). There is no CORS policy. This is not authentication; never enable it on a shared machine.
- Outbox labels: Queued, OfferReadyToNotify, ManuallyNotified, CheckoutStarted, Purchased, Expired, Cancelled, Lapsed, Refunded. The app sends no email. "ManuallyNotified" only records what the owner says they did.
- Capacity increase for one GA departure: shared and/or per-type limits, 1–10000, never lower than the current value and never below live `Reserved + Sold`. Both rules are predicates of the UPDATE that writes, so a request validated against an older value cannot overwrite a larger committed limit (`409 capacity_changed`; review 001 fix). All changes commit together, then the queue is promoted.

## Time zones
- Persistence and API: UTC. `UtcDateTimeConverter` marks SQLite values as UTC, and JSON ends in `Z`. Venue wall-clock times convert with America/New_York rules (`SeedData.VenueLocalToUtc`), never the server's local zone.
- Display: the browser formats every time with `timeZone: America/New_York` and prints the zone (EST/EDT), independent of the purchaser's own zone.
- The Phase 2 investigation found no reproducible defect. Server tests cover DST start/end, summer/winter, a venue evening that is the next UTC day, and the seed's venue date. The client check runs under UTC+14 through UTC−11. Stripe's own hosted pages and receipts are outside this app.

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
- Phase 2: in the same final transaction, open waitlist offers (`Offered`/`Notified`) become `Cancelled` (reason `DemoReset`) because their reserved places are zeroed. Waiting entries stay in line.

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
