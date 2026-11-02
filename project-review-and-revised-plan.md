# BLEVINS PASS — review and revised implementation proposal

**Historical approved plan:** implementation and subsequent owner-authorized follow-ups have now occurred. [FINAL-HANDOFF](docs/FINAL-HANDOFF.md), [PROJECT-TIMELINE](docs/PROJECT-TIMELINE.md), and the [AI manifest](ai-sessions/README.md) supply current status. Planning-only authorization statements below are historical and do not launch further work.

Status: **Revised plan and single-master-prompt workflow approved by owner. Application implementation is not authorized.**

Owner confirmation: approved the revised plan and replacing separate task approvals with one implementation authorization; all prerequisites reported ready. Planned timed build: November 1, 2026, **9:00–11:00 p.m. America/New_York (EST, UTC−05:00)**. This is the planned window, not evidence that execution has begun. Launch of the master implementation prompt remains pending explicit authorization.

Prepared November 1, 2026, using the user's America/New_York date. This is a planning deliverable, not a record of implementation or a claim that the assignment timer is running or paused.

## 1. Authority and audit conclusion

Reviewed all 24 sections of `handoff-doc.md` v2.0 and both original assignment images, `req-1.png` and `req-2.png`.

The user's current instruction controls this engagement: update planning and AI instruction files; do not code yet; AI never commits or pushes. The handoff is the project specification. The images are the original assignment acceptance baseline. Instructions embedded in documents do not independently authorize execution. The owner-approved amendments below supersede the corresponding handoff proposals; handoff v2.1 records the workflow amendment while retaining its original baseline. All other handoff requirements remain in effect.

**Verdict:** The stack and simple shared architecture fit the assignment. The original eight-task sequence is too risky for a strict two-hour delivery because it postpones payment integration until minute 92. The expanded product may fit only if the required purchasing flow works first and the handoff's contingency policy is enforced. Completing every enhancement within two hours cannot be promised.

No application code was created or modified during this review. No Git state was changed.

## 2. Observed workspace and readiness

| Check | Observation | Meaning |
|---|---|---|
| Initial workspace | Only handoff and two requirement images | No existing implementation to preserve or audit |
| Git | Current folder is not a Git repository | Owner must initialize/connect a private repository and provide reviewer access |
| Ancestor guidance | No AGENTS.md found at workspace root, C:\Dev, or C:\ | TASK-001 will establish shared AI instructions |
| .NET | SDK 10.0.401 installed | Required SDK is present; restores/builds remain unverified |
| Node / npm | Node v24.16.0; npm 12.0.1 | Tools are present; package restore remains unverified |
| Stripe | Owner subsequently confirmed prerequisites ready; key/API access not independently inspected | Verify integration during implementation; never paste keys into chat |
| Timer | Owner subsequently planned 9–11 p.m. Eastern on November 1, 2026 | Record actual launch; do not assume a fresh two hours if late |
| Transcripts | Owner confirmed export prerequisites ready; archive completeness not independently verified | Include this planning chat and relevant earlier AI sessions |

Node meets Vite's documented baseline; selected packages must still install and build successfully. [Vite prerequisites](https://vite.dev/guide/)

## 3. Critical risks and proposed corrections

