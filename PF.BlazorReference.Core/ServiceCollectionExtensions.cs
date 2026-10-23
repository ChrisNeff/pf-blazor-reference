using Microsoft.Extensions.DependencyInjection;
using PF.BlazorReference.Core.Features.Provisioning;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers everything Core owns. One line per use case; move to a scan only if the list gets long.</summary>
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddTransient<RegisterDeveloperUseCase>();
        services.AddTransient<ListRegistrationsUseCase>();
        services.AddTransient<RequestEntraProvisioningUseCase>();
        services.AddTransient<ProvisionDeveloperUseCase>();
        services.AddTransient<ListProvisioningBacklogUseCase>();
        return services;
    }
}
