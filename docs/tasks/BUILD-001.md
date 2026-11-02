# BUILD-001: timed implementation log

**Historical phase log:** the current evidence matrix and actual overtime are in [FINAL-HANDOFF](../FINAL-HANDOFF.md) and [PROJECT-TIMELINE](../PROJECT-TIMELINE.md). Earlier environment limits and owner-pending checks below describe their original reporting time; Codex later performed standard local verification. Do not interpret an old task-table “Done” as proof of real refunds or complete native exports.

| Item | Value |
|---|---|
| Owner launch | Sun Nov 1 2026, 9:08 p.m. America/New_York (02:08Z) |
| Fixed deadline | 11:00 p.m. ET (2026-11-02T04:00:00Z). Feature freeze 10:50 p.m. |
| Remaining at launch | 1 h 52 min (late start; no clock reset) |
| AI tool / model | Claude (claude.ai cloud session linked to the owner's PC), configured model `claude-opus-5-5` |
| Token usage | **Not available** (not exposed to the session) |
| Human review duration | **Not available** |

## Environment constraints found at launch (they shape every result below)

1. **No shell on the owner's PC.** The desktop link's command workspace failed to start because of low disk space (needs ~17 GB free). File copy to `C:\Dev\project-brushfire` worked, but running `dotnet`, `npm` or Stripe there did not.
2. **The AI's cloud workspace had no .NET SDK, and NuGet, dot.net and api.stripe.com were blocked** by its network allowlist. npm worked.
3. Consequence: the C# API and tests were never compiled against the real packages or executed in this session. They were type-checked offline against signature stubs (below). The React client was built, linted and browser-checked in the cloud workspace. **A real Stripe test purchase was not possible.** The owner was told at 9:10 p.m.

## Milestones

| Task | Time (ET) | Status | Evidence / notes |
|---|---|---|---|
| TASK-001 Foundation | 9:08–9:16 | **Verified (owner-run build/tests)** | net10 API, controllers, health route, Vite proxy on fixed ports, Context store |
| TASK-002 Data and reads | 9:10–9:17 | **Verified (owner-run tests)** | EF SQLite model, idempotent seed (5 GA events with 2–3 slots each and 2–3 ticket types, plus a concert with 2 performances and 18 seats), availability, seating and theme routes |
| TASK-003 Orders/payments | 9:12–9:17 | **Verified**: 33/33 tests pass on the owner's PC | Guarded holds, lifecycle, Stripe gateway, reconciliation. 33 xUnit cases written (12 GA, 13 payment lifecycle, 8 seating) on real SQLite files, with concurrency races |
| TASK-004 Required frontend | 9:17–9:22 | **Verified (build/lint/UI vs. stand-in API)** | Calendar, slot, ticket types and remaining counts, quantities, summary, review, checkout redirect, confirmation |
| TASK-005 Required-flow gate | 9:22–9:49 | **Verified (owner-run)**: 33/33 tests pass, real Stripe test purchase succeeded | UI flow checked in Chromium (Playwright) against a mock API with the documented shapes: totals $48.00 for 2 Adult; theme switch to Winter kept $48.00; no console errors. Tests and the real Stripe purchase were run by the owner (see below) |
| TASK-006 Reserved seating | built with 003/004 | UI verified vs. stand-in; server **verified by the 8 seating tests**; real seat purchase not yet tried by hand | SVG chart with legend, sold (red) and held (amber) disabled, mouse and Enter-key selection, $210.00 for VIP + Orchestra seats. Section label overlap found in screenshot and fixed |
| TASK-007 Usability/themes | 9:24–9:27 | Verified (stand-in) | Fixed horizontal overflow at 390 px (scrollWidth 390); theme persistence code path calls `PUT /api/settings/theme` |
| TASK-008 Docs/package | 9:27– | Done | READMEs, architecture, 3 contracts, this log, ai-sessions manifest, status updates in AGENTS/CLAUDE/handoff |

Contingency gates: the 9:50 "provider blocker" gate was hit from the start (Stripe and NuGet unreachable). Nothing was faked. The time went into careful code, tests and docs, and the owner can verify everything with the README commands.

## Commands actually run (cloud workspace, `client/`)

| Command | Result |
|---|---|
| `npm install` | OK (lockfile created) |
| `npm run build` (`tsc -b && vite build`) | exit 0, 44 modules, JS 246 kB (76 kB gzip) |
| `npm run lint` | exit 0, no findings |
| Playwright UI script against the mock API on :5050 + `vite` on :5173 | Pass: see TASK-005/006/007 rows. Screenshots were not added to the repo |
| `apt-get install dotnet-sdk-10.0` (Ubuntu archive, 9:31 p.m.) | SDK 10.0.114 installed. NuGet still blocked, so the real packages can't restore |
| Offline type-check: `dotnet build --no-incremental` of all API + test sources (Program.cs wrapped in a method) against hand-written signature stubs for EF Core, Microsoft.Data.Sqlite, Stripe.net and xUnit | **0 errors, 0 warnings** (nullable enabled). Proves C# syntax and internal type consistency. Does **not** prove the real library signatures match or that the tests pass. The stub project lives outside the repo |
| `dotnet test` with the real packages | **Not run** (NuGet blocked) |

## Evidence observed on the owner's PC (file listing only; the AI cannot run commands there)

| Time (ET) | Observation |
|---|---|
| 9:31:54 | `server/EventRegistration.Tests/bin/Debug/net10.0/` contains `EventRegistration.Api.dll` and `EventRegistration.Tests.dll` next to the restored `Stripe.net.dll`, `Microsoft.EntityFrameworkCore*.dll` and xUnit. So the **real build of the first-synced code succeeded** on the owner's machine |
| 9:32:16 | A test-run artifact (`.msCoverageSourceRootsMapping_EventRegistration.Tests`) appeared, so `dotnet test` started |
| ~9:49 (owner-reported) | `dotnet test` in `server/`, run after the final code sync: **total 33, failed 0, succeeded 33, skipped 0** (6.3 s). Both projects built successfully. Output pasted by the owner into the session |
| ~9:49 (owner-reported) | **A real Stripe test-mode purchase succeeded** end to end |
| — | `client/node_modules` and `client/dist` exist, so `npm install` and `npm run build` ran on the owner's PC |

Files changed after that build (synced at about 9:34): `CatalogService.cs` (seat status helper), `SeedData.cs` (time-zone fallback), `EventRegistration.Api.csproj` (UserSecretsId). They were included in the owner's 9:49 test run.

## REVIEW-001 corrections (owner-submitted Codex review, started 9:54 p.m.)

Same Claude session, same deadline. The prompt is saved at [ai-sessions/BUILD-001/review-001-prompt.md](../../ai-sessions/BUILD-001/review-001-prompt.md).

| Finding | Fix |
|---|---|
| P1: abandoned-order cleanup could release a hold while checkout was creating a payable session | `PaymentService.TryClaimCheckoutAttemptAsync` claims the attempt with one guarded UPDATE (`PendingCheckout AND CheckoutAttemptStartedUtc IS NULL`). Pre-checkout releases use `ReleaseAsync(requireNoCheckoutAttempt: true)`, which re-checks the same predicate in its UPDATE, so exactly one side wins. Checkout reloads after claiming and never calls Stripe for a released or confirmed order. After Stripe returns, a zero-row `PendingCheckout→PendingPayment` update returns a URL only if this same session is already recorded; otherwise the session is expired and a 409 is returned |
| P1: competing callers could send different payloads under one idempotency key, and a rejected retry released holds | The claim freezes `PlannedSessionExpiresUtc` once, and every caller builds its request from the stored value. A rejection releases only on the caller's own first attempt. A rejection during recovery keeps the hold (503). The gateway also treats Stripe `idempotency_error` as uncertain |
| P2: refreshing or a failed verification lost the order | The client keeps `?orderId=` until "Book another event", re-verifies on refresh, shows "Try again" after a failed verification, guards against React's double effect run in development and out-of-order responses, and drops the receipt-email promise |

**Evidence (AI workspace, about 10:00 p.m.):**
- Real NuGet assemblies (EF Core 10, Microsoft.Data.Sqlite, Stripe.net, xUnit) were copied from the owner's `bin` folder. All API and test sources were compiled against them with the Linux SDK and run with a small reflection-based runner, because VSTest isn't available offline. Native SQLite was the Ubuntu system `libsqlite3` instead of the bundled `e_sqlite3` (that file is nested too deep to copy). Result: **39 total, 39 passed, 0 failed** (33 original + 6 new in `LifecycleRaceTests`).
- Regression check: with the old `PaymentService`/`StripeCheckoutGateway`, `Lost_response_then_rejected_retry…` **fails**, and passes with the fix. The two arbitration tests call methods that don't exist in the old code. `Concurrent_first_checkouts_with_different_clocks…` also passes on the old code, because the forced order lets caller A save before caller B reads. It documents the frozen-payload contract rather than reproducing the old read-read race, which the atomic claim now makes impossible.
- Real API (same assemblies) on :5050 + Vite on :5173 + Playwright, with no Stripe key or network. Results:
  - Verification while the API was down showed "Try again". After the API restarted, retry showed the order.
  - Refresh kept `?orderId=` and the same order.
  - A cancel return showed the released order (first attempt showed stale state from the development double effect; fixed and re-run), and a refresh still showed it as released.
  - "Book another event" cleared the URL.
  - The booking flow reached "Pay with Stripe" and showed the actionable "Stripe is not configured" error.
  - A confirmed-order refresh with real Stripe was **not** possible here.
- `npm run build` and `npm run lint`: pass.

**Unresolved or owner-pending:** re-run `dotnet test` on the PC (expect 39), and repeat a real purchase, including a refresh of the confirmation page.

## Owner verification checklist

1. ~~`dotnet build`~~: done, succeeded
2. ~~`dotnet test`~~: done, 33/33 passed
3. ~~Real test purchase~~: done, succeeded. Still worth checking by hand: Configure the Stripe test key, run the API and client, and buy GA tickets with 4242… Confirm the order shows **Confirmed** and the remaining count drops. Refresh the confirmation (idempotent). Try to oversell the 3-capacity "Family Wagon".
4. Buy a concert seat. It turns red for that performance only.
5. Switch the theme, refresh: it persists and the selection survives the switch.

## Files

Created: `client/**`, `server/**`, `docs/**`, `ai-sessions/**`, `.gitignore`. Updated: `README.md`, `AGENTS.md`, `CLAUDE.md`, `handoff-doc.md` (status line only). Unchanged: assignment images, plan, master prompt, launch message, retired TASK-001 prompt.

## Git

No Git commands were run by the AI.
