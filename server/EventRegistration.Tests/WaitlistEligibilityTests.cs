using System.Data.Common;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventRegistration.Tests;

/// <summary>
/// Final review F1: the waitlist must not reserve places a customer could simply buy. A new join is refused
/// with tickets_available when nobody is waiting and the whole group fits right now.
/// </summary>
public class WaitlistEligibilityTests
{
    private const string Hayride = "Pumpkin Patch Hayride";

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

    /// <summary>A waitlist service on its own connection whose first transaction pauses until released.</summary>
    private static (AppDbContext Db, WaitlistService Waitlist) PausedWaitlist(TestHarness harness, PauseFirstTransaction pause)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={harness.DatabasePath};Default Timeout=30;Pooling=False")
            .AddInterceptors(pause).Options);
        var waitlist = new WaitlistService(db, new InventoryService(db), harness.Clock, Options.Create(harness.WaitlistOptions),
            Options.Create(new CheckoutOptions()), NullLogger<WaitlistService>.Instance);
        return (db, waitlist);
    }

    private static async Task<(TestHarness Harness, int SlotId, int Adult, int Child)> HayrideAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(Hayride);
        return (harness, slotId, inventory.Single(i => i.TicketType!.Name == "Adult").TicketTypeId, inventory.Single(i => i.TicketType!.Name == "Child").TicketTypeId);
    }

    private static Task<(WaitlistStatusDto Entry, bool Created)> JoinAsync(TestHarness harness, string key, int slotId, params TicketQuantityRequest[] tickets) =>
        harness.CreateServices().Waitlist.JoinAsync(key, new JoinWaitlistRequest(slotId, $"{key}@example.com", tickets), default);

    private static async Task<(int Entries, int Live, int Orders)> CountsAsync(TestHarness harness, int slotId)
    {
        await using var db = harness.CreateContext();
        return (await db.WaitlistEntries.CountAsync(),
            await db.SlotInventories.Where(i => i.TimeSlotId == slotId).SumAsync(i => i.Reserved + i.Sold),
            await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Join_for_a_group_that_can_be_bought_now_is_refused_and_leaves_no_trace()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        await harness.CreateServices().Orders.CreateOrderAsync("partial", new CreateOrderRequest(slotId, [new(adult, 20)], null), default);

        // 5 places left: the whole departure, a mixed group and an exact fit are all buyable, so all are refused.
        foreach (var (key, tickets) in new (string, TicketQuantityRequest[])[]
        {
            ("exact", [new(child, 5)]),
            ("mixed", [new(adult, 2), new(child, 3)]),
            ("single", [new(child, 1)]),
        })
        {
            var refused = await Assert.ThrowsAsync<BookingException>(() => JoinAsync(harness, key, slotId, tickets));
            Assert.Equal(("tickets_available", 409), (refused.Code, refused.StatusCode));
        }

        Assert.Equal((0, 20, 1), await CountsAsync(harness, slotId)); // no entry, no extra hold, no new order
        Assert.Equal(0, harness.Gateway.CreateCalls);
    }

    [Fact]
    public async Task Groups_that_do_not_fit_now_and_joins_behind_people_already_waiting_are_accepted()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        await harness.CreateServices().Orders.CreateOrderAsync("fill", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 4)], null), default);

        // 1 place left: a two-person group cannot buy, so it waits at the head of the line.
        var (head, created) = await JoinAsync(harness, "pair", slotId, new TicketQuantityRequest(child, 2));
        // A single person could fit the last place, but someone is already waiting: they join behind (FIFO).
        var (behind, _) = await JoinAsync(harness, "single", slotId, new TicketQuantityRequest(child, 1));

        Assert.True(created);
        Assert.Equal(("Waiting", 1), (head.Status, head.PositionInLine));
        Assert.Equal(("Waiting", 2), (behind.Status, behind.PositionInLine));
        Assert.Equal((2, 24, 1), await CountsAsync(harness, slotId)); // nothing reserved by the joins
    }

    [Fact]
    public async Task Retry_of_an_accepted_join_returns_the_original_entry_after_places_open()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        var (filler, _) = await harness.CreateServices().Orders.CreateOrderAsync("fill", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 5)], null), default);
        var (original, created) = await JoinAsync(harness, "alice", slotId, new TicketQuantityRequest(child, 2));
        Assert.True(created);

        // Places open up; the same request retried must not be refused as "available" or duplicated.
        await harness.CreateServices().Payments.ConfirmAsync(filler.Id, cancel: true, default);
        var (retried, retryCreated) = await JoinAsync(harness, "alice", slotId, new TicketQuantityRequest(child, 2));

        Assert.False(retryCreated);
        Assert.Equal(original.Reference, retried.Reference);
        Assert.Equal(1, (await CountsAsync(harness, slotId)).Entries);

        // A NEW request with a different key for a now-buyable group is refused.
        var refused = await Assert.ThrowsAsync<BookingException>(() => JoinAsync(harness, "newcomer", slotId, new TicketQuantityRequest(adult, 1)));
        Assert.Equal("tickets_available", refused.Code);
    }

    [Fact]
    public async Task Join_paused_while_the_last_place_is_bought_is_accepted_as_waiting()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        await harness.CreateServices().Orders.CreateOrderAsync("fill", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 4)], null), default);

        var pause = new PauseFirstTransaction();
        var paused = PausedWaitlist(harness, pause);
        await using var pausedDb = paused.Db;
        // Validated while 1 place was free, then paused just before its transaction.
        var join = paused.Waitlist.JoinAsync("late", new JoinWaitlistRequest(slotId, "late@example.com", [new(child, 1)]), default);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await harness.CreateServices().Orders.CreateOrderAsync("last", new CreateOrderRequest(slotId, [new(child, 1)], null), default);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        var (entry, created) = await join;
        Assert.True(created);
        Assert.Equal("Waiting", entry.Status);
        Assert.Equal((1, 25, 2), await CountsAsync(harness, slotId));
    }

    [Fact]
    public async Task Join_paused_while_places_are_released_is_refused_because_it_could_now_buy()
    {
        var (harness, slotId, adult, child) = await HayrideAsync();
        await using var scope = harness;
        var (filler, _) = await harness.CreateServices().Orders.CreateOrderAsync("fill", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 5)], null), default);

        var pause = new PauseFirstTransaction();
        var paused = PausedWaitlist(harness, pause);
        await using var pausedDb = paused.Db;
        // Validated while sold out, then paused; meanwhile the filler order is cancelled and its places return.
        var join = paused.Waitlist.JoinAsync("eager", new JoinWaitlistRequest(slotId, "eager@example.com", [new(child, 2)]), default);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await harness.CreateServices().Payments.ConfirmAsync(filler.Id, cancel: true, default);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        var refused = await Assert.ThrowsAsync<BookingException>(() => join);
        Assert.Equal("tickets_available", refused.Code);
        Assert.Equal((0, 0, 1), await CountsAsync(harness, slotId));
        await using var db = harness.CreateContext();
        Assert.False(await db.WaitlistEntries.AnyAsync(w => w.Status == WaitlistStatus.Waiting));
    }
}
