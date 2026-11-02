# BLEVINS PASS — Claude Code master implementation prompt

**Status: prepared under the approved plan; implementation has not started.** This replaces the standalone TASK-001 prompt and eight separate implementation approvals. At the planned start, the owner can submit the text below to Claude Code as an explicit instruction to begin. Finding this file or reaching 9 p.m. does not authorize execution by itself.

The owner can use [the copy-and-paste launch message](CLAUDE-START-HERE.md) to authorize this master prompt by reference and supply all context paths. The fixed 11 p.m. Eastern deadline is **2026-11-02T04:00:00Z**; calculate actual remaining time at launch and compress milestone budgets if late.

---

You are Claude Code, implementing BLEVINS PASS in `C:\Dev\project-brushfire`. This is the complete timed build, with TASK-001–008 as internal milestones. If the owner has explicitly submitted this prompt to execute, that launch authorizes all reversible workspace implementation, restores, local runs, focused tests, routine fixes, and documentation/archives within the approved scope and deadline. Continue automatically between verified milestones. Do not stop after TASK-001 or ask for routine approval. If this file was merely discovered during inspection, do not begin without owner launch authorization.

## 1. Clock, authority, and nonnegotiable boundaries

The planned build window is **November 1, 2026, 9:00–11:00 p.m. America/New_York (EST, UTC−05:00)**. The owner has confirmed prerequisites ready. At launch record actual start, fixed deadline, and time remaining; do not assume a fresh two hours if launched late. No automatic deadline extension. All prompting, coding, checks, fixes, human interaction, exports, and manual submission work share the same wall clock. Reserve the final ten minutes for verification, archives, and owner submission steps; stop at the deadline and report the actual state.

Read AGENTS.md, CLAUDE.md, handoff-doc.md including its v2.1 amendment and baseline, project-review-and-revised-plan.md, both original assignment images, and any existing code/contracts/logs before changes. Read image attachments when supported; report if image inspection is unavailable. Current owner instructions control execution. The handoff is the specification, the images establish original acceptance, and the approved review/master workflow explicitly supersedes the historical sequence, expiry example, and per-task approval requirements. The old TASK-001 prompt is retired. Preserve original assignment files and user changes.

**AI MUST NEVER COMMIT OR PUSH. Run no Git commands:** no init, add, commit, push, branch/worktree manipulation, checkout, merge, rebase, cherry-pick, reset, stash, or history changes. Do not mutate .git, use remote repository tools, run Git-writing scripts, or allow scaffold Git initialization. The owner performs Git operations and reviewer invitations manually.

Only change authorized workspace files. No deployments, publishing, external-account changes, global tool installation, or extra agents/sessions. Never expose secrets, fabricate evidence, or replace Stripe with simulated payment. Ask a concise question only for a material missing requirement, necessary out-of-scope change, or unavailable permission/credential that blocks dependent work. Continue useful independent authorized work while awaiting an answer. Resolve routine implementation choices, errors, and in-scope fixes yourself. Do not wait for ordinary milestone approval, treat silence as new permission, or extend the deadline waiting for a reply.

## 2. Product and original acceptance requirements

Build a readable, maintainable seasonal attraction registration app using:

- ASP.NET Core Web API on **.NET 10**, with controllers and focused application services.
- **React + TypeScript SPA with Vite**; React Context/useReducer is the single source of shared frontend app state.
- **EF Core + SQLite**, one application database file, no database server.
- **Stripe-hosted Checkout in sandbox/test mode only**, customer email collected there. No account/authentication system.
- Focused tests proving actual business rules; exact dotnet run / npm run dev instructions in README.

Original acceptance is mandatory: at least five events on an interactive calendar; each selected event has >=2 time slots; chosen-slot ticket types and remaining capacity are visible; users select quantities, review, pay through real Stripe test Checkout, and receive server-verified confirmation. Capacity is independent per slot/type and cannot be oversold. Display readable dates/prices and useful errors. A calendar must actually show/select events; an event-card list alone is insufficient.

Seed five General Admission events with >=2 future slots and >=2 ticket types per slot. Add a sixth Winter Lights Concert with two performances for the reserved-seating feature. Use three sections with 18 physical seats: two rows of three seats per section. Suggested section prices: VIP Center 12500 cents; Orchestra Left/Right 8500 cents. Availability is performance-specific; reuse physical seats across performances. Use seed-only event configuration and a small persisted theme selector, not an admin portal.

