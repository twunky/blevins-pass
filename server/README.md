# Server — ASP.NET Core (.NET 10) + EF Core SQLite

```powershell
dotnet build BlevinsPass.slnx
dotnet test BlevinsPass.slnx
cd EventRegistration.Api
dotnet user-secrets set "Stripe:SecretKey" "sk_test_REPLACE_ME"   # or $env:Stripe__SecretKey
dotnet run                                                        # http://127.0.0.1:5050
```

| Path | Responsibility |
|---|---|
| `Program.cs` | DI, SQLite path anchored to the project folder, EnsureCreated + seed, error middleware |
| `Models/` | Event, TimeSlot, TicketType, SlotInventory, SeatingSection, Seat, SeatReservation, Order, OrderItem, SiteSettings |
| `Data/AppDbContext.cs` | Unique indexes, check constraints, the filtered "live seat claim" index, UTC conversion |
| `Data/SeedData.cs` | Idempotent seed: 5 GA events + Winter Lights Concert (18 seats) |
| `Services/OrderService.cs` | Validate, price and hold new orders; request idempotency |
| `Services/InventoryService.cs` | Guarded conditional updates and seat claims (run inside the caller's transaction) |
| `Services/PaymentService.cs` | Checkout session creation/recovery, server verification, reconciliation, idempotent confirm/release |
| `Services/StripeCheckoutGateway.cs` | The only Stripe-specific code (`ICheckoutGateway`) |
| `Services/CatalogService.cs` | Events, availability, seating, theme |
| `Controllers/` | Thin HTTP layer; see [API contract](../docs/contracts/api-contract.md) |
| `EventRegistration.Tests/` | xUnit on real temp SQLite files with a deterministic fake gateway |

Configuration (`appsettings.json`): `Checkout:ClientBaseUrl` (Stripe return origin), `Checkout:SessionLifetimeMinutes` (31), `Checkout:PreCheckoutHoldMinutes` (15). `Stripe:SecretKey` must come from user-secrets or the `Stripe__SecretKey` environment variable; only `sk_test_`/`rk_test_` keys are accepted.
