using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Site theme. Intentionally unauthenticated for this local demo (no account system);
/// a real deployment would restrict PUT to organizers.
/// </summary>
[ApiController]
[Route("api/settings/theme")]
public class SettingsController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<ThemeDto> Get() => catalog.GetThemeAsync();

    [HttpPut]
    public Task<ThemeDto> Put([FromBody] ThemeDto request) => catalog.SetThemeAsync(request.ActiveTheme);
}