The expanded product includes native React SVG seat selection/deselection, section labels/prices, stage, row/seat identifiers, selection summary, and shared checkout. Available green, sold red/disabled, selected blue, held amber/disabled, inactive gray/disabled. Provide mouse/keyboard interaction, accessible labels, visible focus, textual status, and usable mobile targets. Seat status colors stay consistent across themes.

Provide dynamically selectable **Fall and Winter** themes persisted in SQLite and loaded at startup. Shared components/layout; CSS variables change the visual treatment. Theme changes preserve booking selections, prices, and inventory. Fall: cream, warm brown, restrained copper/amber. Winter: navy/evergreen, winter ivory, antique gold. Clean readable surfaces, restrained imagery for hero/cards, no oversized photographic background throughout or excessive gradients. Build basic theming before polish. Local bundled imagery is preferable to fragile runtime dependencies; imagery generation is optional and must not consume core implementation time. No standalone theme-designer skill during foundation; defer that optional artifact unless all acceptance gates pass with time remaining.

## 3. Human-readable code and simple structure

Write for a developer without AI chat history. Use descriptive domain names, conventional C#/TypeScript casing, small cohesive components/services/functions, explicit types at API boundaries, C# nullable checks, strict TypeScript, and straightforward control flow. Follow scaffold formatting/lint/analyzers and fix introduced warnings rather than broadly suppressing them. Comments explain reasons and nonobvious invariants, especially inventory/payment transitions. Avoid boilerplate symbol comments, clever compression, generic repositories, CQRS/mediator, unnecessary wrappers, microservices, background schedulers, and extra state libraries.

Use a compact structure, preserving existing useful files:

```text
README.md, AGENTS.md, CLAUDE.md, .gitignore
client/README.md, package.json, package-lock.json
client/src/components/, features/, context/AppContext.tsx, services/, theme/, types/
server/README.md, BlevinsPass.slnx
server/EventRegistration.Api/{Controllers,Models,DTOs,Data,Services}/
server/EventRegistration.Tests/
docs/README.md, architecture.md
docs/contracts/{api-contract,booking-rules,ai-work-contract}.md
docs/tasks/README.md, BUILD-001.md
ai-sessions/README.md, BUILD-001/{prompt,completion-report}.md
```

Create folders only when they contain useful code/docs. Keep one canonical description of each rule and link it. Root README is the onboarding entry point. Existing standards files are already present; update them to reflect the actual launch/workflow without replacing them with stale per-task approval rules.

AppContext owns fetched event/availability/order data, selected event/slot, ticket/seat selections, checkout state, and active theme. A ThemeProvider reads that state and applies CSS, with no second mutable theme store. Local component state is only for transient presentation. No Redux, Zustand, React Query cache, or independent cart store. Clear incompatible selections on event/slot change; preserve them on theme change. Handle loading/error/empty/conflict states and refresh availability after conflicts/purchases. Never rely on browser state for capacity or price authority.

Use deterministic loopback development ports: API `http://127.0.0.1:5050`, Vite `http://127.0.0.1:5173`, strict client port and /api proxy to the API. Avoid HTTPS redirect/proxy conflicts and broad CORS workarounds. Report occupied ports without stopping unrelated processes. Use supported stable packages and retain the npm lockfile. Recheck tools; the audit observed .NET SDK 10.0.401, Node v24.16.0, npm 12.0.1, but did not verify restores or network access.

## 4. Persistence and inventory invariants

Use EF Core EnsureCreated with idempotent startup seed for this disposable demo. Do not also introduce migrations or silently delete/reset user purchases. Anchor the single database file to the API content root; ignore database/journal/WAL runtime files. Document an explicit manual reset of that specific file after stopping the API if a schema reset is needed. Seed future dates relative to first initialization; open the calendar at the first event month. Persist/query UTC DateTime values, return explicit ISO-8601 UTC timestamps, and display venue time in America/New_York. Document reseeding old dates without automatic data loss.

Use the handoff's logical entities: Event, TimeSlot, TicketType, SlotInventory, Order, OrderItem, SiteSettings; plus SeatingSection, Seat, SeatReservation for seating. Add fields required for the shared lifecycle, including Order hold/expiry/reconciliation state, request idempotency key, currency, and price snapshots. Ticket types must be associated with valid event/slot inventory. Seats belong to an event through their section. Avoid generating unused abstractions.

