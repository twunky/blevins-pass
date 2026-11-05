// Independent audit executable. Uses only disposable SQLite files and FakeCheckoutGateway.
// No application implementation is changed, and no Stripe/reset/refund endpoint is called.
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using EventRegistration.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

if (args.Length > 0 && args[0] == "worker")
{
    var database = args[1]; var slot = int.Parse(args[2]); var type = int.Parse(args[3]); var worker = args[4];
    File.WriteAllText(database + $".ready-{worker}", "ready");
    var until = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(database + ".go"))
    {
        if (DateTime.UtcNow > until) throw new TimeoutException("Worker start signal missing.");
        await Task.Delay(20);
    }
    var scopes = Enumerable.Range(0,4).Select(_ => new AuditScope(database,new TestClock(TestHarness.DefaultStartUtc))).ToArray();
    try
    {
        var results = await Race(scopes.Select((s,i) => (Func<Task<string>>)(() => Attempt(async () =>
            { await s.Orders.CreateOrderAsync($"process-{worker}-{i}",new(slot,[new(type,1)],null),default); }))).ToArray());
        Console.WriteLine(JsonSerializer.Serialize(results));
    }
    finally { foreach(var scope in scopes) await scope.DisposeAsync(); }
    return;
}

var checks = new List<object>(); var failures = 0;
if (args.Length > 0 && args[0] == "additional")
{
await Run("concurrent complete purchases and duplicate confirmation", CompletePurchases);
await Run("Phase 1 model control: sequential requests can allocate 53", PhaseOneControl);
}
else
{
await Run("final shared place: mixed ticket types, 8 waves x 16 buyers", LastPlace);
await Run("mixed-size and multi-line simultaneous groups", MixedGroups);
await Run("same request key: 16 simultaneous retries", DuplicateRequests);
await Run("public orders competing with waitlist joins and promotions", PublicAndOffers);
await Run("notified offer: concurrent accepts versus cancellations", AcceptCancel);
await Run("stale accept loses to expiry and capacity reassignment", AcceptExpiry);
await Run("paid checkout: confirmations, cancellations and new claims", PaidRaces);
await Run("expired unpaid checkout: duplicate releases and new claims", ExpiredCheckout);
await Run("unreachable provider after payment: inventory stays held", ProviderUncertainty);
await Run("last shared place across 4 operating-system processes", CrossProcess);
}
var report = new { checkedAtUtc=DateTime.UtcNow.ToString("O"),passed=checks.Count-failures,failed=failures,
    evidence="Actual application services, isolated real SQLite and fake payments; no HTTP load or real provider",checks };
var serialized = JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(serialized);
var outputPath=args.Length>1 && args[0]=="additional"?args[1]:args.Length==1?args[0]:null;
if(outputPath is not null) await File.WriteAllTextAsync(outputPath,serialized);
Environment.ExitCode=failures == 0 ? 0 : 1;

async Task Run(string name,Func<Task<object>> check)
{
    try { var evidence=await check();checks.Add(new{name,status="passed",evidence}); }
    catch(Exception e) {failures++;checks.Add(new{name,status="failed",error=e.ToString()});}
}

static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
static async Task<string> Attempt(Func<Task> operation)
{
    try {await operation();return "ok";}
    catch(BookingException e) {return e.Code;}
}
static async Task<T[]> Race<T>(IReadOnlyList<Func<Task<T>>> operations)
{
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var tasks=operations.Select(operation=>Task.Run(async()=>{await start.Task;return await operation();})).ToArray();
    start.SetResult();return await Task.WhenAll(tasks);
}
static async Task<(int Slot,int Adult,int Child,int Wagon)> Ride(TestHarness h)
{
    var (_,slot,rows)=await h.GeneralAdmissionSlotAsync("Pumpkin Patch Hayride");
    int Id(string name)=>rows.Single(r=>r.TicketType!.Name==name).TicketTypeId;
    return(slot,Id("Adult"),Id("Child"),Id("Family Wagon"));
}
static async Task<OrderDto> Fill(TestHarness h,int slot,int adult,int child,int children=5)
    =>(await h.CreateServices().Orders.CreateOrderAsync("audit-filler",new(slot,[new(adult,20),new(child,children)],null),default)).Order;

