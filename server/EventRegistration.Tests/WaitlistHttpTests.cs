using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventRegistration.Api.Controllers;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventRegistration.Tests;

/// <summary>
/// HTTP boundary for the public waitlist and the Development-only operator tools, on a real local
/// Kestrel server with an isolated fixture database and the fake payment gateway.
/// </summary>
public class WaitlistHttpTests
{
    private const string LocalOrigin = "http://127.0.0.1:5173";

    private static async Task<(TestHarness Harness, WebApplication App, HttpClient Client, int SlotId, int AdultId)> StartAsync(string environmentName = "Development", bool operatorEnabled = true)
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync("Pumpkin Patch Hayride");
        var adult = inventory.Single(i => i.TicketType!.Name == "Adult").TicketTypeId;
        var child = inventory.Single(i => i.TicketType!.Name == "Child").TicketTypeId;
        await harness.CreateServices().Orders.CreateOrderAsync("sold-out", new CreateOrderRequest(slotId, [new(adult, 20), new(child, 5)], null), default);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var connection = new SqliteConnectionStringBuilder { DataSource = harness.DatabasePath, Pooling = false, DefaultTimeout = 30 }.ToString();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        builder.Services.Configure<CheckoutOptions>(o => o.ClientBaseUrl = LocalOrigin);
        builder.Services.Configure<WaitlistOptions>(o => o.OperatorToolsEnabled = operatorEnabled);
        builder.Services.AddSingleton<TimeProvider>(harness.Clock);
        builder.Services.AddSingleton<ICheckoutGateway>(harness.Gateway);
        builder.Services.AddSingleton<BookingOperationGate>();
        builder.Services.AddScoped<InventoryService>();
        builder.Services.AddScoped<PaymentService>();
        builder.Services.AddScoped<WaitlistService>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(WaitlistController).Assembly);

        var app = builder.Build();
        app.UseMiddleware<BookingExceptionMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (harness, app, new HttpClient { BaseAddress = new Uri(address) }, slotId, adult);
    }

    private static async Task<JsonElement> JoinAsync(HttpClient client, int slotId, int adultId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/waitlist")
        {
            Content = new StringContent($$"""{"timeSlotId":{{slotId}},"email":"guest@example.com","tickets":[{"ticketTypeId":{{adultId}},"quantity":2}]}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", "join-1");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HttpRequestMessage OperatorRequest(HttpMethod method, string path, bool header = true, string? origin = LocalOrigin)
    {
        var request = new HttpRequestMessage(method, path);
        if (header) request.Headers.Add(OperatorController.OperatorHeaderName, OperatorController.OperatorHeaderValue);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    [Fact]
    public async Task Public_status_hides_contact_details_and_uses_utc_timestamps()
    {
        var (harness, app, client, slotId, adultId) = await StartAsync();
        await using var scope = harness;
        await using var server = app;

        var joined = await JoinAsync(client, slotId, adultId);
        var reference = joined.GetProperty("reference").GetString();
        var status = await client.GetStringAsync($"/api/waitlist/{reference}");

        Assert.DoesNotContain("guest@example.com", status);
        Assert.Contains("\"status\":\"Waiting\"", status);
        Assert.EndsWith("Z", joined.GetProperty("slotStartUtc").GetString());
        Assert.Equal(0, harness.Gateway.CreateCalls);
        // There is no listing endpoint for the public.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/api/waitlist")).StatusCode);
    }

    [Fact]
    public async Task Operator_tools_require_header_and_local_origin_for_changes()
    {
        var (harness, app, client, slotId, adultId) = await StartAsync();
        await using var scope = harness;
        await using var server = app;
        var reference = (await JoinAsync(client, slotId, adultId)).GetProperty("reference").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(OperatorRequest(HttpMethod.Get, "/api/dev/operator/waitlist", header: false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(OperatorRequest(HttpMethod.Post, $"/api/dev/operator/waitlist/{reference}/notified", origin: "https://evil.example"))).StatusCode);

        var outbox = await client.SendAsync(OperatorRequest(HttpMethod.Get, "/api/dev/operator/waitlist", origin: null));
        Assert.Equal(HttpStatusCode.OK, outbox.StatusCode);
        var body = await outbox.Content.ReadAsStringAsync();
        Assert.Contains("guest@example.com", body);
        Assert.Contains($"{LocalOrigin}/?waitlist={reference}", body);
        Assert.Contains("\"operatorStatus\":\"Queued\"", body);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Operator_tools_do_not_exist_outside_enabled_development(string environment, bool enabled)
    {
        var (harness, app, client, _, _) = await StartAsync(environment, enabled);
        await using var scope = harness;
        await using var server = app;

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(OperatorRequest(HttpMethod.Get, "/api/dev/operator/waitlist"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(OperatorRequest(HttpMethod.Get, "/api/dev/operator/departures"))).StatusCode);
    }
}
