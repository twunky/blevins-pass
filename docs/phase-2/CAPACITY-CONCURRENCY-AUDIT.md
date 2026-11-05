# Phase 2 capacity and concurrency audit

**Conclusion:** no overselling, double counting, double release or inventory-owner mismatch was reproduced in the current application's tested booking/payment/waitlist paths. The shared 25-person rule is enforced atomically when inventory is claimed. **Eleven independent capacity/concurrency scenarios passed**, in addition to the earlier independently verified 97-test suite. A separate Phase 1 comparison control reproduced 53 admissions with sequential requests when the shared rule was intentionally absent. These are separate runs, not a combined server-suite test count or a guarantee that every possible race is eliminated.

Audit requested by the owner to prioritize overselling and concurrent purchases. Clock observations: November5,2026, 00:17:44–00:23:52 America/New_York (05:17:44–05:23:52Z). Evidence timestamps: first audit completed05:22:15.7379637Z; additional scenarios05:23:38.3835440Z. Fixed deadline remains01:32:33 Eastern /06:32:33Z. No application implementation was changed for this audit.

## What must stay true

For a hayride departure, `sum(Reserved + Sold across every ticket type) <= SharedCapacity`, currently25. Each type also stays within its own capacity, and both counters stay nonnegative. Reserved inventory must equal pending orders plus open waitlist offers; Sold must equal confirmed order quantities. Each ticket counts as one person under the owner's clarification. Every line of a multi-line order must succeed together, and each payment/release transition must move inventory at most once.

Checking only sold tickets would miss concurrent checkouts. This audit includes held places before payment, preventing multiple customers from obtaining payable claims for the same remaining capacity.

## Code-path findings

- [InventoryService](../../server/EventRegistration.Api/Services/InventoryService.cs): `HoldTicketsAsync` tests type availability and the aggregate shared remainder in the **same SQL UPDATE that increments Reserved**, then checks one affected row. SQLite serializes writers; no stale read decides whether a hold succeeds. All callers run inside transactions, so earlier lines rollback if a later line fails. Public booking and waitlist promotion use this same guard.
- [OrderService](../../server/EventRegistration.Api/Services/OrderService.cs): reserves before adding the order, in one transaction. A unique request key plus rollback prevents retry-created duplicate orders/holds. Capacity is server-owned; a stale browser availability display cannot override the database predicate.
- [WaitlistService](../../server/EventRegistration.Api/Services/WaitlistService.cs): offers reserve real inventory; acceptance transfers ownership to an order without adding another hold. Offer expiry/cancellation flips an observed state and releases in the same transaction. A stale accept cannot consume a hold already reassigned to the next customer.
- [PaymentService](../../server/EventRegistration.Api/Services/PaymentService.cs): confirmation converts Reserved to Sold without increasing their sum. Only a successful guarded order-state transition moves counters. Paid orders do not release through cancellation; unknown provider results retain their holds. Checkout creation recovery keeps a stable order-based key and frozen payload.
- [AppDbContext](../../server/EventRegistration.Api/Data/AppDbContext.cs): per-type check constraints prevent negative/over-capacity counters, unique request/session indexes prevent duplicate identities, and the filtered live-seat index protects reserved seating. Shared capacity across rows is enforced by the guarded SQL rather than an aggregate CHECK constraint.
- [SchemaUpgrade](../../server/EventRegistration.Api/Data/SchemaUpgrade.cs): adds the aggregate limit and fails closed for already-overfull historical departures. Historical oversales are not silently erased, refunded or represented as fixed admissions. The previous 97-test run included upgrade/data-preservation and existing-overfull coverage.

## New independent executable evidence

Actual application services with separate real SQLite connections, deterministic fake payment gateway and disposable TestHarness databases. No BookingOperationGate or process-wide booking lock is used by the audit to make the claims safe. One case uses four hidden operating-system worker processes sharing the same temporary SQLite file; all workers ended and their exact temporary signal files were removed.