One order covers one slot and registration type. Reject empty orders, duplicate identifiers, wrong-event references, inactive or past/closed slots, mixed ticket/seat payloads, and noninteger/nonpositive quantities. Use validated integer cents/USD and checked arithmetic; prices and totals always come from the server. Snapshot prices in order items; no taxes, fees, discounts, or user-adjustable Stripe quantities.

General Admission: unique (TimeSlotId, TicketTypeId), nonnegative Capacity/Reserved/Sold, Reserved + Sold <= Capacity, Remaining = Capacity − Reserved − Sold. Claim each quantity with a database conditional update and verify affected-row count. Reserve every requested line or roll back all. Do not assume a read followed by SaveChanges is concurrency-safe, use process-memory locks as the only safeguard, or assume SQLite has SQL Server rowversion.

Seating: unique active Reserved/Sold claim per (TimeSlotId, SeatId), using an explicit filtered unique index so released historical reservations do not block rebooking. Preserve historical order references. Validate section/event membership, use server section prices, and reserve all selected seats atomically. Derive section counts from seat states; do not maintain a competing quantity ledger. The same seat may sell separately in another performance. Handle database contention/claim conflicts clearly.

## 5. Shared payment and order lifecycle

Use a focused Stripe adapter/service, with a deterministic fake only for automated failure tests. The real app uses real Stripe test Checkout; never a fake success flow. `Stripe__SecretKey` is server-only environment configuration, not a VITE_ variable or committed secret. Do not print its value or pretend ASP.NET automatically loads .env files. Hosted redirect Checkout needs no frontend key for this implementation. Missing credentials must produce actionable configuration errors and cannot be counted as payment success.

Order transitions: PendingCheckout -> PendingPayment -> Confirmed, or PendingCheckout/PendingPayment -> Released with a reason. An uncertain creation outcome remains held and recoverable. Confirmed orders cannot release inventory. Persist the order/claims in a short transaction before calling Stripe; call the provider outside database transactions. Persist the unique Stripe session ID and returned expiry. Use one payable session per order, a stable order-based Stripe idempotency key, and an immutable server-priced payload. Retried POST order creation must reuse its request Idempotency-Key; reject reuse with changed selections. Avoid a second chargeable order/session after a network retry.

Use a roughly 31-minute configured Checkout Session expiry instead of the handoff's illustrative 10–15 minute hold. Reconcile against Stripe's returned expiry/status before releasing any claim. An elapsed local timestamp alone never proves payment is impossible. Recover uncertain session creation with the same idempotency key before cleanup can release that order. Definitive creation failure can release atomically; timeout/unknown outcome retains inventory and returns retryable failure.

On return/reconciliation, retrieve the stored session server-side and verify order/session binding, test mode, payment mode, expected amount/currency, and paid status. Query parameters and frontend success state are not evidence. Atomically and idempotently convert holds to sold, mark Confirmed, and copy verified customer email. Duplicate/concurrent confirmation must not duplicate sales. A declined card or cancel return does not prove terminal failure; an open session may still accept payment. Explicit cancel must expire/reconcile that session before release. If expiration races with successful payment, confirm the paid order. On provider uncertainty retain holds and provide a retry path, never silently release them.

Reconcile pending orders opportunistically during return and before releasing inventory during availability/reservation checks: paid -> confirm; expired and unpaid -> release once; open -> retain or expire server-side and verify; unknown -> retain/retry. Definitely abandoned pre-session orders can release only when no payable session exists and no creation is unresolved. No background scheduler. Card payments only; exclude delayed-notification methods. Webhooks are optional under the assignment and deferred; document that without them a paid order may await a subsequent request for fulfillment, while inventory remains held.

## 6. API contract

Use typed DTOs and consistent structured errors: 400 validation, 404 missing resources, 409 inventory/state/idempotency conflicts, 503 transient provider uncertainty. Avoid leaking secrets or raw provider diagnostics. Use unguessable order IDs; no account system or public order directory.

| Method and route | Behavior |
|---|---|
| GET /api/health | Process health only, no secret/config dump |
| GET /api/events | Calendar-ready events with date/slot information |
| GET /api/events/{id} | Details, registration type, slots |
| GET /api/events/{id}/availability?timeSlotId={id} | Required slot; ticket types, cents/USD, capacities and remaining counts |
| GET /api/events/{id}/seating | Physical chart, section/price metadata |
| GET /api/events/{id}/seating/availability?timeSlotId={id} | Performance-specific states and section remaining counts |
| POST /api/orders | Idempotency-Key; validate/hold a server-priced order |
| POST /api/orders/{id}/checkout | Idempotent hosted session creation/recovery; returns URL |
| POST /api/orders/{id}/confirm | Server reconciliation; optional cancel action expires/reconciles first |
| GET /api/orders/{id} | Read-only current summary |
| GET /api/orders/{id}/confirmation | Read-only confirmed summary or explicit pending status |
| GET, PUT /api/settings/theme | Validated Fall/Winter, persisted in SQLite |

