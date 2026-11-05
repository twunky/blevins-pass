# Phase 2 correction proposal

Status: **proposed for owner review before implementation**. Prepared after the owner's November 5 clarification that proposals should precede implementation. No application changes are made by this proposal. The original deadline remains November 5, 2026, 1:32:33 a.m. America/New_York (06:32:33Z).

## Current evidence

Initial Phase 2 implementation is present. Codex independently passed 97 server tests (0 failed/skipped), client lint/build and 42 formatter checks across seven purchaser timezones. Independent review then reproduced four uncovered defects. There is no correction report yet, and inspected source still contains the reviewed behavior. [Findings and reproduction evidence](../../ai-sessions/PHASE-2/codex-review-001.md).

## Proposed changes

**Owner priority update:** focus first on concurrency and overselling. The [capacity/concurrency audit](CAPACITY-CONCURRENCY-AUDIT.md) independently passed11 safety scenarios plus a separate Phase1 comparison control; no over-capacity result was reproduced in current guarded booking paths. Execute the proposed FIFO transaction and monotonic-capacity fixes first, preserving the shared/type/payment invariants and adding permanent regression cases. Then finish the two recovery/progression fixes. This priority update is a proposal, not an implementation launch.

| Problem | Proposed behavior | Acceptance evidence |
|---|---|---|
| Accepted offer loses its checkout action after a retryable failure | Show a server-authorized Resume checkout action for the same pending order, including after refresh. Preserve one order/session and payment safety. | Controlled browser first-503/retry-success flow; same order/session identity and inventory; terminal states cannot purchase again. |
| Public buyer can pass the queue check before a waitlist entry commits | Check FIFO eligibility and claim inventory inside one database serialization boundary. | Deterministic separate-connection test: waiting group commits before the paused public hold; public request receives a conflict and does not reserve. |
| Stale concurrent capacity increase can overwrite a larger increase | Guard current capacity and commitments inside both shared/type update statements. | Deterministic concurrent requests to30/40 leave40 or reject stale30; combined updates rollback safely on conflict. |
| Private status refresh expires the earlier offer without promoting the next entry | Progress the departure queue safely through customer refresh and return the updated offer state. | Expire Alice's offer; only Bob's private refresh runs; Bob receives a capacity-backed offer without an operator/browse request. |

No added features, production administration, provider changes, real resets/refunds, data deletion, Git operations or additional sessions/agents. Preserve the owner-confirmed 25-person shared limit, one person per ticket, FIFO/head-of-line policy and manual notification gate. The 24-hour offer duration remains the worker's documented assumption; this pass does not expand it.

## Execution and closeout after owner review

Use the same local Claude Code session and the prepared [detailed correction prompt](../../ai-sessions/PHASE-2/claude-review-001-prompt.md). Claude adds regressions and fixes, then runs the full server suite, client checks and browser recovery test. Codex reviews the returned report and independently verifies relevant results. Keep evidence from the initial failed probes intact.

Then complete owner notification/Stripe test acceptance (or explicitly record it unverified), finalize the write-up and truthful docs, obtain full Claude /export plus final project raw JSONL and preserve this Codex chat. The owner alone performs the PR, optional Copilot review and submission. Do not consume the remaining clock with optional scope. Application freeze01:22:33 and hard stop01:32:33 Eastern remain unchanged.

## Workflow clarification

Initial work followed the owner's explicit launch and clarified requirements through a detailed execution prompt. A separate proposal document was not created before that pass. For subsequent application work, present the concrete proposal before issuing its implementation instruction; this document supplies that step for the correction pass.
