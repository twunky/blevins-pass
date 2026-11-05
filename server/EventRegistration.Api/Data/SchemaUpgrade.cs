using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Data;

/// <summary>Outcome of <see cref="SchemaUpgrade.ApplyAsync"/>. Applied=false means the database was already current.</summary>
public record SchemaUpgradeResult(bool Applied, string? BackupPath, IReadOnlyList<string> Steps, IReadOnlyList<int> OverfullTimeSlotIds);

/// <summary>
/// Explicit, idempotent upgrade of a Phase 1 database to the Phase 2 schema. EnsureCreated never
/// alters an existing file, so without this an older blevinspass.db would lack the new column/tables.
/// Rules: additive only (no DROP, DELETE or reseed); a consistent copy of the file is written next to
/// it before anything changes; all changes commit in one transaction; running it again does nothing.
/// The SQL mirrors what EnsureCreated produces for the same model (a test compares both schemas).
/// </summary>
public static class SchemaUpgrade
{
    public const int HayrideSharedCapacity = 25;
    public const string HayrideEventName = "Pumpkin Patch Hayride";
    public const string LegacyFamilyWagonDescription = "Private wagon seating for up to 5";
    public const string FamilyWagonDescription = "One rider in a family wagon seat (counts as one person)";

    public static async Task<SchemaUpgradeResult> ApplyAsync(AppDbContext db, DateTime utcNow, ILogger? logger = null)
    {
        var hasSharedCapacity = await ColumnExistsAsync(db, "TimeSlots", "SharedCapacity");
        var hasWaitlist = await TableExistsAsync(db, "WaitlistEntries") && await TableExistsAsync(db, "WaitlistItems");
        if (hasSharedCapacity && hasWaitlist)
        {
            return new SchemaUpgradeResult(false, null, [], []);
        }

        var backupPath = await BackupAsync(db, utcNow);
        var steps = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!hasSharedCapacity)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"TimeSlots\" ADD COLUMN \"SharedCapacity\" INTEGER NULL;");
            steps.Add("Added TimeSlots.SharedCapacity");

            // Owner decision (Phase 2): every hayride ticket, including Family Wagon, is one person; 25 per departure.
            var hayrideSlots = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "TimeSlots" SET "SharedCapacity" = {HayrideSharedCapacity}
                WHERE "SharedCapacity" IS NULL
                  AND "EventId" IN (SELECT "Id" FROM "Events" WHERE "Name" = {HayrideEventName});
                """);
            steps.Add($"Set a shared limit of {HayrideSharedCapacity} on {hayrideSlots} hayride departure(s)");

            // Future-booking wording only; order items keep their own description snapshot.
            var renamed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "TicketTypes" SET "Description" = {FamilyWagonDescription}
                WHERE "Name" = 'Family Wagon' AND "Description" = {LegacyFamilyWagonDescription};
                """);
            steps.Add($"Updated Family Wagon description on {renamed} ticket type(s)");
        }

        if (!hasWaitlist)
        {
            await db.Database.ExecuteSqlRawAsync(WaitlistTablesSql);
            steps.Add("Created WaitlistEntries and WaitlistItems");
        }
        await transaction.CommitAsync();

        var overfull = await FindOverfullTimeSlotsAsync(db);
        foreach (var slotId in overfull)
        {
            // Historical orders are never cancelled or refunded here; the guarded hold simply refuses new tickets.
            logger?.LogWarning("Time slot {TimeSlotId} already holds more tickets than its shared limit; it accepts no new bookings", slotId);
        }
        logger?.LogWarning("Database upgraded to Phase 2 schema. Backup: {BackupPath}. Steps: {Steps}", backupPath, string.Join("; ", steps));
        return new SchemaUpgradeResult(true, backupPath, steps, overfull);
    }

    /// <summary>Departures whose live tickets (held + sold) already exceed the shared limit.</summary>
    public static async Task<List<int>> FindOverfullTimeSlotsAsync(AppDbContext db) =>
        await db.TimeSlots.AsNoTracking()
            .Where(s => s.SharedCapacity != null
                && db.SlotInventories.Where(i => i.TimeSlotId == s.Id).Sum(i => i.Reserved + i.Sold) > s.SharedCapacity)
            .Select(s => s.Id)
            .ToListAsync();

    private static async Task<string?> BackupAsync(AppDbContext db, DateTime utcNow)
    {
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
        {
            return null;
        }
        var backupPath = $"{dataSource}.pre-phase2-{utcNow:yyyyMMddHHmmss}.bak";
        // VACUUM INTO writes a consistent copy and refuses to overwrite an existing file.
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0};", backupPath);
        return backupPath;
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table) =>
        await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {table}")
            .SingleAsync() > 0;

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column) =>
        await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({table}) WHERE name = {column}")
            .SingleAsync() > 0;

    private const string WaitlistTablesSql = """
        CREATE TABLE "WaitlistEntries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_WaitlistEntries" PRIMARY KEY AUTOINCREMENT,
            "PublicReference" TEXT NOT NULL,
            "EventId" INTEGER NOT NULL,
            "TimeSlotId" INTEGER NOT NULL,
            "CustomerEmail" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "RequestIdempotencyKey" TEXT NOT NULL,
            "RequestFingerprint" TEXT NOT NULL,
            "CreatedUtc" TEXT NOT NULL,
            "OfferedUtc" TEXT NULL,
            "NotifiedUtc" TEXT NULL,
            "OfferExpiresUtc" TEXT NULL,
            "ClosedUtc" TEXT NULL,
            "CloseReason" TEXT NULL,
            "OrderId" TEXT NULL
        );
        CREATE TABLE "WaitlistItems" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_WaitlistItems" PRIMARY KEY AUTOINCREMENT,
            "WaitlistEntryId" INTEGER NOT NULL,
            "TicketTypeId" INTEGER NOT NULL,
            "Quantity" INTEGER NOT NULL,
            CONSTRAINT "CK_WaitlistItem_Quantity" CHECK ("Quantity" > 0),
            CONSTRAINT "FK_WaitlistItems_WaitlistEntries_WaitlistEntryId" FOREIGN KEY ("WaitlistEntryId") REFERENCES "WaitlistEntries" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX "IX_WaitlistEntries_PublicReference" ON "WaitlistEntries" ("PublicReference");
        CREATE UNIQUE INDEX "IX_WaitlistEntries_RequestIdempotencyKey" ON "WaitlistEntries" ("RequestIdempotencyKey");
        CREATE INDEX "IX_WaitlistEntries_TimeSlotId_Status" ON "WaitlistEntries" ("TimeSlotId", "Status");
        CREATE INDEX "IX_WaitlistItems_WaitlistEntryId" ON "WaitlistItems" ("WaitlistEntryId");
        """;
}
