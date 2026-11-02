using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(CatalogService catalog, BookingOperationGate gate) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EventSummaryDto>> GetEvents(CancellationToken cancellationToken) =>
        catalog.GetEventsAsync(cancellationToken);

    [HttpGet("{eventId:int}")]
    public Task<EventSummaryDto> GetEvent(int eventId, CancellationToken cancellationToken) =>
        catalog.GetEventAsync(eventId, cancellationToken);

    [HttpGet("{eventId:int}/availability")]
    public async Task<SlotAvailabilityDto> GetAvailability(int eventId, [FromQuery] int timeSlotId, CancellationToken cancellationToken)
    {
        // Availability reads may reconcile (release/confirm) stale orders, so they share the booking gate.
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return await catalog.GetTicketAvailabilityAsync(eventId, RequireSlot(timeSlotId), cancellationToken);
    }

    [HttpGet("{eventId:int}/seating")]
    public Task<SeatingChartDto> GetSeating(int eventId, CancellationToken cancellationToken) =>
        catalog.GetSeatingChartAsync(eventId, cancellationToken);

    [HttpGet("{eventId:int}/seating/availability")]
    public async Task<SeatingAvailabilityDto> GetSeatingAvailability(int eventId, [FromQuery] int timeSlotId, CancellationToken cancellationToken)
    {
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return await catalog.GetSeatingAvailabilityAsync(eventId, RequireSlot(timeSlotId), cancellationToken);
    }

    private static int RequireSlot(int timeSlotId) =>
        timeSlotId > 0 ? timeSlotId : throw BookingException.Validation("The timeSlotId query parameter is required.");
}
