using System.Text.Json;
using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Development-only demo tool. Every check below runs before DemoResetService (and so before any
/// Stripe call): 404 unless Development + DemoReset:Enabled + loopback; then the request must come
/// from the configured local frontend origin, carry the dedicated header, be application/json, and
/// explicitly say { "confirm": true }. There is deliberately no CORS policy for this route.
/// </summary>
[ApiController]
[Route("api/dev/demo-reset")]
public class DemoResetController(
    DemoResetService demoReset,
    IWebHostEnvironment environment,
    IOptions<DemoResetOptions> options,
    IOptions<CheckoutOptions> checkoutOptions) : ControllerBase
{
    public const string ConfirmHeaderName = "X-Demo-Reset";
    public const string ConfirmHeaderValue = "confirm";

    [HttpPost]
    public async Task<ActionResult<DemoResetResultDto>> Reset(CancellationToken cancellationToken)
    {
        if (!DemoResetOptions.IsAllowed(environment.IsDevelopment(), options.Value.Enabled, HttpContext.Connection.RemoteIpAddress))
        {
            return NotFound(Error("not_found", "Not found."));
        }

        var expectedOrigin = checkoutOptions.Value.ClientBaseUrl.TrimEnd('/');
        if (!string.Equals(Request.Headers.Origin.ToString(), expectedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error("origin_rejected", "Demo reset must be requested from the local Blevins Pass app."));
        }
        if (Request.Headers[ConfirmHeaderName] != ConfirmHeaderValue)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error("confirmation_header_missing", $"The {ConfirmHeaderName} header is required."));
        }
        if (Request.ContentType is null || !Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, Error("json_required", "Send a JSON body."));
        }

        DemoResetRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<DemoResetRequest>(Request.Body, JsonSerializerOptions.Web, cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest(Error("validation_failed", "The request body is not valid JSON."));
        }
        if (body?.Confirm != true)
        {
            return BadRequest(Error("confirmation_required", "Send { \"confirm\": true } to reset the demo."));
        }

        var result = await demoReset.ResetAsync(cancellationToken);
        return result.Completed ? Ok(result) : Conflict(result);
    }

    private static ApiErrorBody Error(string code, string message) => new(new ApiError(code, message));
}