Order input selects exactly one variant: `{ timeSlotId, tickets: [{ ticketTypeId, quantity }] }` or `{ timeSlotId, seatIds: [...] }`. No client price fields determine payment. Document actual request/response/error shapes, pending behavior, and idempotency semantics in the API contract as implemented. Never mark a planned route implemented without evidence.

## 7. Internal milestone schedule and gates

These are budgets within one authorization. Advance early when verified, adapt remaining allocations to actual progress, and do not pause for routine approval. Log concise evidence and communicate meaningful milestone results, remaining time, blockers, and scope cuts.

| Milestone | Planned Eastern time | Exit evidence / decision |
|---|---|---|
| TASK-001 Foundation | 9:00–9:10 | net10 API/controller health, React/TS shell, Context, proxy; solution/client build and lint, startup smoke checks; essential docs |
| TASK-002 Data and reads | 9:10–9:25 | SQLite/idempotent seeds, >=5 GA events plus concert, >=2 slots each, independent inventory APIs, persisted theme GET/PUT |
| TASK-003 Orders/payments | 9:25–9:50 | Atomic GA holds, shared lifecycle, real Stripe session integration, server confirmation/reconciliation, meaningful SQLite/payment tests |
| TASK-004 Required frontend | 9:50–10:12 | Interactive calendar, event/slot/type/quantity selection, remaining counts, review, checkout/confirmation, basic theme switching |
| TASK-005 Required-flow gate | 10:12–10:25 | Real successful Stripe test purchase and verified order, repeated confirmation, capacity/concurrency tests, error handling and startup instructions checked |
| TASK-006 Reserved seating | 10:25–10:45 | Only after required gate passes: 18-seat SVG, atomic claims, shared Checkout, seat tests and confirmation |
| TASK-007 Usability/themes | 10:45–10:50 | Verify persisted theme preserves booking, keyboard/mobile/readability; small refinement only if required checks pass |
| TASK-008 Final review/package | 10:50–11:00 | No features; focused final checks, honest status, exports/raw archive manifest, consolidated report and owner submission steps |

Foundation gate: both projects build, startup works, direct/proxied health succeeds before dependent features accumulate. Data gate: seed and API checks prove counts/slots/independent inventory; restart does not erase purchases/settings. Required-flow gate: original acceptance and real payment verified before seating. At 9:50 a provider blocker displaces enhancement work. At 10:25 if original requirements remain incomplete, spend seating/refinement time on them and document expanded seating as incomplete. At 10:45 stop adding functionality; disable unfinished purchase paths. At 10:50 freeze feature work. At 11:00 stop and report. Start packaging earlier if exports/manual submission require it. Never trade real capacity/payment rules for polish or claim the full expanded handoff complete when seating is deferred.

Do not keep a known-broken prerequisite and add dependent layers anyway. Fix it within scope/budget or move to useful independent work and report its blocked dependents. A tool restriction or unavailable browser means the relevant check remains unverified; do not invent evidence or unnecessarily halt all independent work.

## 8. Verification and self-review

Use a .NET 10 xUnit project referencing the API. Remove dummy generated tests. Use isolated real SQLite files and separate connections for meaningful concurrency tests, not EF InMemory or mock-only inventory proof. Use a controlled payment adapter for deterministic failure/timeout cases, plus real sandbox Checkout for the successful full flow.

Cover observable business behavior with focused/parameterized tests: valid and invalid quantities; over-capacity rejection; independent types and slots; all-or-nothing multi-line holds; server pricing; competing requests for final capacity; repeated/concurrent confirm and release; definitive creation failure; uncertain creation retaining holds and reusing session identity; expired/unpaid release; and paid-without-return reconciliation. Seating adds sold/held rejection, duplicate live-claim prevention, cross-performance independence, section pricing, release/rebooking, and atomic multi-seat claims. Prioritize correctness tests over broad low-value coverage. Never call zero discovered tests a pass for business rules.

