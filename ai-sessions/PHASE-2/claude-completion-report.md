# Phase 2 — Claude Code completion report

Worker: local Claude Code session (VS Code extension), model Claude Opus 5.5, session ID `77bd3fc9-4329-4518-bc22-3caa875e473e`, working directory `C:\Dev\project-brushfire`. Prompt: [claude-launch-prompt.md](claude-launch-prompt.md). Audience: the owner and Codex reviewer.

## Clock

| Point | Time (America/New_York) |
|---|---|
| Owner launch (fixed by prompt) | 2026-11-04 23:32:33 |
| This session first read the clock | 23:38:51 (1h53m42s remaining) |
| Server feature complete, 97 tests passing | ~23:52 |
| Client, docs, HTTP smoke, rendered UI check complete | 00:03:34 |
| Report written | ~00:05 (raw snapshot 00:04), **before the 01:02:33 functionality target and the 01:32:33 hard stop** |

The clock was never restarted. Time came from the shell `date` command.

## Diagnosis

- **Overcrowding:** Phase 1 holds already used a guarded conditional UPDATE inside a transaction (`InventoryService`), with passing concurrency tests. The hayride seed had three **independent** caps (Adult 25, Child 25, Family Wagon 3) and no total. Each type could correctly sell out while 53 people boarded a 25-person ride. This was a missing aggregate rule, not a race. I added no row locking or process lock.
- **Timezone:** no defect reproduced (see Evidence). I added visible EST/EDT labels to customer-facing times and regression tests. No date logic was changed.

## Delivered behavior

1. **Shared departure limit:** `TimeSlot.SharedCapacity` (hayride = 25). The hold is one SQL UPDATE that checks the type cap and `SharedCapacity − Σ(Reserved+Sold)`, so usage is derived from the existing ledger and cannot drift. Mixed orders are all-or-nothing. Public `remaining` is clamped to the shared remainder. Legacy overfull departures show 0 and refuse new holds; history is untouched. Family Wagon wording is updated for future bookings, and order snapshots are kept.
2. **Waitlist:** idempotent join (no provider call, no card), private GUID status link, no listing endpoint, email never in public responses. Strict FIFO with head-of-line blocking. Public sales close while anyone is waiting. Offers hold capacity through `SlotInventory.Reserved`. The notification gate means only `Notified` entries can accept. A 24 h offer lifetime restarts at notification. Expire and cancel return places exactly once (guarded flip + release in one transaction). A retry-safe accept turns the reservation into an ordinary order (idempotency key `waitlist-{reference}`), then uses the normal single-session Stripe checkout. An unknown Stripe outcome keeps places held and the queue does not advance. The demo reset cancels open offers in its final transaction.
3. **Development-only operator page** (`/?operator`, footer link): outbox with contact, private link and labelled states (Queued / Places reserved — contact / Manually notified / Checkout started / Purchased / Expired / Cancelled / Lapsed / Refunded), a "Mark notified" button, and a guarded capacity increase per GA departure (increase only, never below held+sold). The API answers 404 unless Development + `Waitlist:OperatorToolsEnabled` + loopback, requires the `X-Local-Operator: owner` header, and requires the local Origin for changes. **The app sends no email.**
4. **Schema upgrade** (`Data/SchemaUpgrade.cs`): runs at startup after `EnsureCreated`. It writes a `VACUUM INTO` backup first, then applies additive SQL in one transaction: column, hayride 25, wagon text, waitlist tables. It is idempotent, never deletes or reseeds, and logs any over-limit departures.

## Changed files

