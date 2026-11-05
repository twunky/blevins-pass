# Phase 2 write-up: feedback from the field

## What the problems actually were

**Hayride overcrowding: most likely a missing rule, not a race.** This conclusion comes from the code and seed configuration; I had no customer logs. Each departure had three independent caps (Adult 25, Child 25, Wagon 3) and no total, so 53 people could be sold onto a 25-person ride. That matches Marcus's report that every type stopped at its own limit. Holds already used guarded conditional UPDATEs, and a sequential comparison reproduces 53 without any concurrency. Row locking would not have helped.

**Waitlist: a missing feature.** Dana wants to keep latecomers without charging them.

**"Wrong time": not reproduced.** Times are stored in UTC and shown in America/New_York. Tests cover DST changes and a venue evening on the next UTC day, and purchasers from UTC+14 to UTC−11 see identical output. I invented no fix, but times now show EST/EDT.

## Questions and assumptions

- **Owner:** each ticket, Wagon included, is one person; 25 per departure; type caps stay. The old wagon text is updated for future bookings only.
- **Owner:** first come, first served; payment only after notification and an explicit purchase. *Owner-approved:* strict FIFO per hayride departure; per ticket type elsewhere.
- **Owner:** local outbox; messages are sent by hand. *Assumption:* 24-hour offers, restarted on notification.

## What I built

- A shared per-departure limit inside the same guarded UPDATE as the type cap. Concurrent mixed orders cannot oversell, orders are all-or-nothing, and already-overfull departures accept nothing new (history untouched).
- A waitlist: idempotent free joins (refused when the tickets could simply be bought), a private status link, FIFO offers that reserve real capacity, a manual-notification gate, expiry/cancel that returns places exactly once, and an accept that becomes an ordinary Stripe order. Customers can resume an interrupted checkout of the same order.
- A Development-only operator outbox and a guarded capacity increase.
- An explicit, backed-up, idempotent database upgrade.

**Review corrections.** Codex found that a public buyer could pass the "nobody waiting" check just before a waitlist join committed. That was a queue-priority race; no over-capacity sale occurred. It also found stale capacity edits, a lost checkout retry, and status refreshes that didn't advance the queue. All four are fixed and Codex-verified. My final self-review found direct API joins could reserve tickets still on sale; now refused. 114 server tests passed independently. Limit: status refreshes don't release abandoned accepted orders; availability or order requests do.

## Deliberately not built

Email/SMS, authentication or production admin, event editing, a scheduler, webhooks, refunds of historical oversales, and best-fit queue skipping.

## Pushback

- The AI's race diagnosis was confident but unsupported; the clear gap was the business rule.
- "Timezone bug" needs the actual confirmation before anyone changes date code.
- Strict FIFO can leave places empty behind a large group. That is Dana's policy choice.
- The operator page is a local demo, not production tooling.

