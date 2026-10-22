using PF.BlazorReference.Web.RateLimiting;

namespace PF.BlazorReference.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the Web project owns: Razor components with the interactive server mode,
    /// and the rate limiting policies. The rate limiter middleware is added in Program.cs. The MudBlazor
    /// services belong to the shared library, which registers them in AddSharedLibrary().
    /// </summary>
    public static IServiceCollection AddWeb(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddRateLimiting();
        return services;
    }
}
