# Phase 2 — final narrow correction report (F1 + documentation cleanup)

Same local Claude Code session `77bd3fc9-4329-4518-bc22-3caa875e473e` (Claude Opus 5.5). Earlier reports are preserved unchanged: [initial](claude-completion-report.md), [review 001](claude-review-001-report.md), [final review](claude-final-review.md). Audience: owner, for relay to Codex.

## Clock (America/New_York, shell `date`)

| Point | Time |
|---|---|
| Instruction received / first clock read | 00:55:01 |
| Focused regressions passed (7/7) | 00:58:28 |
| Full server suite passed (110/110) | 00:59:03 |
| Documentation updated | 01:00:53 |
| This report | 01:01:32 (6 min 31 s after receipt) — **within the 10-minute target**, before the 01:22:33 application freeze and the 01:32:33 hard stop (neither restarted nor extended) |

## 1. F1 fix (only)

`server/EventRegistration.Api/Services/WaitlistService.cs`, `JoinAsync` plus new private `CouldBuyWholeGroupNowAsync`:
- A **new** join runs in one transaction: insert the entry, which takes SQLite's write lock until commit or rollback, then check eligibility on the same connection. If nobody else is `Waiting` for the departure **and** the whole group fits the current per-type remaining capacity and shared remaining capacity (re-read inside the transaction), it rolls back and returns **`409 tickets_available`**: "These tickets are available to buy now, so there is no need to join the waitlist. Please book them directly."
- Because the check runs after the insert while the write lock is held, a concurrent purchase, release or capacity change commits either before the check (and is seen) or after the commit. There is no read-then-write gap.
- A refusal leaves no entry, hold, order or Stripe session, and makes no provider call. Joining never calls the provider.
- **Idempotent retries:** the existing-key lookup still runs first, so a matching retry returns its original entry even if places have opened since. The concurrent same-key insert race still returns the winner.
- Unchanged: sold-out joins, joining behind existing waiters (FIFO), promotion, the notification gate, offer duration, payment safety. No operator cancel was added. F2 and F3 behavior is unchanged.

Tests:
- New `server/EventRegistration.Tests/WaitlistEligibilityTests.cs` (5 cases):
  - buyable exact, mixed and single groups refused (no entry, hold stays 20, 1 order, 0 provider calls);
  - a non-fitting group joins at the head, and a fitting single joins behind it;
  - a retry after places open returns the original entry, while a new buyable request is refused;
  - concurrent: a join paused at transaction start while the last place is bought is accepted as `Waiting`;
  - concurrent: a join paused while the filler order is released is refused `tickets_available` with nothing created.
- Adapted fixture: `SchemaUpgradeTests.cs` end-to-end join now requests 2 Family Wagons (only 1 of 3 free), so it is a legitimate join. Its assertion `joined.Created` is kept, and `Status == "Waiting"` is added. No assertion was weakened.

## 2. Documentation cleanup

- `docs/contracts/booking-rules.md`: new F1 bullet (rule, transaction boundary, no side effects, retries). The F2 limitation is stated: status refresh and outbox only expire and promote offers; they don't reconcile abandoned accepted orders. Availability and new-order requests do; explicit order verification follows the payment rules.
- `docs/contracts/api-contract.md`: `POST /waitlist` documents `409 tickets_available` and retry behavior; code list updated; the GET status note covers F2.
- `README.md`:
  - Phase 1 statements labelled as history: the "Final status" banner, the "Verified" paragraph, "Provider evidence" (with an explicit note that Phase 2 is still pending), and "Submission entry points".
  - Phase 2 bullets added for F1 and the F2 limitation.
  - The review status line points to this report and marks Codex verification of F1 as pending.
- `docs/phase-2/README.md` (Codex-authorized): the remaining-actions paragraph now reflects the completed and verified corrections, the final review, this F1 correction (Codex verification pending), the current write-up instead of the 504-word draft, F2/F3 as documented limitations, and the pending owner/export/submission steps.
- `ai-sessions/PHASE-2/README.md` (Codex-authorized): current-verification line and table rows for the review-001 report and verification, the final review, and this report.
- `docs/phase-2/WRITE-UP.md`: one F1 clause and an F2 limit sentence. **495 words** (`wc -w`, including markdown markers).

## 3. Commands and results

| Command | Result |
|---|---|
| `dotnet build` / `dotnet test server/BlevinsPass.slnx` (normal output) | **Build failed to copy** `EventRegistration.Api.exe`: locked by a running `EventRegistration.Api.exe` (PID 8988, started 00:46:02, listening on 127.0.0.1:5050; not started by this session, so it was left running). That run's "105 passed" used stale binaries and **is not counted**. |
| `dotnet test server/BlevinsPass.slnx --artifacts-path <scratchpad>/artifacts` (first) | 104 passed, 1 failed: the `SchemaUpgradeTests` fixture relied on joining while tickets were available, so it was adapted as above |
| same, `--filter WaitlistEligibilityTests\|SchemaUpgradeTests` | **7 passed**, 0 failed, 0 skipped |
| `dotnet build server/BlevinsPass.slnx --artifacts-path …` | succeeded, 0 warnings, 0 errors |
| `dotnet test server/BlevinsPass.slnx --artifacts-path … --no-build` | **110 passed, 0 failed, 0 skipped** |
| `npx eslint .` (client) | exit 0 |
| `npm run build` (client) | succeeded (no client code changed) |

All runs used temporary SQLite databases and the fake gateway. No real Stripe call, reset or refund, and the owner's live database was never opened.

## Remaining limitations

- **Running API uses pre-F1 code.** The process on port 5050 (PID 8988) was started at 00:46:02 from the normal `bin` folder, before this change. To exercise F1 in manual acceptance, stop it and start again with `dotnet run` (that also rebuilds normal `bin`). I did not touch it.
- Join eligibility does not first reconcile stale orders (WaitlistService has no PaymentService dependency). A group that would fit only after an abandoned hold is released can still join as `Waiting`; it is then offered on the next availability or order request. Nothing is oversold.
- F2 (status refresh and outbox don't reconcile abandoned accepted orders) and F3 (demo-reset wording) are documented and unchanged, by owner decision.
- Codex has not yet verified this F1 change. Earlier independent evidence covers the code as of review 001.

## Outstanding owner actions

1. Optional Codex verification of this F1 change.
2. Manual acceptance after restarting the API: walk through notification and purchase, then make a real Stripe test purchase (card 4242 4242 4242 4242) through a waitlist offer. Record the result yourself. None is claimed.
3. Claude transcript: in this session run `/export` and save it as `C:\Dev\project-brushfire\ai-sessions\PHASE-2\claude-session.txt`. After ending the session, copy `%USERPROFILE%\.claude\projects\c--Dev-project-brushfire\77bd3fc9-4329-4518-bc22-3caa875e473e.jsonl` into `ai-sessions\PHASE-2\raw\`. The existing raw snapshot stops at 00:04. No export is present.
4. Codex chat export.
5. PR, optional Copilot review and submission: owner only.

No remaining defect was found during this pass beyond the limitations above. Implementation is stopped.

No Git commands or indirect Git operations, no extra agents or sessions, no dependencies or architecture changes. Usage: **Not available**.
