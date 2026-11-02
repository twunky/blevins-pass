using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    /// <summary>Process liveness only; deliberately reveals no configuration.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
