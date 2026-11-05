using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventRegistration.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<SlotInventory> SlotInventories => Set<SlotInventory>();
    public DbSet<SeatingSection> SeatingSections => Set<SeatingSection>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<SeatReservation> SeatReservations => Set<SeatReservation>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<WaitlistItem> WaitlistItems => Set<WaitlistItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native date type and loses DateTimeKind. Everything is stored as UTC,
        // so mark values read back as UTC; the API then serializes them with a trailing "Z".
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>().Property(e => e.RegistrationType).HasConversion<string>();

        modelBuilder.Entity<TicketType>()
            .ToTable(t => t.HasCheckConstraint("CK_TicketType_Price", "\"PriceCents\" >= 0"));

        modelBuilder.Entity<SlotInventory>(entity =>
        {
            entity.HasIndex(i => new { i.TimeSlotId, i.TicketTypeId }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_SlotInventory_Counts",
                "\"Reserved\" >= 0 AND \"Sold\" >= 0 AND \"Reserved\" + \"Sold\" <= \"Capacity\""));
        });

        modelBuilder.Entity<SeatingSection>().HasIndex(s => new { s.EventId, s.Code }).IsUnique();
        modelBuilder.Entity<Seat>().HasIndex(s => s.Label).IsUnique();

        modelBuilder.Entity<SeatReservation>(entity =>
        {
            entity.Property(r => r.Status).HasConversion<string>();
            // Only live claims are unique, so released history never blocks rebooking a seat.
            entity.HasIndex(r => new { r.TimeSlotId, r.SeatId })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Held', 'Sold')")
                .HasDatabaseName("IX_SeatReservation_LiveClaim");
            entity.HasIndex(r => r.OrderId);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.Status).HasConversion<string>();
            entity.Property(o => o.RegistrationType).HasConversion<string>();
            entity.HasIndex(o => o.RequestIdempotencyKey).IsUnique();
            entity.HasIndex(o => o.StripeSessionId).IsUnique();
            entity.HasIndex(o => o.Status);
            entity.Ignore(o => o.StripeIdempotencyKey);
            entity.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
        });

        modelBuilder.Entity<SiteSettings>().Property(s => s.ActiveTheme).HasConversion<string>();

        // Keep in sync with SchemaUpgrade, which creates these tables in databases made before Phase 2.
        modelBuilder.Entity<WaitlistEntry>(entity =>
        {
            entity.Property(w => w.Status).HasConversion<string>();
            entity.HasIndex(w => w.PublicReference).IsUnique();
            entity.HasIndex(w => w.RequestIdempotencyKey).IsUnique();
            entity.HasIndex(w => new { w.TimeSlotId, w.Status });
            entity.HasMany(w => w.Items).WithOne().HasForeignKey(i => i.WaitlistEntryId);
        });
        modelBuilder.Entity<WaitlistItem>()
            .ToTable(t => t.HasCheckConstraint("CK_WaitlistItem_Quantity", "\"Quantity\" > 0"));
    }
}

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
