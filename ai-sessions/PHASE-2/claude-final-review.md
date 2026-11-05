# Phase 2 — final review (Claude Code, review only)

Reviewer: the same local Claude Code session that implemented Phase 2 (session `77bd3fc9-4329-4518-bc22-3caa875e473e`, Claude Opus 5.5). Because I wrote this code, this is a self-review; Codex's independent verification remains the independent evidence. Audience: owner, for relay to Codex.

**Clock (shell `date`):** review start 00:48:20 EST (05:48:20Z), November 5, 2026. Report saved 00:49:53 EST (1 min 33 s). Deadline 01:32:33 EST (06:32:33Z), not restarted or extended: **within deadline and within the 10-minute target.**

## Verdict

**No blocking findings. Ready for owner acceptance** (manual notification walkthrough and real Stripe test purchase), subject to the non-blocking findings below. This is not a production-readiness claim and not owner acceptance.

## Findings (most severe first; none blocking)

### F1 — Medium: anyone can reserve a non-sold-out departure for up to 24 h through the waitlist API, and the operator cannot cancel an offer
- **Where:** `server/EventRegistration.Api/Services/WaitlistService.cs:39-98` (`JoinAsync` checks only that the group could *ever* fit, line 70, then promotes immediately, line 98). Offer lifetime is set at lines 265 and 374. `server/EventRegistration.Api/Controllers/OperatorController.cs:29-63` has outbox, notified, departures and capacity routes but no operator cancel.
- **Scenario:** a hayride departure has 25 free places. A direct `POST /api/waitlist` (the UI shows the form only when sold out, but the API does not enforce that) with 20 Adult + 5 Child and any syntactically valid email becomes `Offered` at once and reserves all 25 places. No payment is taken, and nobody can buy for 24 h unless the requester cancels. Further joins sit `Waiting` and keep public sales closed. Capacity is never exceeded, so this is inventory squatting, not overselling. Phase 1 already allowed unpaid holds, but those were bounded to about 15 to 31 minutes.
- **Smallest fix (not implemented; needs owner authorization):** in `JoinAsync`, refuse a join with `409 tickets_available` when nobody is `Waiting` for the departure and the requested group fits now. Optionally add a Development-only operator "cancel offer" route that reuses the existing `CloseEntryAsync`. Alternatively, accept and document as a demo limitation.

### F2 — Low: a customer's status refresh does not reconcile an abandoned accepted order
- **Where:** `WaitlistService.cs:108` (`GetStatusAsync` → `PromoteAsync`). WaitlistService has no call to `PaymentService.ReconcileStaleOrdersAsync`.
- **Scenario:** Bob accepts, never starts checkout, and the 15-minute pre-checkout window passes. Carol refreshes only her own status page and stays `Waiting`. The places are freed, and Carol offered, on the next availability, order or outbox request for that departure (covered by `WaitlistTests.Cancelling_an_offer_or_letting_the_accepted_order_lapse_passes_places_on`). Expired *offers* do advance on refresh, as reviewed. This is safe (no early release) but slower.
- **Smallest fix:** have `WaitlistController.Get` call `payments.ReconcileStaleOrdersAsync(slotId)` before `GetStatusAsync`, as the availability endpoint does. Or document it.

### F3 — Low (Development only): demo-reset-cancelled offers show "You left the waitlist"
- **Where:** `client/src/features/WaitlistStatusView.tsx:14` (and the `Cancelled` explanation). The reset sets `Cancelled` with reason `DemoReset` (`DemoResetService`).
- **Scenario:** after a developer demo reset, a customer whose offer was closed sees wording implying they left. Only reachable through the Development reset.
- **Smallest fix:** neutral wording, e.g. "This waitlist entry is closed".

### F4 — Info: documentation details
- `docs/phase-2/README.md` (Codex-owned), "Records and remaining actions": still lists "504-word draft write-up" and "Pending: same-session corrections and re-verification". The top status line is current, and the file says earlier entries are historical. Suggest Codex update or label it.
- `README.md:126` (Phase 1 section): "The owner confirmed all submission checklist actions completed" refers to Phase 1. The Phase 2 header lines (3, 5) correctly say Phase 2 owner and Stripe acceptance is pending. Readable as is, but a "(Phase 1)" label would remove any doubt.
- `client/src/features/TicketSelector.tsx:18,59`: the join form appears only when every type shows 0 or someone is waiting. With 1 place left, a group of 3 cannot join from the UI. This is consistent with the "sold-out" requirement, and noted only for completeness.

## Requirement coverage