// Read after all competing operations settle: reconcile each inventory row against its owners.
static async Task<int> Ledger(TestHarness h,int slot)
{
    await using var db=h.CreateContext();
    var inventory=await db.SlotInventories.AsNoTracking().Where(i=>i.TimeSlotId==slot).ToListAsync();
    var orders=await db.Orders.AsNoTracking().Include(o=>o.Items).Where(o=>o.TimeSlotId==slot).ToListAsync();
    var offers=await db.WaitlistEntries.AsNoTracking().Include(w=>w.Items).Where(w=>w.TimeSlotId==slot
        && (w.Status==WaitlistStatus.Offered || w.Status==WaitlistStatus.Notified)).ToListAsync();
    foreach(var row in inventory)
    {
        var expectedHeld=orders.Where(o=>o.Status is OrderStatus.PendingCheckout or OrderStatus.PendingPayment)
            .SelectMany(o=>o.Items).Where(i=>i.TicketTypeId==row.TicketTypeId).Sum(i=>i.Quantity)
            +offers.SelectMany(w=>w.Items).Where(i=>i.TicketTypeId==row.TicketTypeId).Sum(i=>i.Quantity);
        var expectedSold=orders.Where(o=>o.Status==OrderStatus.Confirmed).SelectMany(o=>o.Items)
            .Where(i=>i.TicketTypeId==row.TicketTypeId).Sum(i=>i.Quantity);
        Require(row.Reserved==expectedHeld && row.Sold==expectedSold,"Inventory no longer matches active order/offer owners.");
        Require(row.Reserved>=0 && row.Sold>=0 && row.Reserved+row.Sold<=row.Capacity,"Ticket-type capacity violated.");
    }
    var total=inventory.Sum(i=>i.Reserved+i.Sold);
    var limit=await db.TimeSlots.Where(s=>s.Id==slot).Select(s=>s.SharedCapacity).SingleAsync();
    Require(limit is null || total<=limit,"Shared departure capacity violated.");
    return total;
}

static async Task<object> LastPlace()
{
    var winners=0;
    for(var wave=0;wave<8;wave++)
    {
        await using var h=await TestHarness.CreateAsync();var r=await Ride(h);await Fill(h,r.Slot,r.Adult,r.Child,4);
        var operations=Enumerable.Range(0,16).Select(i=>{
            var s=h.CreateServices();var type=i%3==0?r.Adult:i%3==1?r.Child:r.Wagon;
            return(Func<Task<string>>)(()=>Attempt(async()=>{await s.Orders.CreateOrderAsync($"last-{i}",new(r.Slot,[new(type,1)],null),default);}));
        }).ToArray();
        var results=await Race(operations);Require(results.Count(x=>x=="ok")==1,"Exactly one final-place buyer must succeed.");
        Require(results.All(x=>x is "ok" or "insufficient_capacity"),"Unexpected final-place request result.");
        Require(await Ledger(h,r.Slot)==25,"The final place was not conserved.");winners++;
    }
    return new{rounds=8,competingRequests=128,successfulFinalClaims=winners,finalPeoplePerRound=25};
}

static async Task<object> MixedGroups()
{
    var success=0;var attemptedPeople=0;
    for(var wave=0;wave<4;wave++)
    {
        await using var h=await TestHarness.CreateAsync();var r=await Ride(h);
        var operations=Enumerable.Range(0,20).Select(i=>{
            var s=h.CreateServices();var adults=i%4+1;var children=i%3+1;attemptedPeople+=adults+children;
            return(Func<Task<string>>)(()=>Attempt(async()=>{await s.Orders.CreateOrderAsync($"mixed-{i}",new(r.Slot,[new(r.Adult,adults),new(r.Child,children)],null),default);}));
        }).ToArray();
        var results=await Race(operations);success+=results.Count(x=>x=="ok");
        Require(results.All(x=>x is "ok" or "insufficient_capacity"),"Unexpected mixed-group result.");
        await Ledger(h,r.Slot);
    }
    return new{rounds=4,competingOrders=80,attemptedPeople,successfulWholeOrders=success,ledgerMatchesOwners=true};
}

