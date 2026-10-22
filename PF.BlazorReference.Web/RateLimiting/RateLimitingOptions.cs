namespace PF.BlazorReference.Web.RateLimiting;

/// <summary>
/// Settings for the rate limiting policies, bound from the "RateLimiting" configuration section.
/// One property per policy, named like the policy.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Submissions of the public registration form, per client address.</summary>
    public WindowSettings Registration { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public sealed class WindowSettings
    {
        public int PermitLimit { get; set; }

        public int WindowSeconds { get; set; }
    }
}
