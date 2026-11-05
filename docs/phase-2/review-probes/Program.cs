// Independent review probes. Only TestHarness temporary databases and its fake provider are used.
// These deliberately force interleavings missing from the worker's original test suite.
using System.Data.Common;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using EventRegistration.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

await PublicBuyerRace();
await CapacityIncreaseRace();
await StatusExpiry();

static (AppDbContext Db, OrderService Orders, WaitlistService Waitlist) PausedServices(TestHarness h, PauseTransaction pause)
{
    var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={h.DatabasePath};Default Timeout=30;Pooling=False")
        .AddInterceptors(pause).Options);
    var inventory = new InventoryService(db);
    var checkout = Options.Create(new CheckoutOptions());
    var payments = new PaymentService(db, inventory, h.Gateway, h.Clock, checkout, NullLogger<PaymentService>.Instance);
    var waitlist = new WaitlistService(db, inventory, h.Clock, Options.Create(h.WaitlistOptions), checkout, NullLogger<WaitlistService>.Instance);
    return (db, new OrderService(db, inventory, payments, waitlist, h.Clock, checkout), waitlist);
}

static async Task PublicBuyerRace()
{
    await using var h = await TestHarness.CreateAsync();
    var (_, slot, inventory) = await h.GeneralAdmissionSlotAsync("Pumpkin Patch Hayride");
    int Id(string name) => inventory.Single(i => i.TicketType!.Name == name).TicketTypeId;
    await h.CreateServices().Orders.CreateOrderAsync("review-filler", new(slot, [new(Id("Adult"),20),new(Id("Child"),4)],null),default);
    var pause = new PauseTransaction();
    var services = PausedServices(h, pause);
    await using var context = services.Db;
    var buyer = services.Orders.CreateOrderAsync("review-public",new(slot,[new(Id("Child"),1)],null),default);
    await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    try
    {
        var (entry, _) = await h.CreateServices().Waitlist.JoinAsync("review-head",new(slot,"review@example.com",[new(Id("Child"),2)]),default);
        if (entry.Status != "Waiting") throw new Exception("Probe setup: group should not fit the last place.");
    }
    finally { pause.Resume.TrySetResult(); }
    try
    {
        var result = await buyer;
        Console.WriteLine($"PUBLIC_FIFO_RACE: new public order succeeded ({result.Order.Status}) after older waiting entry committed; expected waitlist_active conflict.");
    }
    catch (BookingException e) { Console.WriteLine($"PUBLIC_FIFO_RACE: refused with {e.Code}."); }
}

static async Task CapacityIncreaseRace()
{
    await using var h = await TestHarness.CreateAsync();
    var (_, slot, _) = await h.GeneralAdmissionSlotAsync("Pumpkin Patch Hayride");
    var pause = new PauseTransaction();
    var services = PausedServices(h, pause);
    await using var context = services.Db;
    var stale = services.Waitlist.IncreaseCapacityAsync(slot,new(30,null),default);
    await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    try { await h.CreateServices().Waitlist.IncreaseCapacityAsync(slot,new(40,null),default); }
    finally { pause.Resume.TrySetResult(); }
    try { await stale; }
    catch (BookingException e) { Console.WriteLine($"CAPACITY_INCREASE_RACE: stale increase refused with {e.Code}."); }
    await using var db = h.CreateContext();
    var actual = await db.TimeSlots.Where(s => s.Id == slot).Select(s => s.SharedCapacity).SingleAsync();
    Console.WriteLine($"CAPACITY_INCREASE_RACE: capacity after concurrent 30/40 requests = {actual}; expected 40, never a decrease.");
}

static async Task StatusExpiry()
{
    await using var h = await TestHarness.CreateAsync();
    var (_, slot, inventory) = await h.GeneralAdmissionSlotAsync("Pumpkin Patch Hayride");
    int Id(string name) => inventory.Single(i => i.TicketType!.Name == name).TicketTypeId;
    var filler = (await h.CreateServices().Orders.CreateOrderAsync("review-fill",new(slot,[new(Id("Adult"),20),new(Id("Child"),5)],null),default)).Order;
    var alice = (await h.CreateServices().Waitlist.JoinAsync("review-first",new(slot,"first@example.com",[new(Id("Adult"),20)]),default)).Entry;
    var bob = (await h.CreateServices().Waitlist.JoinAsync("review-second",new(slot,"second@example.com",[new(Id("Child"),10)]),default)).Entry;
    await h.CreateServices().Payments.ConfirmAsync(filler.Id,true,default);
    await h.CreateServices().Waitlist.PromoteAsync(slot,default);
    await h.CreateServices().Waitlist.MarkNotifiedAsync(alice.Reference,default);
    h.Clock.Advance(TimeSpan.FromMinutes(h.WaitlistOptions.OfferLifetimeMinutes+1));
    var refreshed = await h.CreateServices().Waitlist.GetStatusAsync(bob.Reference,default);
    await using var db = h.CreateContext();
    var live = await db.SlotInventories.Where(i => i.TimeSlotId == slot).SumAsync(i => i.Reserved+i.Sold);
    Console.WriteLine($"STATUS_EXPIRY: next customer's own refresh = {refreshed.Status}; live places held = {live}; expected OfferPendingContact with 10 reserved.");
}

sealed class PauseTransaction : DbTransactionInterceptor
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;
    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
        TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref paused,1) == 0)
        {
            Reached.TrySetResult();
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20),cancellationToken);
        }
        return result;
    }
}