static async Task<object> CompletePurchases()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);
    var requests=Enumerable.Range(0,20).Select(i=>{var s=h.CreateServices();return(Func<Task<Guid?>>)(async()=>{
        try{return(await s.Orders.CreateOrderAsync($"purchase-{i}",new(r.Slot,[new(r.Adult,2),new(r.Child,3)],null),default)).Order.Id;}
        catch(BookingException e)when(e.Code=="insufficient_capacity"){return null;}
    });}).ToArray();
    var outcomes=await Race(requests);var winners=outcomes.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
    Require(winners.Length==5,"Exactly five five-person orders should fit.");
    Require(await Ledger(h,r.Slot)==25,"Concurrent order holds exceed or lose capacity.");
    // Two concurrent session requests per order must resolve to one stored session.
    var checkoutRequests=winners.SelectMany(id=>Enumerable.Range(0,2).Select(_=>{
        var s=h.CreateServices();return(Func<Task<CheckoutSessionDto>>)(()=>s.Payments.StartCheckoutAsync(id,default));
    })).ToArray();
    var checkouts=await Race(checkoutRequests);
    Require(checkouts.Select(x=>x.CheckoutUrl).Distinct().Count()==5,"Checkout retries generated multiple payable links.");
    foreach(var id in winners)h.Gateway.Pay((await h.OrderAsync(id)).StripeSessionId!);
    var confirms=winners.SelectMany(id=>Enumerable.Range(0,2).Select(_=>{
        var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(()=>s.Payments.ConfirmAsync(id,false,default)));
    })).ToArray();
    Require((await Race(confirms)).All(x=>x=="ok"),"Duplicate confirmation did not succeed idempotently.");
    Require(await Ledger(h,r.Slot)==25,"Paid admissions exceed or lose capacity.");
    await using var db=h.CreateContext();
    var sold=await db.SlotInventories.Where(i=>i.TimeSlotId==r.Slot).SumAsync(i=>i.Sold);
    var held=await db.SlotInventories.Where(i=>i.TimeSlotId==r.Slot).SumAsync(i=>i.Reserved);
    var confirmed=await db.Orders.CountAsync(o=>o.TimeSlotId==r.Slot && o.Status==OrderStatus.Confirmed);
    Require(sold==25 && held==0 && confirmed==5,"Sales were double counted or left held.");
    return new{simultaneousBuyers=20,requestedPeople=100,successfulOrders=5,distinctCheckoutSessions=5,
        checkoutRequests=10,confirmationRequests=10,soldPeople=sold,heldPeople=held,paymentProvider="deterministic fake"};
}

static async Task<object> PhaseOneControl()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);
    // Explicitly reproduce the old absence of an aggregate cap in this disposable fixture only.
    await using(var db=h.CreateContext())await db.TimeSlots.Where(s=>s.Id==r.Slot)
        .ExecuteUpdateAsync(s=>s.SetProperty(t=>t.SharedCapacity,(int?)null));
    TicketQuantityRequest[][] selections=[[new(r.Adult,20)],[new(r.Adult,5)],[new(r.Child,20)],[new(r.Child,5)],[new(r.Wagon,3)]];
    for(var i=0;i<selections.Length;i++)await h.CreateServices().Orders.CreateOrderAsync($"control-{i}",new(r.Slot,selections[i],null),default);
    var people=await Ledger(h,r.Slot);
    Require(people==53,"Phase 1 model control did not reproduce the independent-cap total.");
    return new{model="Phase 1 independent type limits, shared cap intentionally absent in isolated fixture",
        sequentialOrders=5,heldPeople=people,intendedPhysicalCapacity=25,
        interpretation="The old model can exceed 25 without a race. This is a comparison control, not a passing capacity-safety example."};
}

