# Phase 2 — independent Codex review 001

Review clock: November 5, 2026, 12:05:22–12:11:12 a.m. America/New_York (05:05:22–05:11:12 UTC) observations. Fixed hard stop remains 1:32:33 a.m. Eastern (06:32:33Z). Review is ongoing; this is not final acceptance.

## Independently verified

- `dotnet test server/BlevinsPass.slnx --no-restore --verbosity normal`: **97 passed, 0 failed, 0 skipped**, build 0 warnings/0 errors. Standard runner, approximately 18.34 seconds including build. Initial sandbox attempt exited 1 without diagnostics; the permitted non-sandboxed run succeeded.
- From client, `npm run lint` and `npm run build`: exit 0. Build processed 49 modules.
- `node scripts/check-venue-time.ts <zone>` in each of Pacific/Kiritimati, Asia/Kolkata, Europe/London, UTC, America/New_York, America/Los_Angeles, Pacific/Pago_Pago: 6/6 cases per zone, 42 checks total. These are formatter checks, separate from server tests and browser evidence.
- Reviewed shared-capacity SQL, waitlist state transitions, notification gate, operator boundary, additive backup/upgrade and frontend recovery code.

Claude's full waitlist rendered-flow run, HTTP smoke against an owner-DB copy, original-database immutability and raw archive credential scan remain **Claude-reported**, with saved evidence inspected. They were not independently repeated as complete flows by Codex. No real Stripe purchase, refund, reset or owner database upgrade was executed by Codex.

## Findings requiring correction

### 1. Checkout retry is lost after accept succeeds (P1)

`WaitlistStatusView.buy` accepts the offer before starting Checkout. When checkout returns a retryable 503, status reload becomes CheckoutStarted, while `GetStatusAsync.CanAccept` is false for Accepted entries. The page removes the Buy button and shows only Check payment status. That confirmation path does not reopen checkout, so the customer cannot recover the payable link from the UI, despite the backend supporting idempotent recovery. Refresh has the same limitation.

Independent headless Chrome probe of the production client with controlled API fixtures (no real API/provider/database): one accept + one checkout 503 -> **0 purchase/retry buttons**, 1 payment-status link. [Result](evidence/codex-review/checkout-recovery.json), [screenshot](evidence/codex-review/checkout-recovery.png), [probe source](../../docs/phase-2/review-probes/check-recovery.mjs). Fix needs a server-authorized retry/resume path for the same pending order/session, preserving terminal states.

### 2. Public buyers can bypass a newly committed FIFO entry (P1)

OrderService checks waiting count before its reservation transaction. A public order can observe no queue, pause, then a two-person waitlist group commits while only one place remains. Resuming the public order takes that last place instead of refusing `waitlist_active`. The shared 25-person cap itself remains protected; queue priority does not.

Reproduced against actual services, separate temporary SQLite connections and fake provider, using an EF transaction interceptor to pause exactly before the public hold transaction. Output: `PUBLIC_FIFO_RACE: new public order succeeded (PendingCheckout) after older waiting entry committed; expected waitlist_active conflict.` Check queue eligibility and inventory claim under the same database serialization boundary; a process lock is insufficient.

### 3. Concurrent increases can lower an already raised capacity (P2)

IncreaseCapacityAsync checks monotonicity before the transaction; its guarded writes protect commitments but omit the current configured capacity. Pause request 30 after validation against 25, complete request 40, then resume 30: final shared limit becomes **30**. The per-type update has the same missing monotonic guard. Add the monotonic predicate to both database writes and meaningful concurrent tests.

### 4. Status refresh expires offers but does not advance the queue (P2)

GetStatusAsync calls ExpireOffersAsync, not PromoteAsync. After the first customer's offer expires, the next customer's own status refresh shows **Waiting**, with **0 reserved** although its ten-person group fits. The next customer remains stuck unless an unrelated availability/outbox/promotion path runs. This contradicts the description that offers pass on at the next request/page load for the departure. Queue progression should work through the private status refresh without operator intervention or unsafe provider release.

Findings 2–4 were independently reproduced with `dotnet run --project docs/phase-2/review-probes/ReviewProbes.csproj --no-restore` (workspace-local NUGET_SCRATCH used for tooling). [Probe source](../../docs/phase-2/review-probes/Program.cs). Initial restore attempt encountered a NuGet filesystem error; the subsequent cached-dependency run exited 0 and printed all three observed failures. These are **review probes revealing defects**, not three passing acceptance tests. Only disposable TestHarness databases were opened; no app DB or actual provider was used.

## Documentation and remaining evidence

The write-up is 504 whitespace-delimited words including headings/bullet markers by PowerShell count; shorten slightly and qualify diagnosis as supported by submitted code, without claiming examination of actual customer transaction logs. Root README still calls rendered checks pending despite Claude's evidence; reconcile that status while distinguishing reported versus independent coverage.

Claude session ID: `77bd3fc9-4329-4518-bc22-3caa875e473e`. Existing raw file is a **snapshot** with cutoff stated in filename; full `/export` and final raw JSONL remain pending. No AI Git commands by Codex. Claude likewise reports none; that is not a full external audit. Usage: **Not available**.

Next action: relay [correction prompt](claude-review-001-prompt.md) to the **same local Claude session**, preserving the original clock.