Server: `Controllers/OperatorController.cs` (new), `Controllers/WaitlistController.cs` (new), `DTOs/WaitlistDtos.cs` (new), `DTOs/EventDtos.cs`, `Data/SchemaUpgrade.cs` (new), `Data/AppDbContext.cs`, `Data/SeedData.cs`, `Models/Waitlist.cs` (new), `Models/Event.cs`, `Program.cs`, `Services/WaitlistService.cs` (new), `Services/WaitlistOptions.cs` (new), `Services/InventoryService.cs`, `Services/OrderService.cs`, `Services/CatalogService.cs`, `Services/DemoResetService.cs`, `appsettings.json`, `appsettings.Development.json`.
Tests: `SharedCapacityTests.cs`, `WaitlistTests.cs`, `WaitlistHttpTests.cs`, `SchemaUpgradeTests.cs`, `VenueTimeTests.cs` (all new), `TestSupport.cs`, `GeneralAdmissionOrderTests.cs`.
Client: `features/WaitlistJoin.tsx`, `features/WaitlistStatusView.tsx`, `features/OperatorOutbox.tsx` (new), `features/TicketSelector.tsx`, `features/OrderConfirmation.tsx`, `features/ReviewOrder.tsx`, `App.tsx`, `context/appState.ts`, `context/AppContext.tsx`, `services/api.ts`, `services/format.ts`, `types/api.ts`, `theme/theme.css`, `scripts/check-venue-time.ts` (new).
Docs/evidence: `README.md`, `docs/architecture.md`, `docs/contracts/booking-rules.md`, `docs/contracts/api-contract.md`, `docs/phase-2/WRITE-UP.md` (new), `docs/phase-2/verify-waitlist-ui.mjs` (new), `ai-sessions/PHASE-2/evidence/*` (new), this report.
**Not edited (Codex-owned):** `docs/phase-2/README.md`, `ai-sessions/PHASE-2/README.md`. Suggested updates are below.

## Commands and results

