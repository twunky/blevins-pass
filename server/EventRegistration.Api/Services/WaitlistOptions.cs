namespace EventRegistration.Api.Services;

/// <summary>"Waitlist" configuration section.</summary>
public class WaitlistOptions
{
    /// <summary>
    /// How long an offer holds capacity: counted from the offer, and restarted when the owner records
    /// the manual notification. After that the places return to the queue. Default 24 hours.
    /// </summary>
    public int OfferLifetimeMinutes { get; set; } = 24 * 60;

    /// <summary>
    /// Development-only operator outbox and capacity tool. Off by default; appsettings.Development.json
    /// opts in. The endpoints also require a loopback caller and a dedicated header (see OperatorController).
    /// </summary>
    public bool OperatorToolsEnabled { get; set; }

    public const int MaxCapacity = 10_000;
}
