using PF.BlazorReference.Web.BackgroundServices;
using PF.BlazorReference.Web.RateLimiting;

namespace PF.BlazorReference.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the Web project owns: Razor components with the interactive server mode,
    /// the rate limiting policies, and the background workers (BackgroundServices/). The rate limiter middleware is added in Program.cs. The MudBlazor
    /// services belong to the shared library, which registers them in AddSharedLibrary().
    /// </summary>
    public static IServiceCollection AddWeb(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddRateLimiting();

        services.AddBackgroundServices();
        return services;
    }
}
