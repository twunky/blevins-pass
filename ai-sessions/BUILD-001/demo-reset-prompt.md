# Claude prompt — reset demo availability and refund the test purchase

Owner decisions: restore all demo ticket/seat availability (not merely selected quantities), and refund the completed Stripe test purchase. The owner explicitly permits a small extension because of the late start/computer issues. This does not erase the original two-hour record. This file is a prompt, not evidence of implementation or a completed refund.

---

Implement this small, focused follow-up in `C:\Dev\project-brushfire`. The owner authorizes the reset control and a refund of the completed application test purchase. Aim for 10–15 minutes, report actual overtime beyond the original November 1, 2026 11 p.m. Eastern stop, and avoid unrelated features or polish. Do not reset the original timer or claim an on-time submission if overtime occurs. Preserve the working application if safe completion is blocked.

Read AGENTS.md, CLAUDE.md, README.md, DESIGN_SYSTEM.md, docs/contracts/booking-rules.md, the current payment/order/inventory services and models, their tests, client Context/reducer and App.tsx. The owner overrides the previous feature freeze only for this focused follow-up. No Git commands, commits, pushes, merges, history changes, deployments, account settings, live payments, or extra agents. Do not print secrets or collect unrelated Stripe payments.

## Required behavior

Add **one unobtrusive “Reset demo availability” button** in the footer, labelled as a development/demo tool. Match both seasonal themes. Show it only in the local development interface; the API must independently enforce Development mode and an explicit opt-in setting, with a safe default outside development. Keep local binding and existing origin protections. Use a POST JSON endpoint, never a mutating GET. Do not build a general admin or refund portal.

The button explains its effect and asks for confirmation before running: restore all demo ticket/seat availability, expire unpaid application checkouts, and refund the identified completed test purchase. Cancel must do nothing. While running, disable repeats; show busy/success/error status accessibly. On verified success, clear stale selection/checkout state through Context, refresh current availability, preserve the selected season and catalog, and show a concise result. Use the existing presentation/state architecture.

“Reset” means every General Admission inventory row has Reserved=0 and Sold=0 with Capacity unchanged; every active seat claim becomes released so all active seats are available for every performance. Preserve event dates, ticket/section prices, physical seats, settings/theme, historical orders/items, and Stripe references. Never delete/reseed the database as a shortcut.

## Stripe and inventory safety

1. Identify the intended completed test purchase from this application's stored Checkout Session/order binding. Retrieve it server-side and verify livemode=false, matching order reference, currency/amount, and the associated PaymentIntent/charge. Do not pick the latest payment across the Stripe account. If multiple completed, not-yet-refunded app purchases make the target ambiguous, ask the owner which order before refunding; continue independent code work. The owner authorized the identified test purchase, not unrelated account payments.
2. A succeeded payment requires a **refund**, not PaymentIntent cancellation or Checkout expiration. Refund the remaining unrefunded amount of that exact purchase with a stable idempotency key; retrieve/verify the result. An already fully refunded purchase is a safe no-op. Never treat a timeout, pending refund or API acceptance alone as completed refund success. Use Stripe's real test API/connected Stripe capability when available; never simulate the actual refund.
3. Enumerate only this application's pending orders. Before releasing inventory, expire open unpaid Checkout Sessions and verify they cannot still accept payment. Recover unresolved creation using the established immutable payload/idempotency rules. If any outcome remains uncertain, keep affected claims and return an honest retryable result; do not blindly zero counters.
4. If a payment completes during expiration, verify/reconcile it. Do not silently refund additional purchases outside the authorized target. If another paid order prevents a safe global reset, report that blocker and retain its inventory until the owner resolves it. A refund failure must not make paid inventory available again.
5. Prevent reset from interleaving with new bookings, checkout creation, confirmation, or cleanup. Keep Stripe calls outside database transactions. For this single-process local demo, a shared operation gate may coordinate reset with those request entry points while the original database constraints/guarded writes remain authoritative. Document the single-process limitation; do not claim a process-memory lock is general database concurrency protection. Do not hold a SQLite transaction across provider calls.
6. Only after provider outcomes are verified, perform local reset atomically, preserving historical records and preventing late callbacks/reconciliation from re-selling or re-confirming reset orders. Introduce an explicit terminal demo-reset/refunded representation as necessary and handle it throughout the API/client confirmation states. Do not mislabel a refunded historical purchase as a fresh successful booking or silently weaken the normal Confirmed-never-released rule. This is an explicit development reset exception, documented separately from ordinary booking behavior.
7. Retrying/double-clicking must not refund twice, release twice, produce negative inventory or corrupt seat claims. External refunds cannot be rolled back if another step fails: report partial provider progress honestly, preserve safe local claims, and recover using provider state/idempotency on retry. Do not claim a distributed all-or-nothing transaction.

Reuse/extend ICheckoutGateway and the existing Stripe adapter only as needed. Avoid a schema change that would require deleting the existing database: EnsureCreated does not upgrade tables. If fields are necessary, provide a safe explicit schema update and preserve existing orders. A terminal enum string plus existing audit fields may suffice; choose the smallest maintainable design rather than hiding state inconsistencies.

Stripe references:
- https://docs.stripe.com/api/refunds/create
- https://docs.stripe.com/api/checkout/sessions/expire
- https://docs.stripe.com/api/payment_intents/cancel

## Tests limited to this change

Add focused SQLite-backed reset tests with the existing fake payment boundary:

- Verified refund/expiration restores every slot/type and performance/seat, without changing capacities/prices/theme/catalog or deleting history.
- Already-refunded and repeated reset are idempotent; cancellation of the UI confirmation does nothing.
- Refund failure, uncertain creation/provider response, or still-payable checkout cannot free unsafe inventory.
- Reset racing with booking/checkout/confirmation cannot oversell, re-confirm reset orders, or duplicate refunds.
- Live-mode targets and disabled/non-Development API use are rejected.

Run the new reset tests and existing lifecycle tests directly affected by any changed shared code, using `dotnet test ... --filter` with the actual class/test names. Do not rerun unrelated suites. Compile affected .NET projects and run client build/lint because frontend code changes. Use isolated build output if the owner's running API locks files; do not stop unrelated processes.

Browser-check only the new control: placement in both themes, confirmation cancel, busy/repeat protection, success refresh, meaningful failure, and small-screen usability. Do not manufacture additional paid orders just to demonstrate the reset. Keep automated failure/concurrency tests on fake Stripe outcomes; the actual authorized test refund must be verified against Stripe separately.

## Documentation and completion

Update the booking/API contracts, DESIGN_SYSTEM.md where the footer control is described, README development-only instructions/limitations, and a compact follow-up log/session archive. Record actual start/end, overtime, exact test commands/results, files changed, and unavailable metrics as Not available.

Report the refund target's order/session/payment/refund identifiers and provider status without customer details or secrets; whether availability really returned to capacity; any partial or unresolved operations; and the test filter/results. Say explicitly whether the real refund was executed or still requires owner action. Preserve all transcripts and label export gaps. Confirm no Git operations occurred. Stop after this change; do not expand into customer cancellations, partial refunds, event administration or new UI work.
