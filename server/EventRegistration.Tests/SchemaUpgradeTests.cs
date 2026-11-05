using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Tests;

/// <summary>
/// The owner's existing blevinspass.db predates Phase 2. EnsureCreated cannot change it, so the explicit
/// upgrade must add the new schema without losing purchases, settings or Stripe references.
/// </summary>
public class SchemaUpgradeTests
{
    /// <summary>Turns a fresh harness database into the Phase 1 shape, with a paid order and a non-default theme.</summary>
    private static async Task<(TestHarness Harness, Guid PaidOrderId, string SessionId)> Phase1DatabaseAsync()
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(SchemaUpgrade.HayrideEventName);
        var wagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon").TicketTypeId;
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("phase1-paid", new CreateOrderRequest(slotId, [new(wagon, 2)], null), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        var sessionId = (await harness.OrderAsync(order.Id)).StripeSessionId!;
        harness.Gateway.Pay(sessionId);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);

        await using var db = harness.CreateContext();
        await db.SiteSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveTheme, ThemeName.Winter));
        await db.TicketTypes.Where(t => t.Name == "Family Wagon")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Description, SchemaUpgrade.LegacyFamilyWagonDescription));
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE "WaitlistItems";
            DROP TABLE "WaitlistEntries";
            ALTER TABLE "TimeSlots" DROP COLUMN "SharedCapacity";
            """);
        return (harness, order.Id, sessionId);
    }

    private static async Task<List<string>> SchemaAsync(AppDbContext db, string table) =>
        await db.Database.SqlQuery<string>(
            $"SELECT name || ':' || type || ':' || \"notnull\" || ':' || pk AS \"Value\" FROM pragma_table_info({table}) ORDER BY cid")
            .ToListAsync();

    [Fact]
    public async Task Upgrade_adds_phase2_schema_keeps_data_backs_up_and_is_idempotent()
    {
        var (harness, paidOrderId, sessionId) = await Phase1DatabaseAsync();
        await using var scope = harness;
        string? backupPath;

        await using (var db = harness.CreateContext())
        {
            var result = await SchemaUpgrade.ApplyAsync(db, harness.Clock.UtcNow);
            Assert.True(result.Applied);
            backupPath = result.BackupPath;
            Assert.True(File.Exists(backupPath));
            Assert.Empty(result.OverfullTimeSlotIds);

            var again = await SchemaUpgrade.ApplyAsync(db, harness.Clock.UtcNow.AddMinutes(1));
            Assert.False(again.Applied);
            Assert.Null(again.BackupPath);
        }

        await using (var db = harness.CreateContext())
        {
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == paidOrderId);
            Assert.Equal(OrderStatus.Confirmed, order.Status);
            Assert.Equal(sessionId, order.StripeSessionId);
            Assert.Equal("Family Wagon", order.Items.Single().Description); // historical snapshot unchanged
            Assert.Equal(ThemeName.Winter, (await db.SiteSettings.SingleAsync()).ActiveTheme);

            var hayrideSlots = await db.TimeSlots.Where(s => s.Event!.Name == SchemaUpgrade.HayrideEventName).ToListAsync();
            Assert.All(hayrideSlots, s => Assert.Equal(SchemaUpgrade.HayrideSharedCapacity, s.SharedCapacity));
            Assert.All(await db.TimeSlots.Where(s => s.Event!.Name != SchemaUpgrade.HayrideEventName).ToListAsync(), s => Assert.Null(s.SharedCapacity));
            Assert.Equal(SchemaUpgrade.FamilyWagonDescription, (await db.TicketTypes.SingleAsync(t => t.Name == "Family Wagon")).Description);
            Assert.Equal(2, await db.SlotInventories.SumAsync(i => i.Sold));
        }

        // The upgraded schema matches what EnsureCreated builds for a brand-new database.
        await using var fresh = await TestHarness.CreateAsync();
        await using (var upgradedDb = harness.CreateContext())
        await using (var freshDb = fresh.CreateContext())
        {
            foreach (var table in new[] { "TimeSlots", "WaitlistEntries", "WaitlistItems" })
            {
                Assert.Equal(await SchemaAsync(freshDb, table), await SchemaAsync(upgradedDb, table));
            }
        }

        // The upgraded file works end to end: the waitlist can be joined. The Phase 1 order holds 2 of the
        // 3 Family Wagon places, so a request for 2 cannot be bought now and is a legitimate join
        // (available groups are refused with tickets_available since the final F1 correction).
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync(SchemaUpgrade.HayrideEventName);
        var wagon = inventory.Single(i => i.TicketType!.Name == "Family Wagon").TicketTypeId;
        var joined = await harness.CreateServices().Waitlist.JoinAsync("after-upgrade",
            new JoinWaitlistRequest(slotId, "a@example.com", [new(wagon, 2)]), default);
        Assert.True(joined.Created);
        Assert.Equal("Waiting", joined.Entry.Status);
        File.Delete(backupPath!);
    }

    [Fact]
    public async Task Upgrade_reports_departures_already_over_the_new_shared_limit_without_changing_orders()
    {
        var (harness, paidOrderId, _) = await Phase1DatabaseAsync();
        await using var scope = harness;
        int slotId;
        await using (var db = harness.CreateContext())
        {
            // Raw SQL: the Phase 2 model cannot query a Phase 1 TimeSlots table (no SharedCapacity column yet).
            slotId = await db.Database.SqlQueryRaw<int>("""
                SELECT s."Id" AS "Value" FROM "TimeSlots" s JOIN "Events" e ON e."Id" = s."EventId"
                WHERE e."Name" = 'Pumpkin Patch Hayride' ORDER BY s."StartUtc" LIMIT 1 OFFSET 1
                """).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SlotInventories\" SET \"Sold\" = \"Capacity\" WHERE \"TimeSlotId\" = {slotId}");
        }

        await using (var db = harness.CreateContext())
        {
            var result = await SchemaUpgrade.ApplyAsync(db, harness.Clock.UtcNow);
            Assert.Equal([slotId], result.OverfullTimeSlotIds);
            Assert.Equal(53, await db.SlotInventories.Where(i => i.TimeSlotId == slotId).SumAsync(i => i.Sold));
            Assert.Equal(OrderStatus.Confirmed, (await db.Orders.SingleAsync(o => o.Id == paidOrderId)).Status);
            File.Delete(result.BackupPath!);
        }
    }
}
