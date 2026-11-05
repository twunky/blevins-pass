using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// One SQLite file, anchored to the API project folder (content root) regardless of the working directory.
var connectionString = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("BlevinsPass"));
connectionString.DataSource = Path.Combine(builder.Environment.ContentRootPath, connectionString.DataSource);
connectionString.DefaultTimeout = 30; // seconds to wait on a locked database before failing
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString.ToString()));

builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection("Checkout"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICheckoutGateway, StripeCheckoutGateway>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.Configure<WaitlistOptions>(builder.Configuration.GetSection("Waitlist"));
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.Configure<DemoResetOptions>(builder.Configuration.GetSection("DemoReset"));
builder.Services.AddSingleton<BookingOperationGate>();
builder.Services.AddScoped<DemoResetService>();

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // Malformed bodies (e.g. a quantity of 1.5) use the same error shape as business validation.
    options.InvalidModelStateResponseFactory = context =>
    {
        var firstError = context.ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage;
        return new BadRequestObjectResult(new ApiErrorBody(new ApiError(
            "validation_failed",
            string.IsNullOrWhiteSpace(firstError) ? "The request body is invalid." : firstError)));
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // EnsureCreated builds a new file; it never alters an existing one. SchemaUpgrade then adds the Phase 2
    // column/tables to an older file (backup first, additive only, idempotent). See README "Upgrading".
    await db.Database.EnsureCreatedAsync();
    await SchemaUpgrade.ApplyAsync(db, DateTime.UtcNow, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaUpgrade"));
    await SeedData.EnsureSeededAsync(db, DateTime.UtcNow);
}

app.UseMiddleware<BookingExceptionMiddleware>();
app.MapControllers();

app.Run();