static async Task<object> DuplicateRequests()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);
    var operations=Enumerable.Range(0,16).Select(_=>{var s=h.CreateServices();return(Func<Task<Guid>>)(async()=>
        (await s.Orders.CreateOrderAsync("same-request",new(r.Slot,[new(r.Adult,3),new(r.Child,2)],null),default)).Order.Id);}).ToArray();
    var results=await Race(operations);Require(results.Distinct().Count()==1,"Duplicate request created multiple orders.");
    Require(await Ledger(h,r.Slot)==5,"Duplicate request leaked or multiplied holds.");
    return new{simultaneousRetries=16,distinctOrders=1,heldPeople=5};
}

static async Task<object> PublicAndOffers()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);await Fill(h,r.Slot,r.Adult,r.Child,4);
    var operations=Enumerable.Range(0,24).Select(i=>{var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(async()=>{
        if(i%2==0)await s.Orders.CreateOrderAsync($"public-{i}",new(r.Slot,[new(r.Child,1)],null),default);
        else await s.Waitlist.JoinAsync($"offer-{i}",new(r.Slot,$"audit{i}@example.com",[new(r.Child,1)]),default);
    }));}).ToArray();
    var results=await Race(operations);Require(results.All(x=>x is "ok" or "insufficient_capacity" or "waitlist_active"),"Unexpected public/offer result.");
    Require(await Ledger(h,r.Slot)==25,"Public/waitlist competition did not conserve the last place.");
    return new{publicAttempts=12,waitlistJoins=12,heldPlusSold=25,fifoFairness="Separate review finding; this scenario asserts capacity conservation."};
}

static async Task<(Guid First,Guid Next)> TwoWaiting(TestHarness h,int slot,int adult,int child)
{
    var first=(await h.CreateServices().Waitlist.JoinAsync("audit-first",new(slot,"first@example.com",[new(adult,20)]),default)).Entry.Reference;
    var next=(await h.CreateServices().Waitlist.JoinAsync("audit-next",new(slot,"next@example.com",[new(child,10)]),default)).Entry.Reference;
    return(first,next);
}
static async Task<object> AcceptCancel()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);var filler=await Fill(h,r.Slot,r.Adult,r.Child);
    var q=await TwoWaiting(h,r.Slot,r.Adult,r.Child);
    await h.CreateServices().Payments.ConfirmAsync(filler.Id,true,default);await h.CreateServices().Waitlist.PromoteAsync(r.Slot,default);
    await h.CreateServices().Waitlist.MarkNotifiedAsync(q.First,default);
    var operations=Enumerable.Range(0,24).Select(i=>{var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(async()=>{
        if(i%2==0)await s.Waitlist.AcceptAsync(q.First,default);else await s.Waitlist.CancelAsync(q.First,default);
    }));}).ToArray();
    var result=await Race(operations);Require(result.All(x=>x is "ok" or "offer_already_accepted" or "waitlist_cancelled" or "offer_expired"),"Unexpected accept/cancel result.");
    var people=await Ledger(h,r.Slot);Require(people is 10 or 20,"Cancel/accept left an unexpected owner of inventory.");
    return new{acceptAttempts=12,cancelAttempts=12,heldPlusSold=people,ledgerMatchesOwners=true};
}

static async Task<object> AcceptExpiry()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);var filler=await Fill(h,r.Slot,r.Adult,r.Child);
    var q=await TwoWaiting(h,r.Slot,r.Adult,r.Child);
    await h.CreateServices().Payments.ConfirmAsync(filler.Id,true,default);await h.CreateServices().Waitlist.PromoteAsync(r.Slot,default);
    await h.CreateServices().Waitlist.MarkNotifiedAsync(q.First,default);
    var pause=new PauseTransaction();await using var scope=new AuditScope(h.DatabasePath,h.Clock,h.Gateway,h.WaitlistOptions,pause);
    var accept=Attempt(async()=>{await scope.Waitlist.AcceptAsync(q.First,default);});
    await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    try {h.Clock.Advance(TimeSpan.FromMinutes(h.WaitlistOptions.OfferLifetimeMinutes+1));await h.CreateServices().Waitlist.PromoteAsync(r.Slot,default);}
    finally {pause.Resume.TrySetResult();}
    var outcome=await accept;Require(outcome=="offer_expired","Stale accept should lose to committed expiry/reassignment.");
    Require(await Ledger(h,r.Slot)==10,"Stale accept consumed or freed the next group's hold.");
    return new{staleAcceptance=outcome,nextGroupReserved=10,ledgerMatchesOwners=true};
}

