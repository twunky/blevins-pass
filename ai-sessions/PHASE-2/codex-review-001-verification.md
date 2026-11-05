# Independent verification of review001 corrections

Status: **all four reviewed corrections verified at code/automated-check level**. Owner manual acceptance, real Stripe waitlist test purchase, full transcripts/final raw archive and owner PR/submission remain pending. This is not owner acceptance or evidence of a real payment.

Clock observations: November5,2026,00:42:45–00:44:14 America/New_York (05:42:45–05:44:14Z). Original hard stop remains01:32:33 Eastern /06:32:33Z. Worker reported completion00:41:51; that is worker-reported, distinct from these observations.

## Independent checks

| Check | Result |
|---|---|
| `dotnet test server/BlevinsPass.slnx --no-restore --verbosity minimal` | **105 passed,0 failed,0 skipped**, standard runner,17s reported test duration, exit0 |
| Client `npm run lint` | exit0 |
| Client `npm run build` | exit0; TypeScript/Vite,49 modules |
| Original unchanged backend review probes: `dotnet run --project docs/phase-2/review-probes/ReviewProbes.csproj --no-restore` | exit0; public race refused `waitlist_active`; stale capacity update refused `capacity_changed`, final40; private status refresh offered Bob10 places |
| Independent headless Chrome recovery check against newly built client | first mocked checkout503; Resume visible before/after reload; one accept, two checkout requests for same order; successful second mocked local redirect;390px,Asia/Tokyo,0 page errors |

Original backend probes ran with workspace-local NUGET_SCRATCH, only temporary SQLite and fake payments. Their historical failure output/source is preserved; the rerun now reports corrected outcomes. [Browser result](evidence/codex-review/checkout-resume.json), [screenshot](evidence/codex-review/checkout-resume-after-reload.png), [browser probe](../../docs/phase-2/review-probes/check-resume.mjs).

Code inspection confirmed public waitlist eligibility is inside the guarded inventory UPDATE; shared/type capacity monotonicity is checked at write time with transaction rollback; resume authorization is limited to accepted entries with pending orders; status reads promote without releasing accepted orders outside payment rules. Reviewed eight new regression cases covering exact SQLite interleavings, stale combined-request rollback, uncertain-session recovery and terminal-order behavior.

## Limits and disposition

The independent browser check uses controlled API fixtures, not a real API or Stripe call. The105-test run independently exercises fake-provider same-session recovery. Claude's real-server503/browser-second-response-mocked check is separate worker-reported evidence. The previous eleven capacity/concurrency audit scenarios were not rerun after these corrections; relevant shared-capacity/payment tests passed in the105-case suite, and the three exact defect probes were rerun.

Findings1–4 in codex-review-001.md are closed for the inspected code and verified scenarios. Historical reports/proposal/audit remain unchanged evidence of prior states. No further application fixes are proposed by this review. No application code, real database, reset/refund or provider state was changed by Codex. No Codex Git operations; worker likewise reports none. Usage: **Not available**.
