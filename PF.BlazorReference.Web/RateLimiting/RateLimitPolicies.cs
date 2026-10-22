namespace PF.BlazorReference.Web.RateLimiting;

/// <summary>
/// Names of the rate limiting policies, used by <c>[EnableRateLimiting]</c> and by policy registration.
/// Only public, unauthenticated write paths get a policy. Pages behind sign-in will be limited per user
/// with their own policy when authentication arrives, not by a global per-address limit.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>The public registration form's submissions.</summary>
    public const string Registration = "registration";
}
