# Waitlist guide and implementation review

Reviewed November5,2026 against the corrected Phase2 code. This guide describes the implemented local demo; it does not claim owner acceptance or real Stripe verification. Canonical technical rules: [booking contract](../contracts/booking-rules.md#waitlist-phase-2); exact requests/responses: [API contract](../contracts/api-contract.md).

## Customer workflow

1. Select a future General Admission event and departure. On a shared-capacity departure (hayride) the UI shows one **Join the waitlist** form when it is full or a queue exists. On departures without a shared limit, each unavailable ticket type shows its own **Join {type} waitlist** form (one type per request) while other types stay on sale. Closed/past departures show no form and the API rejects them. Reserved seating is excluded.
2. Choose requested ticket quantities and enter an email address. No card is collected and no Stripe call occurs. Each selected type allows1–20; the server also rejects a group larger than the departure's total shared/type limits.
3. Keep the private `/?waitlist={reference}` link. It shows status, requested tickets, venue time and queue position. Position counts waiting requests/groups, not individual riders. The email is not returned by the public status endpoint. There is no email lookup or lost-link recovery; treat the link as private access to that entry.
4. When the whole group fits, an offer reserves its places. The customer sees **Places are being set aside for you** and cannot purchase yet.
5. The organizer contacts the customer manually, then records notification. The customer can now choose **Buy these tickets** before the offer ends. This creates an ordinary server-priced order and opens Stripe-hosted **test** Checkout. Joining, allocation and notification never charge the customer.
6. If checkout is interrupted, return to the private link and choose **Resume checkout**. It retries the same pending order and existing/recoverable session. **I already paid — check payment status** verifies the order rather than starting another purchase. Only server-verified confirmation is a purchased ticket.

Before acceptance, the customer can leave the queue; an allocated hold is returned safely. After acceptance, manage cancellation through the order/payment flow, not the waitlist leave action. A released, confirmed or refunded order cannot resume checkout.

## Organizer workflow

Use the normal [setup/run instructions](../../README.md#run-it-powershell). Before the first upgrade, stop the API and preserve the owner's database backup; startup also creates a backup when upgrading a Phase1 schema. Do not use a reset/refund to prepare this walkthrough. Automated demonstrations should use a disposable fixture database.

1. Run the API in Development and the Vite dev server. Open [local waitlist outbox](http://127.0.0.1:5173/?operator), also linked from the footer.
2. Select **Refresh**. For a row labelled **Places reserved — contact this customer, then mark notified**, use its email and private link to contact that customer yourself. **The app sends no email or SMS.**
3. After actually contacting them, click **Mark notified (I contacted them)**. This records manual notification and opens the purchase window. Repeating it does not restart the window repeatedly.
4. If genuinely opening more places, use the appropriate departure under **Open more places**, raise the shared and/or relevant ticket-type limit, then choose **Save capacity**. Both limits can constrain a group. Changes cannot lower a current limit or live commitments; a stale concurrent change is refused rather than overwriting a larger increase. Increasing capacity feeds the queue first.
5. Refresh to see Purchased, Expired, Checkout not completed, or other outcomes. The outbox has no operator cancellation button; customers can cancel their own unaccepted entries through their private links.

These are Development-only local tools, gated by configuration, loopback and a dedicated header; changes additionally check the configured frontend Origin. They are not authenticated production administration. Customers' contact details and private links belong only in the local outbox and the organizer's intended notification.

## Allocation, timing and inventory rules

- **FIFO (owner-approved policy):** the oldest competing request is considered first, and a request that does not fit holds back the requests behind it that compete with it. On **shared-capacity** departures (hayride) the queue is departure-wide: if the oldest waiting group does not fit, nobody behind it is offered, and while anyone is `Waiting` all public sales for that departure are closed. On **independent-capacity** departures (`SharedCapacity` null, e.g. cider VIP/GA) FIFO applies **per ticket type**: a waiter blocks public sales and later competing offers only for the type(s) it requested; new requests may join behind it, so a VIP waiter never blocks General Admission. New requests there name exactly one type; older multi-type entries stay linked, are offered all-or-nothing, and block every type they include so overlapping requests cannot pass them. Strict ordering can intentionally leave places unused.
- **Real inventory:** an offer holds tickets in the same ledger as orders. For hayrides, Adult, Child and Family Wagon each consume one person under the owner's clarified rule:25 shared places per departure plus the type caps. An offer becoming an order does not allocate the tickets twice.
- **Offer window:** default24h (`Waitlist:OfferLifetimeMinutes = 1440`) starts on allocation and restarts once when notification is recorded. Notification near the initial deadline can therefore keep places reserved for almost48h before acceptance. This duration was an implementation assumption, not an owner-specified requirement.
- **After acceptance:** the ordinary order rules take over: default15-minute pre-checkout window,31-minute configured Stripe session lifetime, and provider verification before releasing a payable or uncertain session. The offer clock no longer releases accepted orders.
- **Request-driven progression:** status, availability, outbox and other booking requests can promote/expire offers; nothing runs on a background schedule. Only the availability/order/payment paths perform order reconciliation. A private status or outbox refresh alone does not release an abandoned accepted order.
- **Deduplication:** a repeated join with the same request key and same payload returns the same entry; changed payload conflicts. This is not one-entry-per-email enforcement. Accept, notification, checkout and release have their own retry protections.

## Status reference

| Customer status | Meaning / available next action |
|---|---|
| Waiting | No allocation or charge; refresh or leave. |
| OfferPendingContact | Capacity held; organizer has not recorded contact. |
| OfferAvailable | Manual notification recorded; buy before expiry or leave. |
| CheckoutStarted | Accepted into an order; resume while pending or verify payment. |
| Purchased | Linked order confirmed by server-side payment verification. |
| Expired | Unaccepted offer ended; its hold was released. |
| Cancelled | Entry closed, including customer cancellation or development reset. |
| Lapsed | Linked order released under payment rules. |
| Refunded | Test purchase refunded through the development reset workflow. |

## Review findings and proposed follow-up

Core capacity, FIFO write eligibility, monotonic capacity updates, retry recovery and offer progression have independent automated evidence. The [final Claude self-review](../../ai-sessions/PHASE-2/claude-final-review.md) identified further limitations; this documentation review checked their relevant source paths. No new execution or fix is claimed here.

| Finding | Impact and proposed disposition |
|---|---|
| **Medium: API allows joining when tickets are available** | Previously: a direct request could hold places without payment. **Implemented (final correction):** joins for currently purchasable selections are refused with `409 tickets_available` inside the join transaction. Operator cancellation remains not implemented. |
| **Low: accepted-order cleanup needs another path** | After someone accepts and abandons checkout, the next customer's private refresh may keep waiting after15min. Availability reads/order creation perform stale-order reconciliation; explicit order confirmation also reconciles that order. **Outbox refresh does not do this**, despite the Claude review's broader wording. Proposed follow-up: safely reconcile before status-driven promotion, or retain this documented limitation. |
| **Low: restricted UI entry conditions** | **Partly addressed (owner-approved partial-sellout policy):** independent-capacity departures now offer a per-type waitlist for each unavailable type. On shared departures a group that cannot fit the remaining places still cannot join through the UI unless a queue exists. |
| **Low: cancellation wording** | A development-reset closure currently says the customer left. Neutral closed-entry wording would be more accurate. No functional inventory impact. |

No application changes are authorized by this guide. Recommendations require a proposal/owner launch before implementation. The original Phase2 deadline remains November5 at01:32:33 Eastern.

## Verification and owner acceptance

Latest evidence: [independent final verification](../../ai-sessions/PHASE-2/codex-partial-sellout-verification.md) records114 server tests passed,0 failed/skipped, and client lint/build. Earlier independent evidence covers exact backend race probes and controlled-API browser resume checks. The earlier [capacity audit](CAPACITY-CONCURRENCY-AUDIT.md) is separate evidence. Payments were simulated; neither record proves a real waitlist purchase. No successful checks were unnecessarily repeated for this documentation-only task.

Owner walkthrough — **pending, not a record of passed checks**:

1. Queue two groups on a sold-out future GA departure; confirm no card/payment and save their private links.
2. Open enough capacity for the first group; verify its offer holds inventory and the second group's queue priority is preserved.
3. Contact the first customer manually, mark notification, and verify Buy becomes available only then.
4. Complete one real Stripe **test** purchase; verify confirmation and Purchased on the private link. Refresh and confirm no second order/charge appears.
5. Use automated clock-controlled tests for expiry/races rather than waiting24h or altering the real database clock. Verify pending, cancelled and interrupted-checkout behavior using isolated fixtures.

Record the actual owner outcome and time separately. Final native Claude/Codex transcripts and final raw session capture remain separate outstanding submission work. This task changed documentation only; no application code, live database, payments, resets/refunds or Git operations were changed/executed. Usage: **Not available**.


