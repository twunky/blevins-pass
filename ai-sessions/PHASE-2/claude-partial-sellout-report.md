# Phase 2 — partial-sellout / per-type queue policy report (Claude Code)

Same local session `77bd3fc9-4329-4518-bc22-3caa875e473e` (Claude Opus 5.5). Earlier reports are preserved unchanged. Scope: the owner-approved partial-sellout correction and queue-policy change only. Audience: owner, for relay to Codex.

## Clock (America/New_York, shell `date`)

| Point | Time |
|---|---|
| Instruction received / first clock read | 01:15:02 |
| Server changes building | 01:16:40 |
| Client lint/build passing | 01:17:13 |
| Focused tests 4/4 | 01:18:15 |
| Full suite 114/114 | 01:18:43 |
| Rendered partial-sellout check passed | 01:19:49 |
| **Last application/doc change** | **01:21:06**, before the 01:22:33 application freeze (not extended) |
| This report | 01:21:39, before the 01:32:33 hard stop |

## Behavior implemented

Two policies, chosen from the departure's `SharedCapacity` in the database:
- **Shared-capacity departures (hayride):** unchanged. The queue is departure-wide FIFO, groups can mix ticket types, the aggregate limit applies, and public sales are closed while anyone is `Waiting`.
- **Independent-capacity departures (`SharedCapacity` null, e.g. Orchard Cider Tasting):** FIFO per ticket type. A waiting entry competes only for the types it contains.

Where each rule lives:
- **Public hold guard** (`InventoryService.HoldTicketsAsync`): the `NOT EXISTS` predicate inside the same guarded UPDATE now matches a waiting entry only if the departure is shared **or** the entry requests that line's ticket type. It is still atomic with the reservation, and a multi-line order rolls back entirely.
- **Promotion** (`WaitlistService.PromoteAsync`): processes waiting entries in id order. Shared departures stop at the first entry that doesn't fit, as before. Independent departures put a non-fitting entry's types into a "blocked" set and skip later entries that overlap it. A multi-type legacy entry blocks all of its types, so overlapping requests cannot bypass it, and it is still held all-or-nothing in one transaction (guarded hold + guarded flip).
- **F1 eligibility and position** (`CouldBuyWholeGroupNowAsync`, `GetStatusAsync`): use the same "competing waiting" definition (`CompetingWaiting`). F1 still runs inside the join transaction after the insert, and idempotent retries still return the original entry.
- **New requests on independent departures name exactly one ticket type** (400 otherwise). Existing multi-type entries are retained.
- **Availability masking** (`CatalogService`): shared departures as before. Independent departures show 0 only for types with waiting entries. A new per-type `waitlistWaiting` field is added to each ticket type.
- **UI** (`TicketSelector`, `WaitlistJoin`): on independent departures, each unavailable type shows its own "Join {type} waitlist" form under its row. With a complete sellout, every type gets one. Shared departures keep the single form. No form on closed/past departures (`isBookable`); the server still rejects them (`slot_closed`).
- **Unchanged:** notification gate, explicit purchase, no provider call on join, same-order/session resume, expiry/release guards, duplicate protection, offer duration, F2/F3.

## Changed files

- Server: `Services/InventoryService.cs`, `Services/WaitlistService.cs`, `Services/CatalogService.cs`, `DTOs/EventDtos.cs`.
- Tests: `PartialSelloutTests.cs` (new, 4 cases).
- Client: `features/TicketSelector.tsx`, `features/WaitlistJoin.tsx` (optional title, unique heading id), `types/api.ts`, `theme/theme.css` (row wrap for the per-type form).
- Docs: `docs/contracts/booking-rules.md` (stale pre-F1 paragraph replaced; queue-policy and F1 wording), `docs/contracts/api-contract.md`, `docs/phase-2/WAITLIST-GUIDE.md`, `README.md` (FIFO bullet), `docs/phase-2/WRITE-UP.md` (one policy clause; 495 words).
- Evidence: `ai-sessions/PHASE-2/evidence/ui-partial-sellout/` (`results.json`, 2 screenshots).

