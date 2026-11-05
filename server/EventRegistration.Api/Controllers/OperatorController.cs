using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Development-only local operator tools: the waitlist notification outbox and a capacity increase for
/// one General Admission departure. These responses contain customer contact details, so every request
/// must pass the same boundary as the demo reset: 404 unless Development + Waitlist:OperatorToolsEnabled +
/// loopback caller; then the dedicated header (which a cross-site page cannot send without CORS, and there
/// is no CORS policy) and, for changes, the local frontend Origin. This is not production admin: there is
/// no authentication, so it must never be enabled outside a developer's own machine.
/// </summary>
[ApiController]
[Route("api/dev/operator")]
public class OperatorController(
    WaitlistService waitlist,
    BookingOperationGate gate,
    IWebHostEnvironment environment,
    IOptions<WaitlistOptions> options,
    IOptions<CheckoutOptions> checkoutOptions) : ControllerBase
{
    public const string OperatorHeaderName = "X-Local-Operator";
    public const string OperatorHeaderValue = "owner";

    /// <summary>Every waitlist entry with contact details and private links, oldest first.</summary>
    [HttpGet("waitlist")]
    public async Task<ActionResult<IReadOnlyList<OperatorWaitlistEntryDto>>> GetOutbox(CancellationToken cancellationToken)
    {
        if (Reject(isChange: false) is { } rejection)
        {
            return rejection;
        }
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return Ok(await waitlist.GetOutboxAsync(cancellationToken));
    }

    /// <summary>Records that the owner personally contacted this customer. Nothing is sent by the app.</summary>
    [HttpPost("waitlist/{reference:guid}/notified")]
    public async Task<IActionResult> MarkNotified(Guid reference, CancellationToken cancellationToken)
    {
        if (Reject(isChange: true) is { } rejection)
        {
            return rejection;
        }
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        await waitlist.MarkNotifiedAsync(reference, cancellationToken);
        return NoContent();
    }

    [HttpGet("departures")]
    public async Task<ActionResult<IReadOnlyList<OperatorDepartureDto>>> GetDepartures(CancellationToken cancellationToken)
    {
        if (Reject(isChange: false) is { } rejection)
        {
            return rejection;
        }
        return Ok(await waitlist.GetDeparturesAsync(cancellationToken));
    }

    [HttpPost("departures/{timeSlotId:int}/capacity")]
    public async Task<IActionResult> IncreaseCapacity(int timeSlotId, [FromBody] IncreaseCapacityRequest request, CancellationToken cancellationToken)
    {
        if (Reject(isChange: true) is { } rejection)
        {
            return rejection;
        }
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        await waitlist.IncreaseCapacityAsync(timeSlotId, request, cancellationToken);
        return NoContent();
    }

    private ObjectResult? Reject(bool isChange)
    {
        if (!DemoResetOptions.IsAllowed(environment.IsDevelopment(), options.Value.OperatorToolsEnabled, HttpContext.Connection.RemoteIpAddress))
        {
            return NotFound(Error("not_found", "Not found."));
        }
        if (Request.Headers[OperatorHeaderName] != OperatorHeaderValue)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error("operator_header_missing", $"The {OperatorHeaderName} header is required."));
        }
        var expectedOrigin = checkoutOptions.Value.ClientBaseUrl.TrimEnd('/');
        if (isChange && !string.Equals(Request.Headers.Origin.ToString(), expectedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error("origin_rejected", "Operator changes must come from the local Blevins Pass app."));
        }
        return null;
    }

    private static ApiErrorBody Error(string code, string message) => new(new ApiError(code, message));
}
