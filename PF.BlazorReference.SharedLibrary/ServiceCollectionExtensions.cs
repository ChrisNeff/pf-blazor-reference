using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace PF.BlazorReference.SharedLibrary;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the shared library owns: the MudBlazor services its shell and
    /// components depend on. An app that uses the shell calls this from Program.cs.
    /// </summary>
    public static IServiceCollection AddSharedLibrary(this IServiceCollection services)
    {
        services.AddMudServices();
        return services;
    }
}