## Checks and results

| Check | Result |
|---|---|
| `dotnet build server/BlevinsPass.slnx --artifacts-path <scratchpad>/artifacts2` (normal output is locked by the owner's running API) | succeeded |
| `dotnet test … --filter PartialSelloutTests` | **4 passed**, 0 failed, 0 skipped |
| `dotnet test server/BlevinsPass.slnx --artifacts-path … --no-build` | **114 passed, 0 failed, 0 skipped** (110 existing + 4 new; all earlier correction, F1, idempotency, shared-capacity and FIFO regressions included) |
| `npx eslint .` / `npm run build` (client) | exit 0 / succeeded |
| Rendered check: headless Chrome, scratch script; my own API on 5051 built from those artifacts with a fresh scratch fixture DB and a non-test Stripe key (no Stripe call); temporary Vite on 5174 with a scratchpad-only config | passed at 1440 and 390: VIP sold out, "Join VIP waitlist" under VIP only (no GA form); VIP request joined; after a reload GA was still selectable; a real-API GA order then returned `PendingCheckout` with VIP `waitlistWaiting = 1`; overflow 0; 0 page errors |

New tests:
- VIP waiter doesn't block GA: GA order succeeds; masking and per-type waiting are correct; VIP jump refused; GA join refused `tickets_available`; two-type request refused 400; 0 provider calls.
- Competing VIP FIFO, including head-of-line within the type, with GA still purchasable.
- A legacy GA+VIP entry blocks GA sales and keeps a later GA-only request behind it; after VIP frees, it is offered all-or-nothing (GA 3 = 2 + 1, VIP 1) and then the GA-only request.
- Complete sellout gives one request per type; closed departure is rejected; 0 provider calls.

The Phase 2 hayride/shared suites (departure-wide FIFO, aggregate limit, races, F1, resume, refresh) all still pass inside the 114.

## Limitations

- Codex has not verified this pass, and there is no owner acceptance or real Stripe purchase for it.
- The API on port 5050 and the client on 5173 are the owner's processes and were left running. They use code built before this change; restart them (`dotnet run`, `npm run dev`) to see it. The normal `bin` folder was not rebuilt because it is locked.
- The rendered check script lives only in the scratchpad (`partial-check.mjs`), not in the repo. Its results and screenshots are in the evidence folder.
- On shared departures, a group that can't fit the remaining places still can't join from the UI unless a queue exists (unchanged policy).
- Strict per-type FIFO can leave places of a type unused behind a larger request; a legacy multi-type entry can hold back all of its types.
- The capacity/concurrency audit was not re-run. The new predicates only widen when public sales and promotion may proceed on independent departures; aggregate and type limits are still enforced by the same guarded UPDATE.

## Acceptance status and owner actions

- **Not accepted.** No manual or real-Stripe acceptance is claimed.
- Optional: Codex verification of this pass.
- Manual check after restarting: Orchard Cider Tasting, sell out VIP (2), join the VIP waitlist, confirm GA can still be bought, then run the notification/purchase walkthrough with test card 4242 4242 4242 4242.
- **Transcript:** in this session run `/export` and save it as `C:\Dev\project-brushfire\ai-sessions\PHASE-2\claude-session.txt`. After ending the session, copy `%USERPROFILE%\.claude\projects\c--Dev-project-brushfire\77bd3fc9-4329-4518-bc22-3caa875e473e.jsonl` into `ai-sessions\PHASE-2\raw\`. The existing snapshot stops at 00:04. Also export the Codex chat.
- PR, optional Copilot review and submission: owner only.

Implementation stops after this pass. No Git operations, agents or sessions, dependencies, real Stripe calls, resets or refunds; the owner's live database and processes were not touched; my temporary servers (5051, 5174) were stopped. Usage: **Not available**.
