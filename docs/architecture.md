# Architecture and decisions

```text
Browser (React SPA, :5173) --/api proxy--> ASP.NET Core API (:5050) --EF Core--> blevinspass.db (SQLite)
        |                                          |
        +---- redirect to Stripe-hosted Checkout <-+-- Stripe API (test mode, server-side key)
```

## Decisions

| Decision | Reason |
|---|---|
| Controllers + a few focused services (Order, Inventory, Payment, Catalog) | Readable without a framework; no repositories/CQRS for a 10-table demo |
| `ICheckoutGateway` around Stripe | Lets tests run every provider outcome (rejected, timeout, lost response, paid, expired) against real SQLite; the app always uses real Stripe |
| Guarded `ExecuteUpdate` (`WHERE Capacity - Reserved - Sold >= qty`) + affected-row check | SQLite has no rowversion; a conditional UPDATE serializes correctly under concurrent writers. Read-then-save would oversell |
| Filtered unique index on live seat claims | The database itself prevents double-booking a seat per performance, while released history stays for audit |
| Hold first, then call Stripe outside the transaction | Never hold a DB lock across the network; the order exists before any payable session can |
| One Stripe session per order, idempotency key `blevinspass-order-{id}-checkout`, payload frozen on first attempt | A retry after a timeout returns the same session instead of creating a second chargeable one |
| 31-minute session expiry, release only on Stripe-confirmed expiry | Stripe's minimum is 30 min; a local timer alone can't prove a session is unpayable |
| No webhooks, no scheduler; reconcile on return and before availability/holds | Allowed by the assignment and much simpler; documented limitation |
| `EnsureCreated` + idempotent seed | Disposable local demo; manual reset documented |
| Context + `useReducer` as the only shared client store; theme lives in it | Mandated; one place to reason about selection/theme interaction |
| CSS variables keyed by `data-theme` | Themes share every component; seat status colors are fixed across themes |
| Custom calendar and SVG seat map, no UI libraries | Small, accessible, no extra dependencies |
| Fixed loopback ports and Vite proxy | No CORS or HTTPS-redirect issues; Stripe return URL is deterministic |

Business rules are specified once in [contracts/booking-rules.md](contracts/booking-rules.md).

## Seasonal presentation (UI-001)

The owner's redesign brief extends the original color-only theme proposal: shared components now support distinct seasonal hero compositions, font stacks, imagery and surface styling through the same `data-theme` attribute. No additional mutable theme store, route, backend dependency or payment change was introduced. [DESIGN_SYSTEM.md](../DESIGN_SYSTEM.md) is the canonical visual reference; the booking contract remains authoritative for behavior.
