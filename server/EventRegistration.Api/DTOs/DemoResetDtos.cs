namespace EventRegistration.Api.DTOs;

/// <summary>
/// Outcome of a development demo reset. Completed=false means local availability was NOT changed;
/// Blockers say why, and RefundedOrders reports any refunds already made at Stripe (which cannot
/// be undone). Retrying is safe: refunds are idempotent and already-refunded payments are a no-op.
/// </summary>
public record DemoResetResultDto(
    bool Completed,
    string Message,
    IReadOnlyList<DemoResetRefundDto> RefundedOrders,
    int ReleasedPendingOrders,
    IReadOnlyList<DemoResetBlockerDto> Blockers);

public record DemoResetRefundDto(
    Guid OrderId,
    string CheckoutSessionId,
    string PaymentIntentId,
    IReadOnlyList<string> RefundIds,
    long AmountRefundedCents,
    string ProviderStatus);

public record DemoResetBlockerDto(Guid OrderId, string Reason);

/// <summary>Body of POST /api/dev/demo-reset; must be { "confirm": true }.</summary>
public record DemoResetRequest(bool Confirm);
