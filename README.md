# Blevins Pass

Seasonal event registration for the Brushfire attraction: five General Admission events on an interactive calendar, a small reserved-seating concert, Fall/Winter themes, and real Stripe-hosted Checkout in **test mode**.

> **Final status, November 1, 2026:** application implemented, seasonal UI redesigned, and development reset/refund tool added. Codex independently verified the 39-test core suite, then the final **44 affected reset/payment/HTTP tests**, plus client build/lint and five-width UI checks. **The owner manually verified the real refund as PASSED and confirmed all submission checklist actions complete.** See [final handoff](docs/FINAL-HANDOFF.md), [timing and overtime](docs/PROJECT-TIMELINE.md), and [session records](ai-sessions/README.md). The added reset work used owner-authorized overtime; this is not presented as a strict on-time two-hour delivery.

## Stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core Web API, **.NET 10**, controllers + focused services ([server/](server/README.md)) |
| Data | EF Core + **one SQLite file** (`server/EventRegistration.Api/blevinspass.db`), created and seeded on start |
| Client | **React 19 + TypeScript + Vite**; React Context + `useReducer` is the single shared state ([client/](client/README.md)) |
| Payments | **Stripe-hosted Checkout, test mode only**; payment verified server-side on return |
| Tests | xUnit against real SQLite files, including concurrency and payment-lifecycle cases |

## Prerequisites

- .NET 10 SDK (`dotnet --version` → 10.x) and a Vite-compatible Node.js version (20.19+, 22.12+, or supported newer LTS) with npm. This workspace used SDK 10.0.401 and Node 24.16.0.
- A Stripe account in **test/sandbox mode** and its test secret key (`sk_test_...`). Only the server uses it, and it is never committed.

## Run it (PowerShell)

**1. API** (terminal 1):

```powershell
cd C:\Dev\project-brushfire\server
dotnet build BlevinsPass.slnx
cd EventRegistration.Api
# Store your Stripe TEST key outside the repo (one time):
dotnet user-secrets set "Stripe:SecretKey" "sk_test_REPLACE_ME"
#   ...or for this terminal only:  $env:Stripe__SecretKey = "sk_test_REPLACE_ME"
dotnet run
```

The API listens on **http://127.0.0.1:5050**. Check it with `curl http://127.0.0.1:5050/api/health`, which returns `{"status":"ok"}`.

**2. Client** (terminal 2):

```powershell
cd C:\Dev\project-brushfire\client
npm install
npm run dev
```

Open **http://127.0.0.1:5173**. Vite proxies `/api` to the API, so no CORS setup is needed.

**3. Tests:**

```powershell
cd C:\Dev\project-brushfire\server
dotnet test BlevinsPass.slnx
cd ..\client
npm run lint
npm run build
```

### Make a test purchase

1. Click an event on the calendar. The first event month opens automatically, about a week after the database was first created. Then pick a time slot.
2. Choose ticket quantities. Remaining counts are shown per ticket type. For the **Winter Lights Concert**, click seats on the chart instead.
3. **Review order**, then **Pay with Stripe**. On Stripe's page use card `4242 4242 4242 4242`, any future expiry, any CVC, and any email.
4. Stripe redirects back. The server retrieves the session from Stripe, checks the order, amount, currency and test mode, then marks the order **Confirmed** and moves the held tickets to sold.

If the Stripe key is missing, **Pay with Stripe** shows an actionable "Stripe is not configured" error and the tickets stay held. Payment is never simulated.

## Database

- `EnsureCreated` + an idempotent seed run at API start. The seed runs only when the database has no events, so restarts keep orders and the theme setting.
- Event dates are relative to the **first** start (first event 7 days out). After those dates pass, slots show as closed. Nothing is deleted automatically.
- **Preferred demo reset:** use the development footer control described below. It verifies provider outcomes and preserves order history.
- **Disposable database recreation after a schema change:** first settle all related Stripe checkouts/payments, stop the API and preserve a backup if needed. Delete only the exact `blevinspass.db`, `blevinspass.db-wal` and `blevinspass.db-shm` files in the API folder, then restart. This destroys local orders and does not cancel or refund anything at Stripe. Do not use a wildcard that could delete backups. There are no migrations, and `EnsureCreated` does not upgrade an existing schema.
- Times are stored and returned in UTC and displayed in venue time (America/New_York).

## Layout

UI reference: [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) documents the implemented Autumn Editorial and Alpine Luxury identities, shared components, responsive behavior, typography, colors and image assets. UI work is recorded in [UI-001](docs/tasks/UI-001.md).

```text
client/            React SPA (context/, features/, components/, services/, theme/, types/)
server/            BlevinsPass.slnx, EventRegistration.Api/, EventRegistration.Tests/
docs/              architecture.md, contracts/ (API, booking rules, AI work), tasks/BUILD-001.md
ai-sessions/       AI session manifest, prompts, reports, raw session files
```

Start with [docs/architecture.md](docs/architecture.md), [docs/contracts/booking-rules.md](docs/contracts/booking-rules.md) and [docs/contracts/api-contract.md](docs/contracts/api-contract.md). Planning documents: [handoff](handoff-doc.md), [approved plan](project-review-and-revised-plan.md), [master prompt](CLAUDE-MASTER-IMPLEMENTATION-PROMPT.md), [AGENTS.md](AGENTS.md).

