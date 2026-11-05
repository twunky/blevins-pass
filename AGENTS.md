# BLEVINS PASS — shared development instructions

## Current authorization

**Phase2 closeout:** application implementation is stopped/frozen. The owner's final request authorizes documentation/log archival and a future-enhancement proposal file only. Current handoff: [docs/phase-2/FINAL-HANDOFF.md](docs/phase-2/FINAL-HANDOFF.md); proposed future work is not an implementation authorization. Preserve original deadline01:32:33 America/New_York November5,2026 /06:32:33Z. Further application work requires a new explicit owner launch.

**Phase 2 workflow clarification (November5):** the owner wants proposals before implementation. For subsequent application work, create and present a concrete proposal before issuing an implementation instruction. The [correction proposal](docs/phase-2/CORRECTION-PROPOSAL.md) precedes use of the saved review001 execution prompt. Keep the existing Phase2 clock and role boundaries; this is not a timer restart or added feature authorization.

**Phase 2 launch supersedes the historical closeout below:** the owner explicitly said "ok lets start and start the timer. Lets go" on November 4, 2026. Observed start: **2026-11-04 23:32:33 America/New_York / 2026-11-05T04:32:33Z**; fixed deadline: **2026-11-05 01:32:33 America/New_York / 2026-11-05T06:32:33Z**. Codex orchestrates, reviews and maintains documentation; a new owner-operated **local Claude Code** session implements and verifies Phase 2. See [Phase 2 scope/log](docs/phase-2/README.md) and [worker prompt](ai-sessions/PHASE-2/claude-launch-prompt.md). No AI Git operations, publishing, deployments, external account changes, extra agents or extra chats are authorized. Codex does not implement application changes without further explicit authorization. Preserve all Phase 1 records.

**Final closeout:** implementation and the owner-authorized overtime reset follow-up are complete at the code/verification level. The owner's final instruction authorizes documentation/log archival only. No further application changes, reset/refund execution, or new implementation sessions are authorized by this closeout. Read [FINAL-HANDOFF](docs/FINAL-HANDOFF.md), [timeline](docs/PROJECT-TIMELINE.md) and [session manifest](ai-sessions/README.md) for current evidence and remaining owner submission actions. Historical launch permissions below explain the work already performed; they are not a standing instruction to continue building.

**The owner explicitly launched the master implementation (BUILD-001) on November 1, 2026 at 9:08 p.m. America/New_York**, with a fixed deadline of 11:00 p.m. (2026-11-02T04:00:00Z). TASK-001–008 run as internal milestones under that single launch. See [docs/tasks/BUILD-001.md](docs/tasks/BUILD-001.md). Any further implementation session needs its own explicit owner launch.

The owner's subsequent direct seasonal redesign request authorizes Codex UI-001 within the same deadline. Read [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) before any UI changes; it records the implemented Autumn Editorial and Alpine Luxury language. This authorization covers frontend presentation, relevant verification and documentation, not unrelated backend features, Git operations or publishing.

## Read and resolve authority

Start with [README.md](README.md), [handoff-doc.md](handoff-doc.md) including its v2.1 amendment, [the approved review](project-review-and-revised-plan.md), and [the master prompt](CLAUDE-MASTER-IMPLEMENTATION-PROMPT.md). Original assignment evidence is in req-1.png and req-2.png. Read applicable contracts and relevant implementation before edits once those files exist.

Current explicit owner instructions take precedence. The handoff defines the project; owner-approved amendments in the review/master workflow supersede its historical proposals. The images define the original assignment. Report an unresolved material conflict instead of quietly changing requirements. The standalone TASK-001 prompt is retired.

## Git, secrets, and scope

- **Never commit or push. Run no Git commands.** The owner alone handles init, staging, commits, pushes, branches, merges, rebases, resets, stashes, worktrees, history, repository setup, and invitations. Do not use remote tools or scripts to perform those actions indirectly.
- Make only authorized workspace changes. Preserve owner work and original assignment images. Do not deploy, publish, or change external account settings. Do not launch additional agents or sessions unless the owner explicitly authorizes them.
- Keep Stripe secret keys server-side and out of code, browser bundles, output, documentation, and archives. Use local environment configuration. Inspect for secret leakage without printing values.
- Do not introduce authentication, event CRUD, a seating-map editor, extra services, or unrequested production infrastructure. Never invent test results, payment success, archive exports, timestamps, or token usage.

## Execution after the single launch

TASK-001–008 are internal milestones, not repeated approval gates. Once the owner launches the master prompt, progress through them automatically, including routine fixes, appropriate verification, and documentation. Do not stop after the foundation or wait for routine owner review. Internal verification does not constitute owner acceptance.

Planned build window: November 1, 2026, 9–11 p.m. America/New_York. Record actual start/deadline; starting late does not automatically extend the deadline. All implementation, tests, fixes, exports, reviews, and submission work share the same clock. Preserve the original assignment first; prove General Admission purchase before seating. Freeze features by minute 110 of the planned window and stop at the deadline. Follow earlier contingency gates in the approved plan.

Ask only when missing information, permission, or a material out-of-scope decision prevents safe progress. Continue useful independent authorized work while awaiting an answer. Silence is not approval. Do not spend the remaining clock repeatedly retrying an external blocker.

## Architecture and coding standards

- ASP.NET Core Web API, net10.0, controllers and focused services; EF Core with one SQLite application file; React + TypeScript + Vite; Stripe-hosted test Checkout.
- React Context/useReducer owns shared application state. A ThemeProvider reads it; no separate mutable cart/theme store or server-state library. Transient presentation state may stay local.
- The API owns prices, inventory, validation, and payment verification. Use integer cents/USD, UTC persistence/API timestamps, and America/New_York display. Enforce all-or-nothing guarded inventory updates, active seat uniqueness, idempotent confirmation/release, and safe Stripe reconciliation before inventory release.
- Use descriptive domain names, conventional casing, small cohesive functions/components, explicit API types, C# nullable checks, and strict TypeScript. Keep control flow direct. Follow existing formatting, linting, and analyzers without broad suppressions.
- Comments explain nonobvious reasons and invariants. Avoid redundant commentary, clever compression, speculative abstractions, generic repositories, CQRS, and unnecessary dependencies. Make errors actionable without leaking secrets.

## Verification and documentation

Build the foundation before adding dependent features. Prove actual business rules with meaningful SQLite-backed tests, including concurrent claims, independent capacity, all-or-nothing changes, server pricing, and idempotent payment transitions. Use a deterministic fake payment adapter for failure cases plus a real Stripe test purchase for end-to-end evidence. Mock-only or zero-test results do not prove business rules. Run checks appropriate to changes; repeat successful checks only when relevant changes or unresolved failures justify it.

Write for a new developer without chat history. Root README explains current status, layout, exact setup/run/test commands, configuration, database initialization/reset, tradeoffs, limitations, and next steps. Link canonical contracts and concise architecture decisions. Clearly distinguish working, planned, incomplete, and unverified behavior. Update docs with implementation; avoid empty scaffolding and duplicate essays.

Maintain a build log and compact milestone evidence. Retain every applicable AI transcript, including planning sessions, and project-specific Claude /export and raw session files. Prompts/reports do not replace exports. Never collect unrelated projects; preserve originals and accurately label redacted submission copies. If interactive export is unavailable, identify the exact owner action and mark it pending. Record unavailable usage as **Not available**. Finish with actual commands/results, known gaps, archive status, deadline status, and confirmation that no AI Git operations occurred.
