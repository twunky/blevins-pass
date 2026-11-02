using System.Net;

namespace EventRegistration.Api.Services;

/// <summary>
/// "DemoReset" configuration section. Off by default; appsettings.Development.json opts in locally.
/// </summary>
public class DemoResetOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Owner decision (Nov 1 2026): the reset refunds every completed test purchase made through this
    /// application. Each refund is bound to that order's own stored Checkout Session and verified
    /// (test mode, order reference, amount, currency); no other Stripe account payments are touched.
    /// When false, any paid order blocks the reset instead.
    /// </summary>
    public bool RefundCompletedTestPurchases { get; set; }

    /// <summary>The endpoint exists only in Development, with the opt-in set, for loopback callers.</summary>
    public static bool IsAllowed(bool isDevelopment, bool enabled, IPAddress? remoteAddress) =>
        isDevelopment && enabled && remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
}