## What's finished and what's not

**Implemented:**
- Calendar with 6 events (5 GA + concert), at least 2 slots each, slot-specific ticket types with remaining counts, quantity selection, review, Stripe Checkout, and server-verified confirmation.
- Capacity is independent per slot and ticket type. Holds use guarded conditional updates in one transaction (all-or-nothing). Prices come only from the server.
- Order lifecycle with Stripe idempotency keys, safe recovery when session creation has an uncertain outcome, and reconciliation instead of timer-based release.
- Reserved seating: 18 seats in 3 priced sections, 2 performances, an accessible SVG chart (mouse and keyboard), and live claims enforced by a unique index.
- Fall/Winter theme saved in SQLite. Switching keeps the current selection.

**Verified:** Codex rebuilt the corrected server and passed the 39-test core suite, then independently passed the final filtered 44 reset/payment/HTTP cases. These are separate runs, not a claim that every current test class was re-run together. The client builds/lints, and 15 real-API UI scenario records pass across 1440, 1024, 768, 390 and 360px, covering both seasonal entries, no page overflow, quantities/totals, preserved selections, review and keyboard seats. Claude additionally reported reset-control checks through the real Vite proxy against fixture/fake-provider data. See [final verification matrix](docs/FINAL-HANDOFF.md#verification-evidence).

**Provider evidence and remaining limits:** the owner reported a successful real Stripe test purchase and subsequently manually verified the real refund as PASSED. These are owner-verified provider outcomes; automated fake-provider tests are separate evidence. No additional provider IDs or full cross-browser/device/accessibility certification are claimed. The owner confirmed all submission checklist actions completed.

**Shortcuts and tradeoffs:**
- No webhooks. A paid order is confirmed when the buyer returns, or by the next availability or order request for that slot. Its inventory stays held until then.
- No accounts. The email is collected by Stripe. Order IDs are unguessable GUIDs.
- The theme `PUT` endpoint is unauthenticated (local demo).
- `EnsureCreated` instead of migrations.
- Cleanup runs only when requests come in (no background scheduler).
- If Stripe keeps refusing to recover an uncertain checkout attempt, the hold stays in place rather than risk overselling. An operator would resolve it (see booking rules).
- The confirmation page shows the email Stripe verified. It does not promise a receipt, because receipt emails aren't configured in this test setup.

**Next with more time:** Stripe webhooks, organizer authentication, migrations and broader automated real-provider/cross-browser coverage. Local Playwright checks against the real API already exist in `docs/ui-review/verify-ui.mjs`; they intentionally do not submit orders/payments.

## AI and Git

All implementation was AI-assisted. Session records are listed in [ai-sessions/README.md](ai-sessions/README.md). The AI ran no Git commands; the owner performs all Git operations.

## Development-only: reset demo availability

When you run `dotnet run` (Development) and `npm run dev`, the footer shows **Reset demo availability** with a "Development tool" tag. After you confirm, it:
- closes any unpaid checkouts (expiring their Stripe sessions first);
- **refunds every completed Stripe test purchase made in this app**, each verified against its own Checkout Session, and never any other payment in your Stripe account;
- returns every ticket and seat to available. Event dates, prices, seats, the theme and order history are kept; refunded orders show as "refunded".

It needs your Stripe test key (`Stripe__SecretKey` or user-secrets). If anything cannot be verified, the **global availability reset does not complete**. Earlier verified refunds or unpaid-checkout releases may already have occurred; completed and pending results are displayed separately and cannot be rolled back as one distributed transaction. Retrying rechecks provider state and uses stable idempotency keys. It is off outside Development (`DemoReset:Enabled` in `appsettings.Development.json`). The server also checks loopback access, the configured frontend Origin, `X-Demo-Reset: confirm`, JSON content type and `{ "confirm": true }`. Production builds omit the button. Request coordination assumes one API process. Full rules: [booking rules](docs/contracts/booking-rules.md).

## Timing and reviewer handoff

The planned window was 9–11 p.m. Eastern; implementation actually launched at 9:08 p.m. Local disk/shell startup issues, remote NuGet/Stripe restrictions and later a locked running API complicated verification. Core/UI checks were completed before the 10:50 freeze. The owner then authorized a small extension for the added reset/refund tool and its review corrections, reported complete about 11:18 p.m. (18 minutes beyond the original stop; about 2h10 after actual launch). Final verification and this documentation closeout continued afterward. This explains the overrun without changing the original deadline or claiming evaluator approval. [Detailed timeline](docs/PROJECT-TIMELINE.md).

Submission entry points: [final handoff](docs/FINAL-HANDOFF.md), [submission checklist](docs/SUBMISSION-CHECKLIST.md), [AI archive manifest](ai-sessions/README.md), and [human-readable chat log](ai-sessions/CHAT-REFERENCE-LOG.md). The owner alone commits/pushes and invites reviewers. No further implementation is scheduled by this closeout.
