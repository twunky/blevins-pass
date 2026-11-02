namespace EventRegistration.Api.Services;

/// <summary>Bound from the "Checkout" configuration section.</summary>
public class CheckoutOptions
{
    /// <summary>Origin of the React client; Stripe redirects back here.</summary>
    public string ClientBaseUrl { get; set; } = "http://127.0.0.1:5173";

    /// <summary>
    /// Stripe session lifetime. Stripe requires at least 30 minutes, so 31 leaves headroom.
    /// Inventory stays held until Stripe itself reports the session expired or paid.
    /// </summary>
    public int SessionLifetimeMinutes { get; set; } = 31;

    /// <summary>How long a hold may sit before checkout is ever attempted.</summary>
    public int PreCheckoutHoldMinutes { get; set; } = 15;

    public const int MaxTicketsPerLine = 20;
    public const int MaxSeatsPerOrder = 8;
}