| Requirement | Assessment | Basis |
|---|---|---|
| Shared 25 across all hayride types, one person per ticket, type caps | Met | Single guarded UPDATE `InventoryService.cs:25-44` (type cap + shared remainder from live Reserved+Sold); tests `SharedCapacityTests` (mixed stop at 25, type caps, later-line rollback, 12-way concurrent mixed → exactly 25, independent departures, legacy overfull) |
| Atomic, all-or-nothing, duplicates, confirm/release idempotency, uncertain payments | Met | Caller transactions; idempotency-key unique index; guarded status flips in `PaymentService`; tests in Payment/Lifecycle/SharedCapacity/Waitlist suites; Codex audit (11 scenarios, pre-correction; the corrections only add restrictive predicates) |
| FIFO eligibility atomic with public reservation | Met | `NOT EXISTS Waiting` in the same UPDATE (`InventoryService.cs:43-44`), used by public orders (`OrderService.cs:97`); exact interleaving regression; Codex probe re-run refused |
| Offers reserve capacity; no payment on join; notification gate | Met (see F1 for abuse limit) | Promotion hold + flip in one transaction; `CreateCalls == 0` test; accept requires `Notified` (`WaitlistService.cs:186`) |
| Accept/checkout retries reuse order/session | Met | Accept idempotent (`waitlist-{ref}` key + guarded flip); `CanResumeCheckout` (`:133`); fake-provider regressions for uncertain/lost-response; two controlled browser checks (worker: real server 503 + mocked second response; Codex: mocked API) |
| Expiry/cancel no double release; refresh advances queue | Met (accepted-order lapse: F2) | Guarded `CloseEntryAsync`; concurrent-expiry test; Bob-refresh regression; probe re-run |
| Concurrent capacity changes cannot lower a limit | Met | Write-time predicates `WaitlistService.cs:464,479`; stale shared and combined-rollback regressions; probe → 40 |
| UTC persistence/API, venue display, DST/date boundaries | Met; no defect reproduced | `VenueTimeTests`; client check under 7 zones (+14 to −11); headless Chrome in LA/Tokyo/Pago Pago showed EST |
| Existing GA/seating, pricing, themes, payment/reset safeguards | Met | Pre-existing suites still pass inside the 105; seating path excludes the waitlist predicate (`OrderService.cs:61,91`) |
| Upgrade preserves data, fails closed on overfull | Met | `SchemaUpgradeTests` (Phase 1-shaped DB: orders, Stripe refs, theme kept; backup; idempotent; schema equals fresh; overfull reported, orders unchanged); HTTP run on a *copy* of the owner's DB |
| README/contracts/write-up match behavior; evidence kinds distinguished | Met (minor F4) | README lines 3 and 5 separate worker, Codex and pending owner evidence; contracts include the corrections; write-up 459 words |

## Meaningful evidence limits

- All payment outcomes in tests and checks use the fake gateway or mocked responses. **No real Stripe call was made in Phase 2.** The only real-provider outcomes in the README are Phase 1, owner-reported.
- Concurrency evidence is deterministic interleavings and bounded bursts on real SQLite (plus one Codex multiprocess case), not production load or multi-instance certification.
- Codex's 11-scenario audit ran before the corrections and was not re-run. The corrections only add refusal conditions, and the relevant suite and probes passed afterwards.
- Browser checks are headless Chrome scripts, not manual, cross-browser or accessibility certification.
- The owner's live `blevinspass.db` has never been opened or upgraded by the app. Only a copy was.

## Checks actually run in this review

Inspection only: read the required documents and the Codex verification, and grepped exact lines in `InventoryService`, `OrderService`, `WaitlistService`, `CatalogService`, `OperatorController`, `TicketSelector`, `WaitlistStatusView` and `README.md`; word-counted the write-up (459). I repeated no builds, tests or probes. Codex's independent 00:42–00:44 run (105/105, lint/build, probes, browser recovery) covers the current code, and none of my findings required execution to confirm. I did not view the requirement images again in this review; they were read at the start of Phase 2 and the requirements are restated in `docs/phase-2/README.md`.

## Remaining owner actions

1. **Decide on F1** (fix, or accept as a documented demo limitation). Optionally F2 and F3. I will not implement anything without authorization.
2. **Manual acceptance:** back up `server\EventRegistration.Api\blevinspass.db`, run the API (the upgrade writes its own `.pre-phase2-*.bak`), sell out or raise capacity on a hayride departure, join the waitlist, use `/?operator` to Mark notified, buy with Stripe test card 4242 4242 4242 4242, and confirm "Purchased". Record the result yourself.
3. **Claude transcript:** in this same session run `/export` and save it as `C:\Dev\project-brushfire\ai-sessions\PHASE-2\claude-session.txt`. After ending the session, copy `%USERPROFILE%\.claude\projects\c--Dev-project-brushfire\77bd3fc9-4329-4518-bc22-3caa875e473e.jsonl` into `ai-sessions\PHASE-2\raw\`. The existing `raw\...snapshot-20261105-0004ET.jsonl` stops at 00:04 and excludes the correction and review turns. No export is present yet.
4. **Codex chat export:** preserve it with its own export mechanism (not yet recorded as present).
5. **PR, optional Copilot review and submission:** owner only.

## Confirmations

- No application code, tests, configuration, dependencies or existing documentation were changed by this review; only this file was created.
- No Git commands or indirect Git operations, no agents or sessions, deployments, Stripe calls, resets, refunds, and no access to the owner's live database.
- Usage: **Not available**.
