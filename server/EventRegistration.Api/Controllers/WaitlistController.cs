using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Customer waitlist. Joining stores a request only: no payment provider call and no card details.
/// Entries are reachable only by their private reference; there is no listing endpoint.
/// </summary>
[ApiController]
[Route("api/waitlist")]
public class WaitlistController(WaitlistService waitlist, OrderService orders, BookingOperationGate gate) : ControllerBase
{
    /// <summary>Requires an Idempotency-Key header; a retried join returns the same entry.</summary>
    [HttpPost]
    public async Task<ActionResult<WaitlistStatusDto>> Join(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] JoinWaitlistRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        var (entry, created) = await waitlist.JoinAsync(idempotencyKey, request, cancellationToken);
        return created ? CreatedAtAction(nameof(Get), new { reference = entry.Reference }, entry) : Ok(entry);
    }

    [HttpGet("{reference:guid}")]
    public async Task<WaitlistStatusDto> Get(Guid reference, CancellationToken cancellationToken)
    {
        // Reading may expire an offer (returning its places), so it shares the booking gate.
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return await waitlist.GetStatusAsync(reference, cancellationToken);
    }

    /// <summary>Turns a notified offer into an ordinary held order. Retry-safe. The client then calls the order checkout endpoint.</summary>
    [HttpPost("{reference:guid}/accept")]
    public async Task<OrderDto> Accept(Guid reference, CancellationToken cancellationToken)
    {
        Guid orderId;
        using (await gate.EnterBookingOperationAsync(cancellationToken))
        {
            orderId = await waitlist.AcceptAsync(reference, cancellationToken);
        }
        return await orders.GetOrderAsync(orderId);
    }

    [HttpPost("{reference:guid}/cancel")]
    public async Task<WaitlistStatusDto> Cancel(Guid reference, CancellationToken cancellationToken)
    {
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return await waitlist.CancelAsync(reference, cancellationToken);
    }
}