| Scenario | Observed result |
|---|---|
| Final shared place,8 waves ×16 mixed-type buyers | Exactly1 successful claim per wave; each departure ended at25. |
| Mixed-size/multi-line groups,4 waves ×20 orders | 356 admissions requested across80 orders; accepted whole orders stayed within shared/type limits and ledger matched owners. |
|16 simultaneous retries with one request key |1 order,5 held admissions. |
|12 public orders versus12 waitlist joins/promotions | Final total25; no competing order/offer duplicated the final place. FIFO fairness is a separate known defect. |
|12 offer accepts versus12 cancellations | One terminal outcome; next group held10 in the observed run, with no phantom or negative inventory. |
| Stale valid accept paused before transaction, then expiry/reassignment commits | Stale accept refused; next group retained exactly10 places. |
| Paid full departure:20 confirmation/cancellation requests plus10 new buyers |25 sold, no replacement buyers accepted; paid inventory did not release. |
| Expired unpaid departure:15 release requests plus15 replacement buyers | Release happened once;12 two-person replacement orders held24; ledger matched. |
| Provider unreadable after payment and expiry:24 competing requests |0 new successful claims; all25 places remained held safely. |
| Final place,4 processes ×4 requests | Exactly1 successful claim, total25. |
| Full purchase burst:20 simultaneous five-person buyers, duplicate checkout and confirmation requests |100 admissions requested;5 orders/5 distinct checkout sessions;25 sold,0 held after fake payment; no duplicate sales. |

[First ten scenario results](../../ai-sessions/PHASE-2/evidence/codex-review/capacity-audit.json), [complete-purchase and comparison results](../../ai-sessions/PHASE-2/evidence/codex-review/capacity-audit-additional.json), [audit source](capacity-audit/Program.cs).

Exact successful commands (PowerShell at workspace root; installed SDK permitted outside sandbox):

```powershell
$env:NUGET_SCRATCH = 'C:\Dev\project-brushfire\docs\phase-2\review-probes\.nuget-scratch'
dotnet run --project docs/phase-2/capacity-audit/CapacityAudit.csproj -- ai-sessions/PHASE-2/evidence/codex-review/capacity-audit.json
dotnet run --project docs/phase-2/capacity-audit/CapacityAudit.csproj --no-restore -- additional ai-sessions/PHASE-2/evidence/codex-review/capacity-audit-additional.json
```

Both exited0. The first result has10 passing checks; the additional result has1 capacity-safety check and1 expected comparison control. This standalone audit is not automatically discovered by `dotnet test server/BlevinsPass.slnx`. Proposed follow-up: promote the most valuable cases into the normal regression suite when implementing the reviewed corrections.

## What the comparison establishes

In an isolated fixture, remove only SharedCapacity, leaving the original type caps intact. Five **sequential** orders for Adult20 + Adult5 + Child20 + Child5 + Wagon3 allocate53 while every individual cap is obeyed. This demonstrates that the old business model alone is sufficient to explain exceeding a physical25-person limit; concurrency is not required. It does **not** establish the actual historical customer's root cause, because no customer transaction logs or original confirmation were supplied/audited. Current code must enforce both the correct aggregate rule and atomic claims; the audit supports both.

## Remaining races and disposition

The [earlier independent review](../../ai-sessions/PHASE-2/codex-review-001.md) still stands:

1. Public eligibility checked before its transaction can let a buyer bypass a newly committed waiting group. **Queue-priority race; no over25 result was reproduced.**
2. Concurrent capacity increases can stale-overwrite40 with30. **Capacity-management race.** Its write-time commitments guard still rejects a limit below existing held/sold inventory; the reproduced case lowered the configured limit without overselling. Monotonicity nevertheless needs correction.
3. Checkout retry recovery and queue progression on status refresh remain functional defects. They do not create additional admissions in the audited scenarios, but remain relevant to a usable waitlist purchase flow.

Proposed priority: fix and regression-test the queue eligibility and capacity-update races first, preserve the now-audited inventory/payment invariants, then finish the two waitlist recovery defects. Owner requested proposal before implementation; [correction proposal](CORRECTION-PROPOSAL.md) remains pending review. No new row-locking/concurrency mechanism is proposed solely on the unsupported support diagnosis.

## Evidence limits and closeout

These are finite, repeatable service-level concurrency cases with real SQLite, including a multiprocess case, not a distributed-production certification or HTTP load test. Payments are fake-provider simulations; no real Stripe API was called and no claim is made that a real waitlist test purchase passed. Historical customer data, real refund/reset and the owner's app database were not touched by this audit. Legacy over-capacity departures need explicit owner resolution; preventing new oversales does not reduce already-issued tickets.

Initial97-test, lint/build and timezone results remain recorded separately in [review001](../../ai-sessions/PHASE-2/codex-review-001.md). No source application changes, Git operations, deployments or external account changes were performed. Usage: **Not available**. Owner manual acceptance, full transcripts/final raw archive and owner PR/submission remain pending.
