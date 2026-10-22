using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PF.BlazorReference.Web.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    private const int SegmentsPerWindow = 6;

    /// <summary>
    /// Registers the rate limiting policies. A policy applies only where a page opts in with
    /// <c>[EnableRateLimiting(RateLimitPolicies.X)]</c>; the middleware is added in Program.cs.
    /// </summary>
    /// <remarks>
    /// This protects a public form from one abusive client. It does not stop a volumetric distributed
    /// attack, which has to be absorbed at the edge (a CDN, WAF or application gateway in front of the app).
    /// </remarks>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(context);
                return ValueTask.CompletedTask;
            };
        });

        // A second step so the policies can read the bound options (and any override in tests).
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
            {
                var registration = settings.Value.Registration;

                limiter.AddPolicy(RateLimitPolicies.Registration, context =>
                    // Only submissions are limited. Showing the form is not.
                    HttpMethods.IsPost(context.Request.Method)
                        ? RateLimitPartition.GetSlidingWindowLimiter(
                            ClientKey.For(context.Connection.RemoteIpAddress), _ => Window(registration))
                        : RateLimitPartition.GetNoLimiter("not-a-submission"));
            });

        return services;
    }

    /// <summary>
    /// Sliding window leases carry no retry hint (fixed window leases do), so when the limiter gives none
    /// this answers with the policy's full window. After that long a client is certain to be admitted.
    /// </summary>
    private static string RetryAfterSeconds(OnRejectedContext context)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var hint))
            return ((int)Math.Ceiling(hint.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        return settings.Registration.WindowSeconds.ToString(CultureInfo.InvariantCulture);
    }

    // Behind a reverse proxy the remote address is the proxy's unless forwarded headers are configured.
    private static SlidingWindowRateLimiterOptions Window(RateLimitingOptions.WindowSettings settings) => new()
    {
        PermitLimit = settings.PermitLimit,
        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
        SegmentsPerWindow = SegmentsPerWindow,
        QueueLimit = 0,
    };
}