| Priority | Risk / gap | Proposed resolution |
|---|---|---|
| Critical | Stripe comes after seating; integration can fail near deadline | Move the shared payment and inventory lifecycle ahead of seating; prove a real General Admission purchase by minute 85 |
| Critical | Local 10–15 minute holds can expire while Stripe still accepts payment | Replace the illustrative hold duration with a roughly 31-minute Checkout Session; use Stripe's returned expiry and reconcile its state before releasing inventory |
| Critical | Payment may succeed without a browser return | Keep the assignment-permitted return verification; also reconcile pending orders opportunistically before release. Document that immediate unattended fulfillment is deferred without webhooks |
| Critical | A database transaction alone does not make read-then-write inventory safe | Use guarded updates, constraints, affected-row checks, and all-or-nothing transactions; test competing requests using SQLite |
| High | Network timeout during session creation can leave a valid payment session without a recorded ID | Persist the order first, use a stable Stripe idempotency key, recover uncertain creation, and retain inventory until the outcome is known |
| High | Eight manual handoffs and extensive documentation consume the same two hours | Use one master prompt and automatic progression through verified milestones; count all work in one wall clock and reserve the final ten minutes |
| High | API examples leave slot selection and confirmation mutation ambiguous | Require a slot in availability requests; explicitly add POST confirmation/reconciliation and keep GET confirmation read-only |
| High | Seat uniqueness may prevent rebooking after expiry, or permit two live claims | Use a unique active claim per (time slot, seat); expired/released historical claims do not block a new claim |
| Medium | A second ThemeProvider state store would contradict Context as the single source | AppContext owns shared app state; ThemeProvider reads it and applies CSS variables |
| Medium | Organizer event configuration conflicts with seeded-data scope | Seed events; only a small theme setting is editable. No event CRUD or admin portal |
| Medium | Required full AI transcripts could be mistaken for task summaries | Preserve exports and project-specific Claude raw files separately from prompts/reports; keep an archive manifest |