| Command | Result |
|---|---|
| `dotnet test server/BlevinsPass.slnx` (baseline, before changes) | 64 passed, 0 failed, 0 skipped |
| `dotnet build server/BlevinsPass.slnx` (final) | Build succeeded, 0 warnings, 0 errors |
| `dotnet test server/BlevinsPass.slnx` (final) | **97 passed, 0 failed, 0 skipped** (33 new) |
| `npm run lint` / `npx eslint .` (client) | exit 0, no findings |
| `npm run build` (client) | `tsc -b` + Vite build succeeded (49 modules) |
| `node scripts/check-venue-time.ts <zone>` ×7 (Kiritimati +14, Kolkata +5:30, London, UTC, New York, Los Angeles, Pago Pago −11) | 6/6 cases pass in every zone ([evidence](evidence/venue-time-node-check.txt)) |
| HTTP smoke (curl, real API on :5051, **copy** of owner's Phase 1 DB) | upgrade applied + backup, shared cap 409 at the 26th person, join/no charge, 403s, notify gate, capacity, retry-safe accept ([evidence](evidence/http-smoke-2026-11-04.txt)) |
| `node docs/phase-2/verify-waitlist-ui.mjs` (Playwright + headless Chrome, real API :5050 + Vite :5173, fresh scratch fixture DB) | passed: join + status at 1440/390, operator at 1440/390, venue time "10:00 AM EST" under LA/Tokyo/Pago Pago browser zones, no horizontal overflow, 0 page errors, Buy only after notification ([results.json](evidence/ui/results.json), 7 screenshots) |

One intentional expectation change: `Capacity_is_independent_per_slot_and_ticket_type` now expects 17 adult places (not 20) after 3 wagon + 5 adult tickets, because of the owner's shared-limit rule. No test was disabled or skipped.

Key new tests: mixed types stop at 25; type caps still apply; a later-line overflow holds nothing; 12 concurrent mixed groups → exactly 5 succeed (25 people); departures/events independent; confirm counts / release returns (idempotent); legacy overfull fails closed; FIFO + retry dedupe + zero provider calls on join; validation (email, oversize group, seated event, closed slot); notification gate; strict head-of-line + public sales blocked; concurrent expiry releases once and advances; 4 concurrent accepts → 1 order; cancel/lapse passes places on; unknown Stripe outcome keeps places; capacity increase rules; outbox labels + demo reset closes offers; HTTP boundary (no email in public status, 403/404 operator cases); schema upgrade on a Phase 1-shaped DB (data, Stripe refs and theme kept, backup, idempotent, schema equals fresh `EnsureCreated`, overfull report); venue time conversions and `Z` serialization.

## Evidence boundaries

- **Browser:** one automated headless-Chrome run (above). It is not a manual or cross-browser/device/accessibility certification. A pre-existing cosmetic item showed up in the screenshots: the Phase 1 "Skip to main content" link appears over page text in full-page captures. It is not changed in Phase 2.
- **Stripe:** all payment outcomes in tests come from the deterministic fake gateway. **No real Stripe call was made in Phase 2** (the smoke and UI checks stopped before checkout). A real test purchase through a waitlist offer is an owner step.
- **Owner database:** `server/EventRegistration.Api/blevinspass.db` was only copied (still `Nov 2 16:47`, 131072 bytes). The API was never pointed at it. Upgrade evidence comes from the scratchpad copy. No reset or refund was executed.

## Known gaps / limitations

- Operator tools are a Development-only local demo: no authentication, no sender, single-process demo assumptions.
- Queue movement happens on requests (no scheduler), so an expired offer is released on the next page load or request for that departure.
- Strict head-of-line can leave places unused behind a large group (an owner policy choice). There is no best-fit mode.
- No email-based duplicate detection (that would reveal list membership). Duplicates are prevented per request via the Idempotency-Key.
- Waiting entries stay after a demo reset, and open offers are cancelled.
- The production bundle still contains the operator component code (unreachable without `import.meta.env.DEV`; the API refuses outside Development).

## Owner steps

1. **Back up, then upgrade:** stop any running API, copy `server\EventRegistration.Api\blevinspass.db` somewhere safe, then `dotnet run` as usual. Look for the "Database upgraded to Phase 2 schema" log line and its `.pre-phase2-*.bak` path.
2. **Manual provider acceptance (optional):** sell out a hayride departure (or use the operator page), join the waitlist from the sold-out panel, open `/?operator`, raise capacity, contact yourself, press **Mark notified**, open the private link, **Buy these tickets**, and pay with Stripe test card 4242 4242 4242 4242. Confirm the status page shows "Purchased". Attest the result yourself; I claim no real-provider success.
3. **Export this session:** in this same Claude Code session run `/export` and save it as `C:\Dev\project-brushfire\ai-sessions\PHASE-2\claude-session.txt`. Then copy the raw JSONL **after the session ends** (it is still growing) from `%USERPROFILE%\.claude\projects\c--Dev-project-brushfire\77bd3fc9-4329-4518-bc22-3caa875e473e.jsonl` to `ai-sessions\PHASE-2\raw\`. No subagent or tool-result folder exists for this session. I copied an in-progress raw snapshot to `ai-sessions/PHASE-2/raw/77bd3fc9-4329-4518-bc22-3caa875e473e.snapshot-20261105-0004ET.jsonl` (cut off at 00:04 ET; key-pattern scan found 0 matches). It is not the final transcript.
4. **Git/PR:** the owner stages, commits, opens the PR and runs the optional Copilot review. I ran no Git commands.

## Suggested updates for Codex-owned files

- `docs/phase-2/README.md`: mark implementation and verification complete as of 00:05 ET and link this report, `WRITE-UP.md`, `verify-waitlist-ui.mjs` and the `evidence/` folder.
- `ai-sessions/PHASE-2/README.md`: record session ID `77bd3fc9-4329-4518-bc22-3caa875e473e`, the raw snapshot file (label: in-progress, derived copy, cutoff time in its name), and the pending `/export`.

## Usage and Git

Token usage (input/output/cache): **Not available**. **No AI Git operations:** this session ran no Git commands, no staging, commits, pushes, branches or PRs, directly or indirectly. No subagents or extra sessions were launched.
