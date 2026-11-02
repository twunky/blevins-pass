# REVIEW-001 — payment lifecycle and confirmation recovery

Prepared by Codex after reviewing BUILD-001. This is a correction prompt for the owner to send to Claude, not evidence that corrections have been implemented or permission for Codex to change application code.

## Independent review evidence

Codex ran the current sources on the owner's machine: `dotnet test server/BlevinsPass.slnx --no-restore` passed 33/33 with no skipped tests; both .NET projects built. `npm run build` and `npm run lint` passed. The initial sandboxed .NET attempt failed; the same test command succeeded outside that sandbox. Real Stripe purchase success is owner-reported in the build log, not independently repeated by Codex. Browser evidence in the supplied report used a stand-in API.

The implementation is not yet accepted: existing tests do not cover the lifecycle interleavings below. These findings are based on source inspection; the races have not yet been reproduced with a deterministic regression test.

## Prompt to Claude

Continue BLEVINS PASS with focused corrections only. Read AGENTS.md, CLAUDE.md, the approved plan, booking contract, and current code/tests. This owner-submitted correction instruction authorizes these fixes and relevant tests/docs within the existing deadline: November 1, 2026, 11 p.m. America/New_York (2026-11-02T04:00:00Z); feature freeze 10:50 p.m. Calculate actual time remaining. Do not start if the deadline has passed without an updated owner instruction. No new features, extra agents, Git commands, commits, pushes, or history changes. Preserve owner work and secrets.

### 1. P1: atomically arbitrate checkout creation versus abandoned-order release

PaymentService reads an order, later saves CheckoutAttemptStartedUtc/PlannedSessionExpiresUtc, and calls Stripe. ReleaseAsync guards only pending status; it does not ensure the stale caller's no-attempt assumption remains valid. A cleanup request can read a null attempt marker, checkout can persist its marker, and cleanup can then release the claims before Stripe creates a payable session. The later PendingCheckout-to-PendingPayment update can affect zero rows, yet the checkout method still returns the provider URL. The buyer can pay after inventory was returned to sale.

Implement database-backed atomic arbitration of the creation claim and pre-session release, checking relevant state/marker predicates and affected-row counts. Preserve short transactions with provider calls outside them. A stale cleanup decision must not release an active or unresolved attempt; a stale checkout caller must not launch a payable session for a released order. Do not return a successful checkout URL after an invalid transition. Reuse the established lifecycle rather than introducing a framework.

Add deterministic separate-connection tests that coordinate both interleavings with barriers, rather than hoping Task.WhenAll triggers the race. Assert there is no payable session with released claims.

### 2. P1: freeze one provider payload and retain holds on ambiguous recovery errors

Concurrent initial checkout requests can each load null attempt/expiry fields and independently set a different planned expiry before calling Stripe with the same idempotency key. Stripe can reject differing parameters for an existing key. StripeCheckoutGateway currently treats virtually every 4xx except 409/429 as proof that no session exists; PaymentService then releases inventory. A rejected retry does not establish that the earlier request never created a payable session.

Persist/select one immutable creation payload atomically so competing callers reuse it. Distinguish proven first-attempt failure from an error during an already-uncertain recovery; an HTTP status alone is insufficient evidence to release prior unresolved claims. Preserve holds when identity/outcome is uncertain and provide recovery. Test concurrent creation with different clock values and an adapter that rejects mismatched payloads, plus a lost response followed by a rejected retry. The current fake returns an existing session without comparing parameters, so strengthen it where appropriate.

Stripe reference: https://docs.stripe.com/api/idempotent_requests.md — parameter mismatches can error even when the key belongs to a prior operation.

### 3. P2: preserve return-order recovery across refresh and failed verification

AppContext removes orderId from the URL before server verification and stores the order only in in-memory Context. Refreshing the confirmation loses the reference and returns to browsing; a failed first verification can leave no order or retry reference. Keep the unguessable order reference in the route/query until an explicit booking reset, or use an equivalent durable navigation reference. Context still owns application state; the URL identifies the resource, not payment truth. Re-fetch/reconcile server-side on refresh and expose a retry after transient failure. Never treat the cancel/success query flag as authoritative payment state.

Verify successful return -> refresh -> same confirmed order, and a failed first verification -> retry -> confirmed order. Check that repeated confirmation never increments sold inventory twice. Remove unsupported UI promises of an email receipt unless receipt delivery is actually configured/verified.

## Completion requirements

Run the focused regressions and existing .NET suite, client build/lint, and rendered refresh/retry checks against the real API when available. Record exact results and limitations; mocks do not establish full browser/backend integration. Update contracts, README and BUILD-001 log to match behavior; retain an accurate correction-session archive.

Do not mark raw-session snapshots or prompts/reports as a complete transcript. Planning exports and the full implementation export remain required; refresh applicable raw records after the session ends and accurately describe any copy-time cutoff. Do not copy unrelated projects or expose secrets.

Return one concise correction report with files changed, regression evidence, unresolved issues, remaining time, archive status, and confirmation of no Git operations. Stop at the deadline.