static async Task<object> PaidRaces()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);var paid=await Fill(h,r.Slot,r.Adult,r.Child);
    await h.CreateServices().Payments.StartCheckoutAsync(paid.Id,default);h.Gateway.Pay((await h.OrderAsync(paid.Id)).StripeSessionId!);
    var operations=Enumerable.Range(0,30).Select(i=>{var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(async()=>{
        if(i%3==0)await s.Orders.CreateOrderAsync($"new-{i}",new(r.Slot,[new(r.Child,1)],null),default);
        else await s.Payments.ConfirmAsync(paid.Id,i%3==1,default);
    }));}).ToArray();
    var results=await Race(operations);Require(results.Count(x=>x=="insufficient_capacity")==10,"A new buyer took already-paid inventory.");
    Require(results.Count(x=>x=="ok")==20,"Paid reconciliation/cancellation failed.");
    Require((await h.OrderAsync(paid.Id)).Status==OrderStatus.Confirmed,"Paid order did not end Confirmed.");
    Require(await Ledger(h,r.Slot)==25,"Paid races changed live admissions.");
    return new{confirmOrCancelRequests=20,newBuyerAttempts=10,newBuyersAccepted=0,soldPeople=25};
}

static async Task<object> ExpiredCheckout()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);var old=await Fill(h,r.Slot,r.Adult,r.Child);
    await h.CreateServices().Payments.StartCheckoutAsync(old.Id,default);h.Clock.Advance(TimeSpan.FromMinutes(32));
    var operations=Enumerable.Range(0,30).Select(i=>{var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(async()=>{
        if(i%2==0)await s.Orders.CreateOrderAsync($"replacement-{i}",new(r.Slot,[new(r.Child,2)],null),default);
        else await s.Payments.ConfirmAsync(old.Id,true,default);
    }));}).ToArray();
    var results=await Race(operations);Require(results.All(x=>x is "ok" or "insufficient_capacity"),"Unexpected expiry/new-buyer result.");
    Require((await h.OrderAsync(old.Id)).Status==OrderStatus.Released,"Expired unpaid order did not release.");
    Require(await Ledger(h,r.Slot)==24,"Expiry released twice or lost replacement holds.");
    return new{duplicateReleaseRequests=15,newBuyerAttempts=15,replacementOrders=12,heldPeople=24};
}

static async Task<object> ProviderUncertainty()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);var paid=await Fill(h,r.Slot,r.Adult,r.Child);
    await h.CreateServices().Payments.StartCheckoutAsync(paid.Id,default);h.Gateway.Pay((await h.OrderAsync(paid.Id)).StripeSessionId!);
    h.Clock.Advance(TimeSpan.FromMinutes(32));h.Gateway.FailReads=true;
    var operations=Enumerable.Range(0,24).Select(i=>{var s=h.CreateServices();return(Func<Task<string>>)(()=>Attempt(async()=>{
        if(i%2==0)await s.Orders.CreateOrderAsync($"uncertain-{i}",new(r.Slot,[new(r.Child,1)],null),default);
        else await s.Payments.ConfirmAsync(paid.Id,true,default);
    }));}).ToArray();
    var results=await Race(operations);Require(results.Count(x=>x=="ok")==0,"Unknown paid inventory became available.");
    Require(results.All(x=>x is "insufficient_capacity" or "payment_provider_unavailable"),"Unexpected uncertain-provider result.");
    Require(await Ledger(h,r.Slot)==25,"Unknown paid inventory was released.");
    return new{competingRequests=24,successfulNewClaims=0,heldPeople=25};
}

