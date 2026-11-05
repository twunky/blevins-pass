using System.Data.Common;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventRegistration.Tests;

/// <summary>
/// Regressions for Codex review 001. The two race tests force the exact reviewed interleaving: one request
/// is paused on its own SQLite connection just before its database transaction starts, a competing request
/// commits, then the paused request resumes.
/// </summary>
public class ReviewCorrectionTests
{
    private const string Hayride = "Pumpkin Patch Hayride";

    /// <summary>Pauses the first transaction started on its connection until released.</summary>
    private sealed class PauseFirstTransaction : DbTransactionInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref paused, 1) == 0)
            {
                Reached.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private static (AppDbContext Db, OrderService Orders, WaitlistService Waitlist) PausedServices(TestHarness harness, PauseFirstTransaction pause)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={harness.DatabasePath};Default Timeout=30;Pooling=False")
            .AddInterceptors(pause).Options);
        var inventory = new InventoryService(db);
        var checkout = Options.Create(new CheckoutOptions());
        var payments = new PaymentService(db, inventory, harness.Gateway, harness.Clock, checkout, NullLogger<PaymentService>.Instance);
        var waitlist = new WaitlistService(db, inventory, harness.Clock, Options.Create(harness.WaitlistOptions), checkout, NullLogger<WaitlistService>.Instance);
        return (db, new OrderService(db, inventory, payments, waitlist, harness.Clock, checkout), waitlist);
    }

    private static async Task<(TestHarness Harness, int SlotId, int Adult, int Child)> HayrideAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        return (harness, slotId, inventory.Single(i => i.TicketType!.Name == "Adult").TicketTypeId, inventory.Single(i => i.TicketType!.Name == "Child").TicketTypeId);
    }

    private static async Task<int> LivePeopleAsync(TestHarness harness, int slotId)
    {
        await using var db = harness.CreateContext();
        return await db.SlotInventories.Where(i => i.TimeSlotId == slotId).SumAsync(i => i.Reserved + i.Sold);
    }

    // ---- 1. FIFO eligibility is atomic with the public hold ----------------------------------

    [Fact]
    public async Task Public_buyer_paused_after_seeing_no_queue_is_refused_when_an_older_waiting_entry_commits_first()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        await harness.CreateServices().Orders.CreateOrderAsync("filler", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 4)], null), default);

        var pause = new PauseFirstTransaction();
        var paused = PausedServices(harness, pause);
        await using var pausedDb = paused.Db;
        var buyer = paused.Orders.CreateOrderAsync("public", new CreateOrderRequest(slotId, [new(child, 1)], null), default);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        WaitlistStatusDto head;
        try
        {
            // A two-person group cannot fit the last place, so it stays Waiting at the head of the line.
            (head, _) = await harness.CreateServices().Waitlist.JoinAsync("head", new JoinWaitlistRequest(slotId, "head@example.com", [new(child, 2)]), default);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        var refused = await Assert.ThrowsAsync<BookingException>(() => buyer);
        Assert.Equal("waitlist_active", refused.Code);
        Assert.Equal("Waiting", head.Status);
        Assert.Equal(24, await LivePeopleAsync(harness, slotId)); // the public request reserved nothing
        await using var db = harness.CreateContext();
        Assert.Equal(1, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Public_buyer_who_commits_before_a_later_join_keeps_the_place()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        await harness.CreateServices().Orders.CreateOrderAsync("filler", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 4)], null), default);

        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("public", new CreateOrderRequest(slotId, [new(child, 1)], null), default);
        var (late, _) = await harness.CreateServices().Waitlist.JoinAsync("late", new JoinWaitlistRequest(slotId, "late@example.com", [new(child, 1)]), default);

        Assert.Equal("PendingCheckout", order.Status);
        Assert.Equal("Waiting", late.Status);
        Assert.Equal(25, await LivePeopleAsync(harness, slotId));
    }

    // ---- 2. Capacity increases are monotonic at write time --------------------------------

    [Fact]
    public async Task Stale_shared_capacity_increase_cannot_overwrite_a_larger_committed_increase()
    {
        var (harness, slotId, _, _) = await HayrideAsync();
        await using var scope = harness;
        var pause = new PauseFirstTransaction();
        var paused = PausedServices(harness, pause);
        await using var pausedDb = paused.Db;

        var stale = paused.Waitlist.IncreaseCapacityAsync(slotId, new IncreaseCapacityRequest(30, null), default); // validated against 25
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await harness.CreateServices().Waitlist.IncreaseCapacityAsync(slotId, new IncreaseCapacityRequest(40, null), default);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        var refused = await Assert.ThrowsAsync<BookingException>(() => stale);
        Assert.Equal("capacity_changed", refused.Code);
        await using var db = harness.CreateContext();
        Assert.Equal(40, await db.TimeSlots.Where(s => s.Id == slotId).Select(s => s.SharedCapacity).SingleAsync());
    }

    [Fact]
    public async Task Stale_combined_increase_rolls_back_entirely_when_its_type_cap_is_stale()
    {
        var (harness, slotId, _, child) = await HayrideAsync();
        await using var scope = harness;
        var pause = new PauseFirstTransaction();
        var paused = PausedServices(harness, pause);
        await using var pausedDb = paused.Db;

        var stale = paused.Waitlist.IncreaseCapacityAsync(slotId, new IncreaseCapacityRequest(35, [new(child, 30)]), default);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await harness.CreateServices().Waitlist.IncreaseCapacityAsync(slotId, new IncreaseCapacityRequest(null, [new(child, 40)]), default);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        var refused = await Assert.ThrowsAsync<BookingException>(() => stale);
        Assert.Equal("capacity_changed", refused.Code);
        Assert.Equal(40, (await harness.InventoryAsync(slotId, child)).Capacity);
        await using var db = harness.CreateContext();
        // The shared part of the stale request (35) was rolled back with it.
        Assert.Equal(25, await db.TimeSlots.Where(s => s.Id == slotId).Select(s => s.SharedCapacity).SingleAsync());
    }

    // ---- 3. Accepted offers can resume the same checkout -------------------------------------

    private static async Task<(TestHarness Harness, int SlotId, Guid Reference)> NotifiedOfferAsync()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        var (filler, _) = await harness.CreateServices().Orders.CreateOrderAsync("filler", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 5)], null), default);
        var (entry, _) = await harness.CreateServices().Waitlist.JoinAsync("alice", new JoinWaitlistRequest(slotId, "alice@example.com", [new(child, 3)]), default);
        await harness.CreateServices().Payments.ConfirmAsync(filler.Id, cancel: true, default);
        await harness.CreateServices().Waitlist.PromoteAsync(slotId, default);
        await harness.CreateServices().Waitlist.MarkNotifiedAsync(entry.Reference, default);
        return (harness, slotId, entry.Reference);
    }

    [Theory]
    [InlineData(FakeCreateBehavior.UncertainNothingCreated)]
    [InlineData(FakeCreateBehavior.UncertainButCreated)]
    public async Task Failed_checkout_after_accept_can_be_resumed_with_the_same_order_and_session(FakeCreateBehavior firstAttempt)
    {
        var (harness, slotId, reference) = await NotifiedOfferAsync();
        await using var scope = harness;
        var orderId = await harness.CreateServices().Waitlist.AcceptAsync(reference, default);

        harness.Gateway.NextCreateBehavior = firstAttempt;
        var failed = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(orderId, default));
        Assert.Equal(503, failed.StatusCode);

        // After a reload the server still authorizes resuming this order (not a new acceptance).
        var status = await harness.CreateServices().Waitlist.GetStatusAsync(reference, default);
        Assert.Equal(("CheckoutStarted", false, true), (status.Status, status.CanAccept, status.CanResumeCheckout));
        Assert.Equal(orderId, status.OrderId);
        Assert.Equal(orderId, await harness.CreateServices().Waitlist.AcceptAsync(reference, default));
        Assert.Equal(3, await LivePeopleAsync(harness, slotId));

        harness.Gateway.NextCreateBehavior = FakeCreateBehavior.Succeed;
        var resumed = await harness.CreateServices().Payments.StartCheckoutAsync(orderId, default);
        var again = await harness.CreateServices().Payments.StartCheckoutAsync(orderId, default);
        Assert.Equal(resumed.CheckoutUrl, again.CheckoutUrl);
        var sessionId = (await harness.OrderAsync(orderId)).StripeSessionId!;
        Assert.EndsWith(sessionId, resumed.CheckoutUrl); // the one session for this order's idempotency key
        Assert.Equal(3, await LivePeopleAsync(harness, slotId));

        harness.Gateway.Pay(sessionId);
        await harness.CreateServices().Payments.ConfirmAsync(orderId, cancel: false, default);
        var purchased = await harness.CreateServices().Waitlist.GetStatusAsync(reference, default);
        Assert.Equal(("Purchased", false), (purchased.Status, purchased.CanResumeCheckout));
        await using var db = harness.CreateContext();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.StripeSessionId != null));
    }

    [Fact]
    public async Task Released_accepted_order_cannot_resume_checkout()
    {
        var (harness, _, reference) = await NotifiedOfferAsync();
        await using var scope = harness;
        var orderId = await harness.CreateServices().Waitlist.AcceptAsync(reference, default);
        await harness.CreateServices().Payments.ConfirmAsync(orderId, cancel: true, default);

        var status = await harness.CreateServices().Waitlist.GetStatusAsync(reference, default);
        Assert.Equal(("Lapsed", false, false), (status.Status, status.CanAccept, status.CanResumeCheckout));
        var refused = await Assert.ThrowsAsync<BookingException>(() => harness.CreateServices().Payments.StartCheckoutAsync(orderId, default));
        Assert.Equal("order_released", refused.Code);
    }

    // ---- 4. A customer's own status refresh advances the queue -------------------------------

    [Fact]
    public async Task Next_customers_own_refresh_expires_the_earlier_offer_and_receives_places()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        var (filler, _) = await harness.CreateServices().Orders.CreateOrderAsync("fill", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 5)], null), default);
        var (alice, _) = await harness.CreateServices().Waitlist.JoinAsync("first", new JoinWaitlistRequest(slotId, "first@example.com", [new(adult, 20)]), default);
        var (bob, _) = await harness.CreateServices().Waitlist.JoinAsync("second", new JoinWaitlistRequest(slotId, "second@example.com", [new(child, 10)]), default);
        await harness.CreateServices().Payments.ConfirmAsync(filler.Id, cancel: true, default);
        await harness.CreateServices().Waitlist.PromoteAsync(slotId, default);
        await harness.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference, default);
        harness.Clock.Advance(TimeSpan.FromMinutes(harness.WaitlistOptions.OfferLifetimeMinutes + 1));

        // Only Bob's private status refresh runs: no availability, outbox or explicit promotion call.
        var refreshed = await harness.CreateServices().Waitlist.GetStatusAsync(bob.Reference, default);

        Assert.Equal("OfferPendingContact", refreshed.Status);
        Assert.Equal(0, (await harness.InventoryAsync(slotId, adult)).Reserved);
        Assert.Equal(10, (await harness.InventoryAsync(slotId, child)).Reserved);
        Assert.Equal("Expired", (await harness.CreateServices().Waitlist.GetStatusAsync(alice.Reference, default)).Status);
    }
}
