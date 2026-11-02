using EventRegistration.Api.DTOs;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventRegistration.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders, PaymentService payments, BookingOperationGate gate) : ControllerBase
{
    /// <summary>Holds inventory for a server-priced order. Requires an Idempotency-Key header.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        var (order, created) = await orders.CreateOrderAsync(idempotencyKey, request, cancellationToken);
        return created ? CreatedAtAction(nameof(Get), new { orderId = order.Id }, order) : Ok(order);
    }

    [HttpPost("{orderId:guid}/checkout")]
    public async Task<CheckoutSessionDto> StartCheckout(Guid orderId, CancellationToken cancellationToken)
    {
        using var operation = await gate.EnterBookingOperationAsync(cancellationToken);
        return await payments.StartCheckoutAsync(orderId, cancellationToken);
    }

    /// <summary>Verifies payment with Stripe (never trusting the redirect). Body { "cancel": true } abandons checkout.</summary>
    [HttpPost("{orderId:guid}/confirm")]
    public async Task<OrderDto> Confirm(Guid orderId, [FromBody] ConfirmOrderRequest? request, CancellationToken cancellationToken)
    {
        using (await gate.EnterBookingOperationAsync(cancellationToken))
        {
            await payments.ConfirmAsync(orderId, request?.Cancel ?? false, cancellationToken);
        }
        return await orders.GetOrderAsync(orderId);
    }

    [HttpGet("{orderId:guid}")]
    public Task<OrderDto> Get(Guid orderId) => orders.GetOrderAsync(orderId);

    /// <summary>Same summary; the Status field says Confirmed or still pending/released.</summary>
    [HttpGet("{orderId:guid}/confirmation")]
    public Task<OrderDto> GetConfirmation(Guid orderId) => orders.GetOrderAsync(orderId);
}
