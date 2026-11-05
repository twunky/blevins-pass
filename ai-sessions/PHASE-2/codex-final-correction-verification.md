# Independent final F1 correction verification

Status: **F1 independently verified; application implementation remains stopped.** Owner manual acceptance, real Stripe waitlist purchase, full transcript/final raw archive and owner PR/submission remain pending. Earlier reports remain historical evidence.

Clock observations: November5,2026,01:02:18–01:03:12 America/New_York (06:02:18–06:03:12Z), with test completion observed subsequently. Original hard deadline remains01:32:33 Eastern /06:32:33Z. Worker-reported implementation completion01:01:32 is a separate attestation.

## Checks and results

- Fresh isolated server build/test: `dotnet test server/BlevinsPass.slnx --artifacts-path C:\Users\seanb\AppData\Local\Temp\blevins-phase2-final-codex-5aba105c51544a4286cde0d1272c63bd --verbosity minimal` -> **110 passed,0 failed,0 skipped**,17s reported test duration, exit0. Workspace-local NUGET_SCRATCH was used for tooling. Restore and compile output pointed to this newly created artifacts directory, avoiding the owner's running API output and stale binaries.
- Client `npm run lint` -> exit0.
- Client `npm run build` -> exit0; TypeScript/Vite,49 modules.
- Inspected JoinAsync, CouldBuyWholeGroupNowAsync and all five WaitlistEligibilityTests. Eligibility reads and entry insertion are inside the same transaction after insertion acquires the database write lock. A refusal rolls back entry/items and adds no inventory/order/provider work. Matching request-key retries take the existing-entry path. The helper re-reads shared capacity inside the transaction. Sold-out/queued joins, request identity and both forced purchase/release interleavings are covered by the passing tests.

One fresh-build warning was observed: **EF1002** at SchemaUpgradeTests.cs:39, in the test-only schema inspection helper interpolating its table-name argument into pragma_table_info SQL. Inspected callers supply fixture table names; this is not an exposed application query. It was not changed or suppressed. Do not describe this fresh server build as zero-warning. No other warning was reported in the captured build output.

## Limits and remaining actions

No real Stripe calls, reset/refund actions, owner database opens/upgrades, or application source changes by Codex. The existing API was left alone. Claude reports it runs pre-F1 code; owner must restart their API using `dotnet run` before acceptance to rebuild/load current source. The new110-case suite covers current server code; earlier standalone capacity audit and browser recovery checks were not repeated without relevant client/payment changes.

F2 remains documented: private status and outbox reads expire/promote offers but do not reconcile abandoned accepted orders; availability/new-order requests do. F3 Development reset wording is unchanged. The fix prevents joining for an immediately buyable group; it is not general bot protection or a production abuse-prevention system.

Owner steps: restart API, manual notification/checkout walkthrough and real Stripe test purchase; record actual outcome; obtain full Claude /export and final project-specific raw JSONL (existing snapshot stops00:04); preserve this Codex chat; perform PR/optional Copilot review/submission personally. No acceptance/export completion is claimed. No Codex Git operations; Claude reports none. Usage: **Not available**.
