# DEMO-RESET-001: development reset and test refunds (owner-authorized overtime)

**Subsequent owner verification:** real refund manually verified **PASSED**, and all owner submission checklist actions confirmed complete. [Final confirmation](../../ai-sessions/OWNER-FINAL-CONFIRMATION.md). Earlier owner-pending statements below retain the historical implementation/report cutoff; they are no longer open submission items.

| Item | Value |
|---|---|
| Original stop | Nov 1 2026, 11:00 p.m. ET. **This follow-up is overtime** and does not change the original two-hour record |
| Started | 11:01 p.m. ET (owner message with [prompt](../../ai-sessions/BUILD-001/demo-reset-prompt.md)) |
| Code and docs finished | about 11:12 p.m. ET. **Overtime: about 12 minutes** for implementation |
| Owner decision | 11:05 p.m.: refund every completed test purchase made in the app (the database had 5 Confirmed test orders, so the target was ambiguous; I asked first) |
| Token usage | **Not available** |

## What was built
- API: `POST /api/dev/demo-reset` (`DemoResetController`, `DemoResetService`, `DemoResetOptions`, `BookingOperationGate`).
  - The gateway gained `RefundSessionPaymentAsync`, which uses Stripe Refunds on the session's PaymentIntent with a stable idempotency key and verifies by re-reading the refunds.
  - New terminal `OrderStatus.RefundedByDemoReset`; it is an enum string, so no schema change is needed.
  - The booking entry points (create order, checkout, confirm, availability) share the gate.
- Client: footer `DemoResetControl` (dev server only), with an inline confirmation, busy, success and failure states. A `demoAvailabilityReset` reducer action clears selections and checkout and reloads availability while keeping the theme and catalog. The confirmation screen handles the refunded status. Added `vite-env.d.ts`.

## Verification (AI workspace)
- Real NuGet assemblies from the owner's `bin` folder, system SQLite, and the reflection runner (VSTest isn't available offline). Equivalent of `--filter`: classes `DemoResetTests LifecycleRaceTests PaymentLifecycleTests` → **33 total, 33 passed, 0 failed** (14 new reset cases, 6 race cases, 13 payment-lifecycle cases).
  - Owner command for the PC: `dotnet test server/BlevinsPass.slnx --filter "FullyQualifiedName~DemoResetTests|FullyQualifiedName~LifecycleRaceTests|FullyQualifiedName~PaymentLifecycleTests"`.
- Client: `npm run build` and `npm run lint` pass. The production bundle contains no "Reset demo availability" text.
- Real API in Development on :5050 + Vite + Playwright:
  - The control is visible in Fall and Winter.
  - Cancel sent 0 requests.
  - The button is busy and disabled during a run.
  - With a paid order this workspace couldn't refund, the reset failed with a clear message and sold stayed at 2.
  - Once that order was resolved, a reset gave reserved/sold 0/0, selection cleared, theme kept, URL cleared.
  - At 360 px: scrollWidth 360 and 44px buttons.
- The same API in **Production** returned **404**.

## Real refund status
**Not executed by the AI.** My workspace cannot reach Stripe; only the API on the owner's PC holds the key. Owner action:
1. Restart `dotnet run` so the new code loads.
2. Click **Reset demo availability → Reset and refund** in the dev UI.

The result line and API response list each order, session, PaymentIntent and refund id with its provider status. Pending refunds or failures leave availability untouched and are safe to retry.

## Files
Server: `Models/Enums.cs`, `Services/{ICheckoutGateway,StripeCheckoutGateway,PaymentService,DemoResetService,DemoResetOptions,BookingOperationGate}.cs`, `DTOs/DemoResetDtos.cs`, `Controllers/{DemoResetController,OrdersController,EventsController}.cs`, `Program.cs`, `appsettings*.json`. Tests: `TestSupport.cs`, new `DemoResetTests.cs`. Client: `App.tsx`, `features/{DemoResetControl,OrderConfirmation}.tsx`, `context/appState.ts`, `services/api.ts`, `types/api.ts`, `theme/theme.css`, `vite-env.d.ts`. Docs: README, DESIGN_SYSTEM, booking-rules, api-contract, this log, ai-sessions manifest.

No Git commands were run.

## Final-review corrections (Codex review, 11:14 p.m. → about 11:18 p.m. ET; additional overtime about 4 min; total overtime about 18 min)
1. **Request boundary:** before calling `DemoResetService`, `DemoResetController` now requires all of the following:
   - Origin equal to the configured local frontend (403 otherwise);
   - `X-Demo-Reset: confirm` (403);
   - `application/json` (415);
   - `{ "confirm": true }` (400).

   The Development, opt-in and loopback guards stay first (404). There is no CORS policy, and only the reset client call changed.
2. **Accurate partial results:** the blocked message counts only `refunded` results as completed and names pending or incomplete refunds separately. The UI keeps and lists the per-order outcomes and blockers.

**Evidence (AI workspace, real NuGet assemblies, system SQLite, reflection runner)**
- Classes `DemoResetTests DemoResetHttpTests LifecycleRaceTests PaymentLifecycleTests` → **44 total, 44 passed, 0 failed**.
- `DemoResetHttpTests` runs a real local Kestrel host on an isolated fixture database with the fake Stripe gateway. It covers 8 rejection cases (each asserting 0 refunds and the paid order still Confirmed), plus a valid request (200, refunded) and Production (404).
- `Mixed_completed_and_pending_refunds…` passes.
- Owner command: `dotnet test server/BlevinsPass.slnx --filter "FullyQualifiedName~DemoReset|FullyQualifiedName~LifecycleRaceTests|FullyQualifiedName~PaymentLifecycleTests"`.
- `npm run build` and `npm run lint` pass.
- Browser, real API + Vite proxy:
  - A direct POST without Origin and header got 403.
  - Through the UI, a blocked reset showed the message plus per-order details.
  - Through the UI, a successful reset left inventory at 0/0, which proves the Origin header survives the proxy.
- No real reset or refund was run against the owner's database or Stripe.

**Real refund status:** still **owner action**. Restart the API and click the button. It refunds every qualifying completed test purchase made in this app and nothing else in the Stripe account. The results must be read from the returned per-order statuses.