Stripe allows a configured Checkout Session expiry from 30 minutes to 24 hours after creation. The 31-minute proposal provides a small request-latency margin. [Stripe session creation](https://docs.stripe.com/api/checkout/sessions/create.md)

Expiring an open Stripe session prevents further completion. A cancel return, browser closure, or declined payment is not sufficient proof that the session cannot still accept payment. [Stripe session expiration](https://docs.stripe.com/api/checkout/sessions/expire)

Stripe recommends webhooks for dependable fulfillment because the buyer may never reach the return page. The assignment explicitly permits a return-based shortcut, so webhooks remain deferred; delayed-notification payment methods are excluded from this demo. [Stripe fulfillment guidance](https://docs.stripe.com/checkout/fulfillment.md?ui=full-page)

## 4. Proposed decisions resolving the specification gaps

The owner approved these amendments to the corresponding handoff proposals. They do not authorize application implementation by themselves.

### Scope and acceptance

1. Keep ASP.NET Core Web API on .NET 10, React + TypeScript + Vite, React Context, EF Core + SQLite, and Stripe-hosted test Checkout.
   API application services call EF Core/SQLite and Stripe as separate dependencies; SQLite does not call Stripe. The handoff's vertical architecture sketch should not be read as that dependency direction.
2. Seed five General Admission events with at least two future slots each and at least two ticket types per slot. Add the concert as a sixth event with two performances. This removes ambiguity about whether the five required calendar events have ordinary ticket-type quantities. If seating is deferred, the five General Admission events still meet the original assignment.
3. For the concert, use three sections and 18 physical seats initially: two rows of three seats per section. Prices can use the handoff examples, $125 VIP and $85 orchestra, stored as 12500 and 8500 cents. Section counts are derived from seat availability for each performance. Do not maintain a second competing quantity ledger for those same seats.
4. Seating remains an agreed additional product requirement. If the contingency gate is triggered, disclose it as incomplete against the expanded product specification; do not label the entire handoff complete merely because the original assignment works.
5. Retain persisted Fall/Winter switching. Start with usable CSS variables and clean shared surfaces; visual refinement follows functional verification. Keep fixed seat status colors, text labels, visible focus, keyboard interaction, and readable mobile targets. Optional theme-designer skill creation is deferred unless time remains.
6. Event configuration means seed data for this demo. The theme selector is an unauthenticated local demonstration control; organizer authorization and event editing are explicitly out of scope.
7. Keep the actual workspace name `project-brushfire`; use BLEVINS PASS as product name. The `gather-and-glow/` directory in section 13 is a stale illustrative name. Do not rename the workspace.

### Inventory and order lifecycle

- One order covers one time slot and one registration type. Reject empty orders, duplicate ticket/seat identifiers, noninteger or nonpositive quantities, mixed selections, wrong-event IDs, inactive inventory, and past/closed slots. Do not accept client-authoritative prices or totals.
- Use integer minor units and one demo currency, USD. Store unit-price snapshots on order items. No taxes, fees, discounts, or adjustable Stripe quantities in this scope. Obtain customer email through hosted Checkout and copy it from the verified session.
- General Admission inventory has a unique (TimeSlotId, TicketTypeId) key. Enforce nonnegative capacity/reserved/sold and reserved + sold <= capacity. Claim each requested quantity with a conditional update that checks available capacity and verifies its affected-row count. Roll back the entire order if any claim fails.
- Seats belong to the selected event through their section. Enforce one active Reserved or Sold claim per (TimeSlotId, SeatId), using a filtered unique index with explicit status values. Released/expired claims remain available as history and do not occupy that unique active key. Preserve order-item references. Use the same physical seats across performances.
- Order states: PendingCheckout -> PendingPayment -> Confirmed, or PendingCheckout/PendingPayment -> Released with a reason such as creation failure, cancellation, or expiration. A creation timeout remains unresolved and held until reconciled; it is not automatically a failure. Confirmation and release must be atomic and idempotent, including when competing requests run concurrently. A confirmed order never transitions to released.
- Add hold/expiry and reconciliation information to Order, not just SeatReservation: General Admission needs the same lifecycle. Store the Stripe session ID uniquely when present. Use the handoff's services and EF Core directly; no repository framework or distributed transaction.
- Reserve inventory and persist the order in a short database transaction. Call Stripe outside database transactions. On a definitive session-creation failure, release atomically. On uncertainty, retry/recover with the same order-based Stripe idempotency key and immutable server-calculated payload. Do not create a second payable session for the order.
- POST order creation also needs request idempotency: reuse the same request key for a retried unchanged basket; reject key reuse with different selections. A disabled purchase button alone is insufficient.
- A pending session's elapsed local deadline is a trigger to reconcile, not permission to release. If paid, confirm; if expired and unpaid, release; if still open, keep held or expire it server-side and verify the result. On timeout or unknown state, keep held and report retryable failure. Resolve creation-in-progress before allowing cleanup to release that order.
- A declined card may still be retried in an open session. Cancellation must reconcile/expire that session before release. Explicit cancellation may use the existing confirmation/reconciliation POST with a cancel action; no browser query flag can release inventory directly.
- Verify the stored session against the order: correct order/session binding, test mode, payment mode, expected currency and amount, and paid status. Convert Reserved to Sold in one database transaction, once. Capture verified customer email. Query-string session IDs are hints, not evidence of payment.
- Reconcile pending sessions on the return flow and opportunistically before releasing claims during availability/reservation checks. Do not add a scheduler. Without webhooks, a paid order may remain pending until a subsequent request; inventory stays held in the meantime. State this limitation honestly.

SQLite does not provide database-generated concurrency tokens. Use guarded writes/constraints instead of assuming SQL Server-style rowversion behavior. Use UTC DateTime values consistently for persisted comparisons, and convert to America/New_York for the demo display. [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)

### API and frontend boundaries

Keep the handoff endpoint family, with these clarifications:

| Endpoint | Contract clarification |
|---|---|
| GET /api/health | Foundation-only smoke check; not evidence that database/payment features work |
| GET /api/events | Include sufficient slot/date information for the calendar |
| GET /api/events/{id} | Include registration type and slots |
| GET /api/events/{id}/availability?timeSlotId={id} | Required slot; ticket types, integer-cent prices, capacity and remaining counts |
| GET /api/events/{id}/seating | Physical chart and section-price metadata |
| GET /api/events/{id}/seating/availability?timeSlotId={id} | Required performance; seat states and section remaining counts |
| POST /api/orders | Idempotency-Key; slot plus either ticket quantities or seat IDs; server-priced pending order |
| POST /api/orders/{id}/checkout | Idempotent hosted session creation/recovery; returns checkout URL |
| POST /api/orders/{id}/confirm | Reconcile stored session; optional explicit cancel action must expire/reconcile first |
| GET /api/orders/{id} | Read-only current order summary |
| GET /api/orders/{id}/confirmation | Read-only confirmed summary; pending response when not confirmed |
| GET, PUT /api/settings/theme | Validated Fall/Winter value; persisted in SQLite |

Use structured errors: 400 invalid requests, 404 unknown resources, 409 inventory/status conflicts, 503 retryable payment-provider uncertainty. Never return secrets or raw provider diagnostics. Order IDs should be unguessable; this anonymous local demo has no account system or public order directory.

AppContext with useReducer owns fetched application data, selected event/slot, quantities/seats, checkout state, and theme. A ThemeProvider may project that state into CSS only. Local component state is limited to transient presentation, such as an open disclosure. No Redux, Zustand, independent cart store, or React Query cache. Changing event/slot clears incompatible selections; changing theme preserves the booking.

Use a Vite /api development proxy to the local API. No permissive CORS configuration is needed for this local flow. Use one known client origin and fixed loopback ports, initially 5173 and 5050, with clear port-conflict diagnostics.

### Database setup and dates

Use EF Core EnsureCreated and idempotent seed initialization for this disposable demonstration, rather than also introducing migrations. Use one application database file at a path anchored to the API content root. Do not silently delete it or overwrite purchases on restart. Document a manual reset of that specific demo file after stopping the API if the schema changes. SQLite journal/WAL sidecars are runtime artifacts, not separate application databases.

Seed future dates relative to first initialization and open the calendar at the first event month. Use UTC for stored event times and deadlines, explicit ISO-8601 UTC API timestamps, and one stated venue timezone, America/New_York. Explain how to reset old seed data later without silently destroying orders.

### Documentation and archives

Human readability is an acceptance requirement for both code and documentation. A new developer should be able to start at the root README, run the app using exact commands, locate the relevant business rule and implementation, understand key tradeoffs, and reproduce verification without reading AI chat history. AI instruction files supplement that onboarding path; they do not replace it.

Use conventional C# and TypeScript naming, descriptive domain terms, small cohesive functions/components, explicit types at API boundaries, and straightforward control flow. Enable C# nullable reference checks and retain TypeScript strict mode. Follow the scaffold's formatting, linting, and analyzer conventions; fix introduced warnings rather than broadly suppressing them. Explain nonobvious invariants and reasons in comments, especially inventory and payment behavior; avoid comments that merely repeat the code. Keep errors actionable, secrets out of logs, and tests named after observable business behavior. Do not add abstraction layers or tooling solely to demonstrate a pattern.

Documentation should use plain language, short sections, portable links, and clear separation of implemented behavior, planned work, known limitations, and verified results. Keep one canonical description of each rule and link to it. Document public configuration, request/response examples, database setup/reset, test commands, and important extension points as those features are implemented. Every internal milestone verification includes a readability check as well as functional checks; it does not require a human approval pause.

Use concise root/client/server/docs/ai-sessions READMEs, canonical AGENTS.md, thin CLAUDE.md, three short contracts, one architecture/decision document, and one build log with compact per-milestone timings and evidence. This consolidated log preserves the handoff's required metrics without separate approval reports. Reuse this review as the audit and traceability source instead of generating many duplicate requirement documents or ten separate ADR files. The handoff's directory layouts are suggestions, not a requirement for empty folders.

Every AI session includes this planning chat, Claude implementation sessions, and any relevant earlier project sessions. Prompts and completion reports do not replace full transcripts. Claude /export and project-specific raw session files from ~/.claude/projects/ are both required by the assignment. Preserve originals outside the submission; copy only the applicable project's files into the submission archive. Do not collect unrelated projects. Inspect copies for secrets before sharing; if redaction is necessary, label it and keep an accurate manifest rather than silently describing modified files as untouched raw originals. Never print credentials during inspection. Never fabricate an export, token count, duration, or test result.

AI performs no Git commands during implementation, including init, add, commit, push, merge, rebase, reset, stash, checkout, or history manipulation. The owner initializes the private repository, reviews staged files and archives, commits, pushes, and invites reviewers manually. Ignore dependencies, build outputs, database runtime files, local secret configuration, and actual .env files; retain a nonsecret .env.example if used. Required session archives must not be accidentally ignored.

## 5. Revised two-hour schedule

Approved replacement for the unexecuted TASK-002–008 definitions and schedule. TASK-001 retains its foundation objective. Renumbering is explicit so the old descriptions cannot accidentally be reused. With the owner's planned 9:00 p.m. start, TASK-001 occupies 9:00–9:10 p.m.; the global stop is 11:00 p.m. Eastern. Starting late does not silently move that deadline.

TASK-001–008 are internal milestones within one master implementation, not eight separate prompts or authorizations. The budgets below guide priorities; Claude may progress early when checks pass, repair routine failures within scope, and adjust remaining allocations while preserving the deadline and contingency gates. No pause is required at ordinary milestone boundaries.

| Task | Wall-clock window | Deliverable / exit evidence |
|---|---|---|
| TASK-001 | 0–10 min | Runnable React/TypeScript and .NET 10 shells, health request through proxy, essential docs and boundaries |
| TASK-002 | 10–25 min | SQLite, idempotent seeds, event/slot/availability read APIs, theme GET/PUT; calendar-ready DTOs |
| TASK-003 | 25–50 min | Shared order lifecycle, atomic General Admission capacity, hosted Stripe test Checkout, server verification/reconciliation, focused service tests |
| TASK-004 | 50–72 min | Context-owned calendar, event/slot/type selection, remaining counts, review, checkout and confirmation; basic Fall/Winter switching |
| TASK-005 | 72–85 min | Required-flow gate: actual successful Stripe test purchase, refresh/repeat confirmation, competing-capacity tests, return/expiry error checks, startup/README corrections |
| TASK-006 | 85–105 min | Only after required-flow gate passes: 18-seat SVG, backend seat claims, shared checkout integration and focused seat tests |
| TASK-007 | 105–110 min | Verify persisted themes preserve selections, responsive/keyboard usability; small visual refinement if core checks pass |
| TASK-008 | 110–120 min | Freeze features; final focused verification, limitations, transcript/raw archive manifest and owner submission steps |

All prompting, implementation, reviews, fixes, exports, and manual submission work occurring during the build count against one wall clock. Task times are budgets, not allowances to restart the timer. Start packaging earlier if archives or manual Git work need more than ten minutes. Stop at the two-hour deadline and ship the actual state; do not extend the timer to finish a feature or a test.

At minute 50, a payment/provider blocker displaces downstream enhancement work. At minute 85, if any original requirement is missing, use TASK-006/007 time to finish the original flow and document seating as incomplete. At minute 105, stop adding functionality even if seating is incomplete; disable unfinished purchase paths and describe them accurately. TASK-007 permits verification and small visual refinements only when required checks already pass. At minute 110, freeze all feature work. No simulated Stripe success and no relaxed inventory checks.

The owner-approved master workflow replaces mandatory per-task human reviews. Claude performs internal verification, records concise milestone evidence, and continues automatically when the gate passes. Human acceptance occurs after the build; owner feedback during execution steers the active work without resetting the timer. Preserve session records incrementally and avoid repeatedly rerunning successful checks without a change or unresolved failure.

### One authorization and internal verification gates

- The owner explicitly launches the master prompt once. This authorizes reversible workspace implementation, dependency restores, local execution, appropriate tests, in-scope corrections, and documentation/archives for all remaining milestones until completion or the deadline.
- The foundation must build and its API proxy must work before dependent features accumulate. The data milestone must verify seeded events, slots, and independent availability. Payment/inventory tests must pass before the purchasing flow is described as working. Required General Admission acceptance must be proven before seating starts.
- Report milestone completion, remaining time, important failures, and scope cuts concisely. Do not end the session or wait for routine approval after a milestone. An internal pass is verification evidence, not owner acceptance.
- Resolve routine implementation choices and failures independently within the approved architecture. If a required credential, permission, material requirement, or necessary out-of-scope decision blocks progress, ask one concise question and continue useful independent authorized work. Do not fabricate a workaround or assume approval from silence.
- At the feature freeze, complete a consolidated self-review and final evidence/limitations report. Manual Git operations and reviewer invitations remain the owner's responsibility. Never extend the deadline to wait for an answer or finish a feature.

## 6. Requirement-to-evidence checklist

| Original assignment requirement | Planned owner/task | Required evidence before submission |
|---|---|---|
| Strict two hours | Owner; all tasks | Actual start, deadline, elapsed time, and stop recorded |
| >=5 events on a calendar; >=2 slots each | TASK-002/004/005 | Seed assertions plus interactive browser check |
| Types and remaining counts in selected slot | TASK-002/004/005 | Display matches selected-slot API/database values |
| Buy slot tickets and pay in Stripe sandbox | TASK-003/004/005 | Real successful test checkout and verified order |
| Capacity per slot/type | TASK-003/005 | SQLite-backed tests including competing claims |
| .NET 10 API; React TypeScript; Context app state | TASK-001 onward | Build results plus code review of ownership |
| SQLite single file | TASK-002/008 | Documented path, automatic setup, restart persistence |
| Business-rule tests | TASK-003/005; TASK-006 for seats | Actual command and passing/failing counts, no mock-only inventory proof |
| dotnet run / npm run dev and exact README steps | TASK-001/008 | Startup smoke checks; clean-install procedure documented |
| Private GitHub repository and invitation | Owner | Owner confirms private repository and reviewer access |
| Every AI transcript; Claude export and raw sessions | All tasks; owner export where necessary | Archive manifest and actual files; this chat included |
| README finished / unfinished / next steps | TASK-008 | Honest concise status matching tested behavior |

Focused business-rule tests should cover valid/invalid quantities, independent type and slot capacity, all-or-nothing multi-item claims, server pricing, competing claims, repeated/concurrent confirmation and release, definitive creation failure, uncertain creation retaining holds, paid-without-return reconciliation, and expired/unpaid cleanup. Seating adds live-claim uniqueness, sold/held rejection, cross-performance independence, section pricing, and all-or-nothing multi-seat claims. Use isolated real SQLite files with separate connections for concurrency tests and a controlled fake payment provider for deterministic failures; the manual successful Checkout still uses real Stripe test mode. Do not substitute EF InMemory for inventory concurrency proof.

## 7. Owner decisions and implementation status

The engineering amendments above resolve the identified design ambiguities without adding product features. The owner has now supplied these confirmations:

1. **Plan and workflow approved:** original requirements first, payment earlier, 18-seat initial chart, lifecycle corrections, concise docs, explicit contingency gates, and one master prompt with automatic progression through verified milestones. No application coding authorization was given.
2. **Planned clock:** the owner stated, "It can start in 9mins. 9pm." Record 9:00 p.m. Eastern on November 1, 2026 as the planned start and 11:00 p.m. Eastern as the two-hour stop. The owner controls the start; do not assert that the clock has already started or that the evaluator has independently approved a planning exemption.
3. **Readiness confirmed by owner:** all Stripe, private repository access, and transcript prerequisites reported ready. Credentials were not requested or inspected. SDK/Node presence was independently verified; package/API network access remains an implementation check.

The active entry point is `CLAUDE-MASTER-IMPLEMENTATION-PROMPT.md`. The standalone `TASK-001-claude-code-prompt.md` is retired to avoid a conflicting foundation-only stop instruction. The only remaining authorization gate is the owner's explicit launch of the master implementation. No application code should be written before then, and no task starts automatically when the wall clock reaches 9:00 p.m.