static async Task<object> CrossProcess()
{
    await using var h=await TestHarness.CreateAsync();var r=await Ride(h);await Fill(h,r.Slot,r.Adult,r.Child,4);
    var workers=new List<Process>();var reads=new List<Task<string>>();
    try
    {
        for(var i=0;i<4;i++)
        {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{Assembly.GetExecutingAssembly().Location,"worker",h.DatabasePath,r.Slot.ToString(),(i%2==0?r.Adult:r.Child).ToString(),i.ToString()})start.ArgumentList.Add(arg);
            var process=Process.Start(start)??throw new Exception("Worker process did not start.");workers.Add(process);
            reads.Add(process.StandardOutput.ReadToEndAsync());
        }
        var deadline=DateTime.UtcNow.AddSeconds(25);
        while(Enumerable.Range(0,4).Any(i=>!File.Exists(h.DatabasePath+$".ready-{i}")))
        {if(DateTime.UtcNow>deadline)throw new TimeoutException("Workers did not become ready.");await Task.Delay(20);}
        await File.WriteAllTextAsync(h.DatabasePath+".go","go");
        await Task.WhenAll(workers.Select(w=>w.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(40));
        var outcomes=new List<string>();
        for(var i=0;i<workers.Count;i++)
        {
            var stderr=await workers[i].StandardError.ReadToEndAsync();
            Require(workers[i].ExitCode==0,$"Worker exited {workers[i].ExitCode}: {stderr}");
            outcomes.AddRange(JsonSerializer.Deserialize<string[]>(await reads[i])!);
        }
        Require(outcomes.Count(x=>x=="ok")==1,"More than one process claimed the last place.");
        Require(outcomes.All(x=>x is "ok" or "insufficient_capacity"),"Unexpected multiprocess result.");
        Require(await Ledger(h,r.Slot)==25,"Cross-process inventory did not conserve the final place.");
        return new{operatingSystemProcesses=4,competingRequests=16,successfulFinalClaims=1,heldPlusSold=25};
    }
    finally
    {
        foreach(var worker in workers){if(!worker.HasExited){worker.Kill();await worker.WaitForExitAsync();}worker.Dispose();}
        foreach(var suffix in new[]{".go",".ready-0",".ready-1",".ready-2",".ready-3"})File.Delete(h.DatabasePath+suffix);
    }
}

sealed class AuditScope : IAsyncDisposable
{
    private readonly AppDbContext db;
    public OrderService Orders {get;}
    public WaitlistService Waitlist {get;}
    public AuditScope(string database,TestClock clock,FakeCheckoutGateway? gateway=null,WaitlistOptions? waitlistOptions=null,DbTransactionInterceptor? interceptor=null)
    {
        var builder=new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={database};Default Timeout=30;Pooling=False");
        if(interceptor is not null)builder.AddInterceptors(interceptor);
        db=new AppDbContext(builder.Options);var inventory=new InventoryService(db);var checkout=Options.Create(new CheckoutOptions());
        var payments=new PaymentService(db,inventory,gateway??new FakeCheckoutGateway(clock),clock,checkout,NullLogger<PaymentService>.Instance);
        Waitlist=new WaitlistService(db,inventory,clock,Options.Create(waitlistOptions??new WaitlistOptions()),checkout,NullLogger<WaitlistService>.Instance);
        Orders=new OrderService(db,inventory,payments,Waitlist,clock,checkout);
    }
    public ValueTask DisposeAsync()=>db.DisposeAsync();
}
sealed class PauseTransaction : DbTransactionInterceptor
{
    public TaskCompletionSource Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;
    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
        TransactionStartingEventData eventData,InterceptionResult<DbTransaction> result,CancellationToken cancellationToken=default)
    {if(Interlocked.Exchange(ref paused,1)==0){Reached.TrySetResult();await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20),cancellationToken);}return result;}
}