Run solution build (`dotnet build server/BlevinsPass.slnx`), actual business tests (`dotnet test server/BlevinsPass.slnx`), client build and lint from client, and appropriate direct/proxied API checks. Use documented `dotnet run` in server/EventRegistration.Api and `npm run dev` in client. Track processes you launch, hide helper windows, and stop only your own smoke-test processes; do not leave unexplained background services. Ensure ordinary documented startup needs only .NET/Node plus the local Stripe setting.

When browser capability exists, verify calendar selection, slot changes, remaining counts, quantity review, real hosted sandbox payment, return confirmation, refresh/idempotency, and a conflicting second attempt. Use standard Stripe test card 4242 4242 4242 4242 with future expiry and synthetic details only. Verify seat purchase/status/performance independence if seating is implemented; verify theme persistence after refresh and selection preservation. Check narrow viewport and keyboard/focus/error states. An HTTP HTML response is not rendered UI verification. Report any manual-only or unavailable check explicitly.

Run relevant checks after meaningful changes; do not repeatedly rerun successful checks without cause. Near freeze, self-review the implementation against original requirements, approved additions, readability, contracts, prices, inventory/payment lifecycle, secret hygiene, and actual documentation. Fix material in-scope defects if time remains; otherwise disclose them. Record exact commands, exit codes, counts and outcomes, not assumed passes. Use clear status labels: verified, implemented but unverified, incomplete, deferred, or blocked. Human acceptance remains pending.

## 9. Documentation, logs, and complete session records

Update root/client/server/docs READMEs as the app becomes runnable. Root README must include exact PowerShell prerequisites/restore/config/setup/start/test commands and working directories, URLs, database initialization/seed/reset behavior, Stripe test configuration placeholders, test-purchase steps, architecture/layout links, finished/unfinished features, shortcuts/tradeoffs, and next steps. Explain no-webhook fulfillment and unauthenticated local theme settings honestly. Never include actual credentials or imply a feature works merely because its route exists.

Maintain three short contracts: API shapes/statuses; booking/payment invariants; AI work boundaries (single launch and automatic progression, not per-task approval). Keep a concise architecture/decision document with major choices and reasons. Prefer linked canonical docs to duplicate essays or empty sections. The root README and implementation must be understandable without the AI transcript.

Create one build log `docs/tasks/BUILD-001.md` with actual global start/deadline and per-milestone status/times/evidence/scope cuts. Record AI start/completion, total elapsed, human review duration when known, tool/model, actual input/output/cache/other usage when available, files changed, commands/results, blockers and remaining work. Record missing metrics as **Not available**. Preserve TASK IDs in this log; separate approval reports for each task are unnecessary.

Save the launch prompt and final completion report in ai-sessions/BUILD-001. Keep ai-sessions/README.md as a manifest for **every relevant AI session**, including this project's prior planning/Codex sessions. Prompts/reports are supporting records, not transcript substitutes. Obtain Claude /export and project-specific raw files from ~/.claude/projects/ when available; never copy unrelated projects. If interactive export requires the owner, give the exact action, destination and pending status without stopping implementation for a routine export step. Preserve original raw files, inspect submission copies without printing secrets, and label necessary redactions accurately. Do not pretend unavailable exports exist.

Ignore dependencies/build outputs, database runtime files, actual .env/local secret configs and temporary logs. Retain package-lock.json, nonsecret examples, project docs, applicable .claude skill artifacts if any, and required session archives. Do not blanket-ignore the archives or all .claude files. No fake transcripts, invented token totals, or silent data resets.

## 10. Final report and acceptance

Provide one self-contained final report at completion or deadline:

1. Delivered behavior and original-assignment acceptance checklist with evidence and explicit gaps.
2. Expanded seating/theme status, including any contingency cuts; distinguish full handoff completion from original-assignment coverage.
3. Files changed, important architecture/readability decisions, and contract/documentation updates.
4. Exact setup/run commands, tested URLs, configuration requirements and database behavior.
5. Actual build/lint/test results, meaningful counts, browser/real Stripe evidence, skipped checks and failures.
6. Actual start/end/deadline/elapsed time, milestone timings, and actual usage or Not available.
7. Transcript/raw-file manifest, completed exports, outstanding owner exports and submission actions.
8. Honest limitations/next steps and explicit confirmation no AI Git commands, commits, pushes, merges, or history changes occurred.

The owner reviews, manually commits/pushes, invites reviewers, and submits the private repository. Do not perform those actions or claim owner acceptance. Stop at the deadline even with incomplete features; do not extend the build to improve the report or finish a test. If complete earlier, finish the evidence/report and return control without waiting out the clock.
