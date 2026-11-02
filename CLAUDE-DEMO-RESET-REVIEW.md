# Demo reset — final focused review

Codex independently compiled the current sources and ran only DemoResetTests, LifecycleRaceTests and PaymentLifecycleTests using the standard .NET runner: **33 passed, 0 failed, 0 skipped**. Client build and lint passed. Build output was isolated in a temporary directory because the owner may have the API running. No reset, order, payment or refund was triggered by this review; no application source or Git state was changed.

The reset service appropriately waits for payment outcomes, preserves history, uses the booking gate, and does not globally zero availability until confirmed purchases have verified refund results. The log records the owner's separate decision to refund all completed app test purchases. The single-process limitation remains documented. Two small corrections remain before final acceptance.

## Prompt for Claude

Make only these focused corrections under the already authorized small overtime extension. Do not expand features, run Git commands, or trigger a real reset/refund as part of testing. Record actual additional overtime.

### 1. Protect the destructive endpoint's request boundary

`server/EventRegistration.Api/Controllers/DemoResetController.cs` currently accepts POST without a bound request body, content-type restriction or origin/custom-header check. Development mode and loopback access do not distinguish the intended UI from a cross-origin request made by a browser on the same machine. The frontend confirmation is not enforced by the server.

Require an explicit JSON confirmation request (e.g. `{ confirm: true }`), application/json, and a dedicated custom header. Validate Origin against the configured local frontend origin, rejecting absent/mismatched origins for this browser-only operation. Do not enable permissive CORS. Reject requests before invoking DemoResetService or any provider operation. Keep all existing Development/opt-in/loopback/test-mode guards. Update only the reset client call to send the required fields/header. Do not introduce a login system or change ordinary booking routes.

Add focused HTTP-boundary tests proving unconfirmed/malformed JSON, form/no-content-type requests, missing custom headers, and foreign/missing origins cannot reach reset/refund logic. Verify the valid local confirmed request succeeds with a fake provider and non-Development remains 404. A test of IsAllowed alone is not evidence of these request protections. Test only isolated fixture databases/fake Stripe, never the owner's live demo database.

### 2. Accurately describe partially completed or pending refunds

`DemoResetService.BlockedMessage` uses refunds.Count to say refunds were already made, but that list also contains refund_pending/not_fully_refunded results. Count only fully refunded results as completed, and identify pending/uncertain results separately. The failed UI should retain/display the per-order result/status already returned by the API so the owner can tell what completed before a blocker. Do not claim a pending refund succeeded or that earlier external refunds were rolled back.

Add one focused mixed-result test (one completed refund plus one pending/failure) asserting the reported counts/statuses are accurate and availability remains safely held where required.

## Verification and handoff

Run only the affected reset boundary/service tests plus any directly impacted lifecycle tests, then client build/lint. Do not rerun unrelated suites or create real test purchases. Report exact commands/results, files changed, overtime and remaining limitations. Update the reset documentation and archive report. After the correction, the owner can restart the API and use the button; the button refunds all qualifying app test purchases, not unrelated Stripe-account transactions. Real Stripe outcomes still require the returned result/provider verification; this review does not certify they have executed.
