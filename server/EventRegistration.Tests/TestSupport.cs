using System.Collections.Concurrent;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventRegistration.Tests;

/// <summary>A clock the tests can move forward.</summary>
public sealed class TestClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public enum FakeCreateBehavior
{
    Succeed,
    Reject,

    /// <summary>Throws "unknown outcome" without creating anything.</summary>
    UncertainNothingCreated,

    /// <summary>Creates the session at the provider but the response is lost.</summary>
    UncertainButCreated,
}

/// <summary>
/// Deterministic stand-in for Stripe used only by automated tests. Like Stripe, it returns the
/// same session when a creation is replayed with the same idempotency key.
/// </summary>
public sealed class FakeCheckoutGateway(TestClock clock) : ICheckoutGateway
{
    private readonly ConcurrentDictionary<string, FakeSession> sessionsByIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, FakeSession> sessionsById = new();

    public FakeCreateBehavior NextCreateBehavior { get; set; } = FakeCreateBehavior.Succeed;
    public bool FailReads { get; set; }
    public int CreateCalls;
    public long? AmountOverride { get; set; }

    /// <summary>When true, replays of an existing key are refused (simulates an ambiguous 4xx on recovery).</summary>
    public bool RejectReplays { get; set; }

    /// <summary>
    /// Awaited once, by the first creation call, after Stripe has "received" the request but before it
    /// responds. Lets tests pause one caller mid-call and run another request deterministically.
    /// </summary>
    public Func<Task>? PauseFirstCreate { get => pauseFirstCreate; set => pauseFirstCreate = value; }

    private Func<Task>? pauseFirstCreate;

    public async Task<CheckoutSessionSnapshot> CreateSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CreateCalls);
        if (sessionsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var replay))
        {
            // Like Stripe: a reused key with different parameters is an error, never a new session.
            if (RejectReplays || !replay.Request.Equals(PayloadOf(request)))
            {
                throw new CheckoutRejectedException("fake: idempotency key reused with different parameters");
            }
            return replay.ToSnapshot(AmountOverride);
        }

        switch (NextCreateBehavior)
        {
            case FakeCreateBehavior.Reject:
                throw new CheckoutRejectedException("fake rejection");
            case FakeCreateBehavior.UncertainNothingCreated:
                throw new CheckoutUncertainException("fake timeout");
        }

        var session = new FakeSession
        {
            Id = $"cs_test_{Guid.NewGuid():N}",
            OrderId = request.OrderId.ToString(),
            AmountTotal = request.LineItems.Sum(i => (long)i.UnitAmountCents * i.Quantity),
            Currency = request.Currency,
            ExpiresUtc = request.ExpiresUtc,
            Request = PayloadOf(request),
        };
        if (!sessionsByIdempotencyKey.TryAdd(request.IdempotencyKey, session))
        {
            return await CreateSessionAsync(request, cancellationToken); // lost a race with a concurrent caller: replay
        }
        sessionsById[session.Id] = session;

        var pause = Interlocked.Exchange(ref pauseFirstCreate, null);
        if (pause is not null)
        {
            await pause();
        }

        if (NextCreateBehavior == FakeCreateBehavior.UncertainButCreated)
        {
            throw new CheckoutUncertainException("fake lost response");
        }
        return session.ToSnapshot(AmountOverride);
    }

    private static string PayloadOf(CheckoutSessionRequest request) =>
        $"{request.Currency}|{request.ExpiresUtc:O}|{request.SuccessUrl}|{request.CancelUrl}|" +
        string.Join(";", request.LineItems.Select(i => $"{i.Name}:{i.UnitAmountCents}x{i.Quantity}"));

    public Task<CheckoutSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (FailReads)
        {
            throw new CheckoutUncertainException("fake read failure");
        }
        var session = sessionsById[sessionId];
        if (session.State == CheckoutSessionState.Open && !session.Paid && clock.UtcNow >= session.ExpiresUtc)
        {
            session.State = CheckoutSessionState.Expired;
        }
        return Task.FromResult(session.ToSnapshot(AmountOverride) with { IsTestMode = !LiveMode });
    }

    public Task<CheckoutSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = sessionsById[sessionId];
        if (session.State == CheckoutSessionState.Open)
        {
            session.State = CheckoutSessionState.Expired;
        }
        return Task.FromResult(session.ToSnapshot(AmountOverride));
    }

    // ---- Refunds (demo reset) ---------------------------------------------------------------

    public bool FailRefunds { get; set; }
    public bool RefundsStayPending { get; set; }

    /// <summary>Sessions whose refunds stay pending (per-order control for mixed outcomes).</summary>
    public HashSet<string> PendingRefundSessionIds { get; } = [];
    public bool LiveMode { get; set; }
    public int RefundsCreated;

    public Task<PaymentRefundSnapshot> RefundSessionPaymentAsync(string sessionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (FailRefunds)
        {
            throw new CheckoutUncertainException("fake refund failure");
        }
        var session = sessionsById[sessionId];
        lock (session)
        {
            if (session.Paid && session.RefundedCents < session.AmountTotal && !session.RefundPending)
            {
                Interlocked.Increment(ref RefundsCreated);
                session.RefundIds.Add($"re_test_{session.RefundIds.Count + 1}_{sessionId[^6..]}");
                if (RefundsStayPending || PendingRefundSessionIds.Contains(sessionId))
                {
                    session.RefundPending = true;
                }
                else
                {
                    session.RefundedCents = session.AmountTotal;
                }
            }
            return Task.FromResult(new PaymentRefundSnapshot(
                $"pi_test_{sessionId[^8..]}", !LiveMode, session.Paid ? session.AmountTotal : 0,
                session.RefundedCents, session.RefundPending, session.RefundIds.ToList()));
        }
    }

    /// <summary>Simulates a refund made earlier (e.g. by hand in the Stripe dashboard).</summary>
    public void MarkRefundedAtProvider(string sessionId)
    {
        var session = sessionsById[sessionId];
        session.RefundedCents = session.AmountTotal;
        session.RefundIds.Add("re_test_dashboard");
    }

    /// <summary>Simulates the customer completing payment on the hosted page.</summary>
    public void Pay(string sessionId, string email = "buyer@example.com")
    {
        var session = sessionsById[sessionId];
        session.Paid = true;
        session.State = CheckoutSessionState.Complete;
        session.Email = email;
    }

    private sealed class FakeSession
    {
        public required string Id { get; init; }
        public required string OrderId { get; init; }
        public long AmountTotal { get; init; }
        public required string Currency { get; init; }
        public DateTime ExpiresUtc { get; init; }
        public CheckoutSessionState State { get; set; } = CheckoutSessionState.Open;
        public bool Paid { get; set; }
        public string? Email { get; set; }
        public required string Request { get; init; }
        public long RefundedCents { get; set; }
        public bool RefundPending { get; set; }
        public List<string> RefundIds { get; } = [];

        public CheckoutSessionSnapshot ToSnapshot(long? amountOverride) => new(
            Id, $"https://checkout.stripe.test/{Id}", OrderId, State, Paid, IsTestMode: true, IsPaymentMode: true,
            amountOverride ?? AmountTotal, Currency, Email, ExpiresUtc);
    }
}

