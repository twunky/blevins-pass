# Phase 2 — review 001 correction report (Claude Code)

Same local session as the initial work: session ID `77bd3fc9-4329-4518-bc22-3caa875e473e`, Claude Opus 5.5, `C:\Dev\project-brushfire`. The initial [completion report](claude-completion-report.md) is preserved unchanged. Inputs: [correction proposal](../../docs/phase-2/CORRECTION-PROPOSAL.md), [review-001 prompt](claude-review-001-prompt.md), [capacity audit](../../docs/phase-2/CAPACITY-CONCURRENCY-AUDIT.md), [Codex findings](codex-review-001.md). Audience: owner, for relay to Codex.

## Clock (America/New_York, from shell `date`)

| Point | Time |
|---|---|
| Correction instruction received / first clock read | 00:33:44 |
| Four server fixes + regressions passing (105/105) | 00:37:36 |
| Codex probes re-run, client changes, browser retry check passed | 00:39:35 |
| Final server build/test | 00:41:18 |
| This report | 00:41:51 (8 min 7 s after receipt) — **within the 20-minute target**; before the 01:22:33 freeze and 01:32:33 hard stop (not restarted or extended) |

## Corrections (in the requested order)

1. **FIFO eligibility atomic with the public hold.** The separate `CountWaiting` pre-check in `OrderService` is gone. Public GA holds now call `InventoryService.HoldTicketsAsync(..., requireEmptyWaitlist: true)`, which adds `NOT EXISTS (Waiting entry for this slot)` to the **same guarded UPDATE** that checks the type cap and shared limit and increments `Reserved`. A waitlist entry committed before that statement always blocks the purchase (`409 waitlist_active`, nothing reserved, whole order rolled back). Waitlist promotion still uses the hold without that predicate. No process lock.
2. **Monotonic capacity writes.** The shared UPDATE now also requires `SharedCapacity IS NULL OR SharedCapacity <= requested`, and the per-type UPDATE requires `Capacity <= requested`, alongside the existing commitment guards. A stale request gets `409 capacity_changed` ("already raised to N; nothing saved"), and any part of a combined request already written rolls back with it.
3. **Resume checkout for accepted offers.** `WaitlistStatusDto.CanResumeCheckout` is true only for an `Accepted` entry whose order is `PendingCheckout` or `PendingPayment`. The private page shows **Resume checkout** (also after a reload). It calls the existing `POST /orders/{id}/checkout` for the **same order**, which already reuses the frozen payload and the stable Stripe idempotency key. Unknown outcomes keep the hold. Terminal orders get no resume, and the server refuses them (`order_released`, `order_already_confirmed`).
4. **Status refresh advances the queue.** `GetStatusAsync` now runs `PromoteAsync` (expire lapsed offers, then offer freed places in FIFO order) instead of only expiring. It never releases accepted orders: their release still follows payment reconciliation. Promotion does not call status reads, so there is no recursion. Each offer is still its own short transaction.

Preserved: shared 25-person limit, per-type caps, one person per ticket, strict head-of-line, the notification gate, idempotency keys, payment-safety rules and existing data. No new dependencies, features, agents, sessions, resets, refunds or Git operations.

## Changed files (this pass)

- Server: `Services/InventoryService.cs`, `Services/OrderService.cs`, `Services/WaitlistService.cs`, `DTOs/WaitlistDtos.cs`.
- Tests: `ReviewCorrectionTests.cs` (new, 8 cases), `WaitlistTests.cs`. The demo-reset outbox test now asserts the DB state right after the reset, then that a status refresh offers the next customer (intended change from fix 4).
- Client: `features/WaitlistStatusView.tsx`, `types/api.ts`.
- Docs: `docs/contracts/booking-rules.md`, `docs/contracts/api-contract.md`, `README.md` (Phase 2 status lines and waitlist bullets), `docs/phase-2/WRITE-UP.md` (459 words), `docs/phase-2/verify-checkout-resume.mjs` (new).
- Evidence: `evidence/review-probes-after-correction.txt`, `evidence/ui-retry/results.json` and `resume-available-390.png` (new).
- Not edited: Codex-owned `docs/phase-2/README.md` and `ai-sessions/PHASE-2/README.md`; Codex review probes and their original evidence.

## Commands and results

| Command | Result |
|---|---|
| `dotnet build server/BlevinsPass.slnx` | succeeded, 0 warnings, 0 errors |
| `dotnet test server/BlevinsPass.slnx` | **105 passed, 0 failed, 0 skipped** (97 before + 8 new). One intermediate run had 1 failure: the outbox/demo-reset test, whose expectation changed with fix 4. It was updated and re-run. |
| `dotnet run --project docs/phase-2/review-probes/ReviewProbes.csproj` (Codex probes, unchanged) | `PUBLIC_FIFO_RACE: refused with waitlist_active`; `CAPACITY_INCREASE_RACE: stale increase refused with capacity_changed`, final capacity `40`; `STATUS_EXPIRY: OfferPendingContact`, `10` held |
| `npx eslint .` (client) | exit 0 |
| `npm run build` (client) | succeeded |
| `node docs/phase-2/verify-checkout-resume.mjs` (headless Chrome, 390px, Asia/Tokyo zone) | passed: first checkout = **real server 503** (API started with a deliberately non-test Stripe key, so it refuses before any network call); reload shows "Checkout started" + **Resume checkout**, no Buy; resume requested the **same order id**; afterwards one order, 22 shared places left (3 held); 0 page errors |

New regression cases: the exact paused public-buyer race (refused, 24 held, 1 order); public buyer before a later join keeps the place; stale shared 30 vs committed 40 (stays 40); stale combined request rolled back entirely; resume after `UncertainNothingCreated` and after `UncertainButCreated` (same order, one session, inventory unchanged, Purchased afterwards with no resume); released order cannot resume; Bob's own refresh after Alice's offer expires (OfferPendingContact, 10 child reserved, adults 0).

## Coverage limits

- The browser check's second (successful) checkout response came from Playwright, not the server. It proves the UI resumes the **same order**. Same-session reuse at the provider is proven by the server tests with the fake gateway. No real Stripe call was made in this pass.
- Race tests are deterministic interleavings on separate SQLite connections, not load tests. Codex's capacity audit remains separate evidence.
- The owner's `blevinspass.db` was untouched (still Nov 2 16:47, 131072 bytes). All runs used scratchpad fixture DBs. Ports 5050/5173 were freed afterwards (only processes I started were stopped).

## Unresolved / owner steps

- **Codex re-verification** of these corrections is pending.
- **Owner manual acceptance and a real Stripe test purchase** through a waitlist offer remain unverified.
- **Transcript exports outstanding:** run `/export` in this same session and save it as `ai-sessions\PHASE-2\claude-session.txt`. After the session ends, copy the final `%USERPROFILE%\.claude\projects\c--Dev-project-brushfire\77bd3fc9-4329-4518-bc22-3caa875e473e.jsonl` into `ai-sessions\PHASE-2\raw\`. The existing raw snapshot ends at 00:04 and does not include this correction pass. The Codex chat export is also still outstanding.
- PR, optional Copilot review and submission are owner actions.

Suggested Codex-owned updates: `docs/phase-2/README.md` should mark review-001 corrections applied (00:33–00:42) pending Codex verification and link this report. `ai-sessions/PHASE-2/README.md` should list this report and note that the final `/export` and raw JSONL are still required.

Usage: **Not available**. **No AI Git operations** (none run, directly or indirectly). No subagents or extra sessions.
