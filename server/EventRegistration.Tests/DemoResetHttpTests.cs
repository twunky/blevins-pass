using System.Net;
using System.Text;
using EventRegistration.Api.Controllers;
using EventRegistration.Api.Data;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Models;
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
/// HTTP-boundary tests for POST /api/dev/demo-reset on a real local Kestrel server, wired to an
/// isolated fixture database and the fake Stripe gateway (never the owner's demo database).
/// Each rejected request must leave the paid fixture order Confirmed with zero refunds created.
/// </summary>
public class DemoResetHttpTests
{
    private const string LocalOrigin = "http://127.0.0.1:5173";

    private static async Task<(TestHarness Harness, WebApplication App, HttpClient Client, Guid PaidOrder)> StartAsync(string environmentName = "Development")
    {
        var harness = await TestHarness.CreateAsync();
        var (_, slotId, inventory) = await harness.GeneralAdmissionSlotAsync("Harvest Lantern Walk");
        var (order, _) = await harness.CreateServices().Orders.CreateOrderAsync("paid",
            new CreateOrderRequest(slotId, [new(inventory[0].TicketTypeId, 2)], null), default);
        await harness.CreateServices().Payments.StartCheckoutAsync(order.Id, default);
        harness.Gateway.Pay((await harness.OrderAsync(order.Id)).StripeSessionId!);
        await harness.CreateServices().Payments.ConfirmAsync(order.Id, cancel: false, default);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var connection = new SqliteConnectionStringBuilder { DataSource = harness.DatabasePath, Pooling = false, DefaultTimeout = 30 }.ToString();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        builder.Services.Configure<CheckoutOptions>(o => o.ClientBaseUrl = LocalOrigin);
        builder.Services.Configure<DemoResetOptions>(o => { o.Enabled = true; o.RefundCompletedTestPurchases = true; });
        builder.Services.AddSingleton<TimeProvider>(harness.Clock);
        builder.Services.AddSingleton<ICheckoutGateway>(harness.Gateway);
        builder.Services.AddSingleton<BookingOperationGate>();
        builder.Services.AddScoped<InventoryService>();
        builder.Services.AddScoped<PaymentService>();
        builder.Services.AddScoped<DemoResetService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(DemoResetController).Assembly);

        var app = builder.Build();
        app.UseMiddleware<BookingExceptionMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (harness, app, new HttpClient { BaseAddress = new Uri(address) }, order.Id);
    }

    private static HttpRequestMessage ResetRequest(string? origin = LocalOrigin, bool header = true, string? body = """{"confirm":true}""", string contentType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/dev/demo-reset");
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (header) request.Headers.Add(DemoResetController.ConfirmHeaderName, DemoResetController.ConfirmHeaderValue);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType);
        return request;
    }

    public static TheoryData<string, HttpStatusCode> RejectedRequests => new()
    {
        { "unconfirmed", HttpStatusCode.BadRequest },
        { "confirm-false", HttpStatusCode.BadRequest },
        { "malformed-json", HttpStatusCode.BadRequest },
        { "form-post", HttpStatusCode.UnsupportedMediaType },
        { "no-content-type", HttpStatusCode.UnsupportedMediaType },
        { "missing-header", HttpStatusCode.Forbidden },
        { "foreign-origin", HttpStatusCode.Forbidden },
        { "missing-origin", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(RejectedRequests))]
    public async Task Rejected_requests_never_reach_reset_or_refund_logic(string scenario, HttpStatusCode expected)
    {
        var (harness, app, client, paidOrder) = await StartAsync();
        await using var harnessScope = harness;
        await using var appScope = app;

        var request = scenario switch
        {
            "unconfirmed" => ResetRequest(body: "{}"),
            "confirm-false" => ResetRequest(body: """{"confirm":false}"""),
            "malformed-json" => ResetRequest(body: "{confirm:"),
            "form-post" => ResetRequest(body: "confirm=true", contentType: "application/x-www-form-urlencoded"),
            "no-content-type" => ResetRequest(body: null),
            "missing-header" => ResetRequest(header: false),
            "foreign-origin" => ResetRequest(origin: "http://evil.example"),
            "missing-origin" => ResetRequest(origin: null),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, harness.Gateway.RefundsCreated);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(paidOrder)).Status);
    }

    [Fact]
    public async Task Valid_local_confirmed_request_resets_and_refunds_with_fake_provider()
    {
        var (harness, app, client, paidOrder) = await StartAsync();
        await using var harnessScope = harness;
        await using var appScope = app;

        var response = await client.SendAsync(ResetRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, harness.Gateway.RefundsCreated);
        Assert.Equal(OrderStatus.RefundedByDemoReset, (await harness.OrderAsync(paidOrder)).Status);
    }

    [Fact]
    public async Task Non_development_returns_404_even_for_a_valid_request()
    {
        var (harness, app, client, paidOrder) = await StartAsync(environmentName: "Production");
        await using var harnessScope = harness;
        await using var appScope = app;

        var response = await client.SendAsync(ResetRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, harness.Gateway.RefundsCreated);
        Assert.Equal(OrderStatus.Confirmed, (await harness.OrderAsync(paidOrder)).Status);
    }
}
