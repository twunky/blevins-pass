# Claude Code — launch message

Open Claude Code in `C:\Dev\project-brushfire` and paste the message below. Submitting it to Claude as an execution instruction authorizes the full approved timed build. It does not authorize this Codex chat to implement the application.

The fixed deadline is November 1, 2026 at 11:00 p.m. America/New_York (November 2 at 04:00 UTC). Claude must calculate actual remaining time when it starts; the originally planned 9 p.m. start does not create a fresh two-hour allowance.

---

Begin implementing **BLEVINS PASS** now in:

`C:\Dev\project-brushfire`

I authorize you to execute the **entire approved master implementation plan**, including scaffolding, in-scope application code, dependency restores, local startup, meaningful tests, routine fixes, documentation, and session records. TASK-001 through TASK-008 are internal milestones. Continue automatically after verifying each milestone; do not stop after the foundation or wait for routine per-task approval. Verification does not constitute my final acceptance.

**Deadline:** November 1, 2026, **11:00 p.m. America/New_York (EST, UTC−05:00)**, equivalent to **2026-11-02T04:00:00Z**. Record the actual start and remaining time before reading/implementation. Adjust milestone budgets to the remaining time; do not reset or extend the deadline. Freeze features by 10:50 p.m. Eastern and reserve the remaining time for final evidence, archives, and my manual submission steps. If this deadline has already passed, do not start a new build window without my explicit timing instruction.

### Read this context before implementation

Read the following files in full, resolving links to any relevant existing code/contracts:

1. `C:\Dev\project-brushfire\AGENTS.md` — shared rules, boundaries, readable code, verification.
2. `C:\Dev\project-brushfire\CLAUDE.md` — Claude entry guide.
3. `C:\Dev\project-brushfire\README.md` — project navigation and current status.
4. `C:\Dev\project-brushfire\handoff-doc.md` — authoritative project specification, including the v2.1 workflow amendment and original baseline.
5. `C:\Dev\project-brushfire\project-review-and-revised-plan.md` — approved audit, architecture/lifecycle corrections, schedule and contingency gates.
6. `C:\Dev\project-brushfire\CLAUDE-MASTER-IMPLEMENTATION-PROMPT.md` — complete active implementation instructions and final reporting requirements.
7. `C:\Dev\project-brushfire\req-1.png` and `C:\Dev\project-brushfire\req-2.png` — original assignment and submission requirements. Inspect the images when supported; their requirements are also transcribed in the handoff/master prompt. Report unavailable image inspection honestly.

The old `TASK-001-claude-code-prompt.md` is retired; do not use its former foundation-only workflow. This launch resolves the planning-only execution gate in AGENTS.md and CLAUDE.md. Record the actual authorization in the build log and update those status statements before proceeding. The handoff's historical per-task approval passages are superseded by its v2.1 amendment and the approved master workflow.

### Priorities and safeguards

- Required stack: ASP.NET Core Web API on **.NET 10**, **React + TypeScript + Vite**, **Context/useReducer as the shared app-state owner**, **EF Core + one SQLite application file**, and **Stripe-hosted Checkout in sandbox/test mode**.
- First prove the original assignment: five General Admission events on an interactive calendar, at least two slots per event, selected-slot ticket types/remaining counts, quantity selection/review, real test payment, server verification, and independent capacity enforcement. Use meaningful SQLite-backed business-rule tests, including competing claims and idempotent payment transitions.
- Then implement the sixth event: the compact 18-seat concert with two performances, three differently priced sections, accessible SVG selection, correct seat colors/statuses, performance-specific claims, and shared Checkout. Follow the approved contingency gates if required functionality is behind.
- Retain persisted Fall/Winter theme switching through shared components without losing booking selections. Prioritize functionality and readable responsive UI before visual refinement.
- Prices, inventory, and payment verification belong to the backend. Never release a hold solely because a local timestamp elapsed while a Stripe session may still accept payment. Follow the master prompt's transactional, idempotency, reconciliation, and expiry rules.
- Write conventional, human-readable C#/TypeScript with descriptive names, strict typing, focused functions/components, useful comments, concise onboarding docs, and documented tradeoffs. A new developer must not need this chat history to run or understand the app.
- **Never commit or push. Run no Git commands**, including init, staging, branching, merge, rebase, reset, stash, checkout, worktrees, or history changes. Do not use remote tools/scripts to bypass this rule. I perform all Git operations and reviewer invitations.
- Keep secrets server-side and out of output, code, browser bundles, docs, and archives. Prerequisites were confirmed ready, but verify actual tools/configuration safely; never print a key. Do not deploy, publish, change accounts, or launch extra agents/sessions.
- Make ordinary implementation decisions and fix routine problems independently within the approved scope. Ask only for a genuine missing requirement, permission/credential, or necessary out-of-scope decision; continue useful independent work while awaiting an answer. Never fabricate payment success, test results, exports, or usage metrics.

Keep one concise `docs/tasks/BUILD-001.md` log with milestone timing/evidence and scope cuts. Preserve this launch message and the master instructions as supporting prompt records, plus the complete applicable transcript and Claude raw session files; prompts/reports do not replace exports. Mark interactive owner export steps pending when necessary.

Start by recording actual remaining time, reading the context, inspecting the current workspace without Git, and implementing the foundation. Advance through verified milestones automatically. Provide meaningful progress updates and a consolidated final report with actual commands/results, working run instructions, acceptance gaps, archive status, elapsed time, and confirmation that you performed no Git operations. Stop at completion or the fixed deadline.
