namespace EventRegistration.Api.Models;

public enum RegistrationType
{
    GeneralAdmission,
    ReservedSeating,
}

/// <summary>
/// Order lifecycle. Allowed transitions:
/// PendingCheckout -> PendingPayment -> Confirmed, and
/// PendingCheckout/PendingPayment -> Released (with a reason).
/// Confirmed and Released are terminal.
/// </summary>
public enum OrderStatus
{
    /// <summary>Inventory is held; no Stripe session is known to exist yet.</summary>
    PendingCheckout,

    /// <summary>Inventory is held and a Stripe Checkout session exists that may still accept payment.</summary>
    PendingPayment,

    /// <summary>Payment verified server-side; held inventory converted to sold.</summary>
    Confirmed,

    /// <summary>Held inventory returned to the pool. Never reached from Confirmed.</summary>
    Released,

    /// <summary>
    /// Development-only exception (see docs/contracts/booking-rules.md, "Demo reset"): a Confirmed
    /// test purchase whose payment was verified as fully refunded at Stripe by the demo reset.
    /// Terminal; never confirmed again and never shown as a successful booking.
    /// </summary>
    RefundedByDemoReset,
}

public enum SeatReservationStatus
{
    Held,
    Sold,
    Released,
}

public enum ThemeName
{
    Fall,
    Winter,
}