/// <summary>
/// Each test gets its own real SQLite file (not EF InMemory), seeded exactly like the app.
/// Every call to <see cref="CreateServices"/> opens a separate DbContext/connection, which lets
/// tests run genuinely concurrent requests against the same database.
/// </summary>
public sealed class TestHarness : IAsyncDisposable
{
    public string DatabasePath => databasePath;

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"blevinspass-test-{Guid.NewGuid():N}.db");
    private readonly List<AppDbContext> contexts = [];

    public TestClock Clock { get; } = new(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));
    public FakeCheckoutGateway Gateway { get; }

    private TestHarness()
    {
        Gateway = new FakeCheckoutGateway(Clock);
    }

    public static async Task<TestHarness> CreateAsync()
    {
        var harness = new TestHarness();
        await using var db = harness.CreateContext();
        await db.Database.EnsureCreatedAsync();
        await SeedData.EnsureSeededAsync(db, harness.Clock.UtcNow);
        return harness;
    }

    public AppDbContext CreateContext()
    {
        var connection = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30, Pooling = false };
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ToString()).Options);
    }

    /// <summary>Creates one "request scope". Pass a separate clock to simulate a server whose time differs.</summary>
    public BookingServices CreateServices(TestClock? clock = null)
    {
        var requestClock = clock ?? Clock;
        var db = CreateContext();
        contexts.Add(db);
        var options = Options.Create(new CheckoutOptions());
        var inventory = new InventoryService(db);
        var payments = new PaymentService(db, inventory, Gateway, requestClock, options, NullLogger<PaymentService>.Instance);
        return new BookingServices(
            db,
            new OrderService(db, inventory, payments, requestClock, options),
            payments,
            new CatalogService(db, payments, requestClock));
    }

    /// <summary>A demo reset service on its own connection, sharing the given gate with booking calls.</summary>
    public DemoResetService CreateDemoReset(BookingOperationGate gate, bool refundCompletedTestPurchases = true)
    {
        var services = CreateServices();
        return new DemoResetService(
            services.Db, services.Payments, Gateway, gate, Clock,
            Options.Create(new DemoResetOptions { Enabled = true, RefundCompletedTestPurchases = refundCompletedTestPurchases }),
            NullLogger<DemoResetService>.Instance);
    }

    // ---- Seed lookups -------------------------------------------------------------------

    public async Task<(int EventId, int SlotId, List<SlotInventory> Inventory)> GeneralAdmissionSlotAsync(string eventName, int slotIndex = 0)
    {
        await using var db = CreateContext();
        var slot = (await db.TimeSlots.AsNoTracking().Include(s => s.Event).Where(s => s.Event!.Name == eventName).ToListAsync())
            .OrderBy(s => s.StartUtc).ElementAt(slotIndex);
        var inventory = await db.SlotInventories.AsNoTracking().Include(i => i.TicketType)
            .Where(i => i.TimeSlotId == slot.Id).OrderBy(i => i.TicketTypeId).ToListAsync();
        return (slot.EventId, slot.Id, inventory);
    }

    public async Task<SlotInventory> InventoryAsync(int slotId, int ticketTypeId)
    {
        await using var db = CreateContext();
        return await db.SlotInventories.AsNoTracking().SingleAsync(i => i.TimeSlotId == slotId && i.TicketTypeId == ticketTypeId);
    }

    public async Task<(int EventId, int[] SlotIds, List<Seat> Seats)> ConcertAsync()
    {
        await using var db = CreateContext();
        var concert = await db.Events.AsNoTracking().Include(e => e.TimeSlots).SingleAsync(e => e.Name == "Winter Lights Concert");
        var seats = await db.Seats.AsNoTracking().Include(s => s.SeatingSection)
            .Where(s => s.SeatingSection!.EventId == concert.Id).OrderBy(s => s.Label).ToListAsync();
        return (concert.Id, concert.TimeSlots.OrderBy(s => s.StartUtc).Select(s => s.Id).ToArray(), seats);
    }

    public async Task<Order> OrderAsync(Guid orderId)
    {
        await using var db = CreateContext();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            try { File.Delete(databasePath + suffix); } catch (IOException) { /* best effort temp cleanup */ }
        }
    }
}

public sealed record BookingServices(AppDbContext Db, OrderService Orders, PaymentService Payments, CatalogService Catalog);
