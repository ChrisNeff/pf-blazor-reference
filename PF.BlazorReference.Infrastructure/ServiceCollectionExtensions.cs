using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PF.BlazorReference.Core;
using PF.BlazorReference.Infrastructure.Interceptors;
using PF.BlazorReference.Infrastructure.Queues;
using PF.BlazorReference.Infrastructure.UserManagement;

namespace PF.BlazorReference.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "ConsentIdentity";

    /// <summary>
    /// Registers everything Infrastructure owns: the DbContext factory, the data port, the audit and
    /// version interceptors, the invitation queue, and the user-directory port (Entra through Microsoft Graph, or the fake).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditTimestampInterceptor>();
        services.AddSingleton<VersionInterceptor>();

        services.AddDbContextFactory<ConsentIdentityDbContext>((sp, options) => options
            .UseSqlite(connectionString)
            .AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>(), sp.GetRequiredService<VersionInterceptor>()));

        services.AddSingleton<IConsentIdentityDbFactory, ConsentIdentityDbFactory>();
        services.AddSingleton<IInvitationQueue, ChannelInvitationQueue>();

        AddUserManagement(services, configuration);
        return services;
    }

    private static void AddUserManagement(IServiceCollection services, IConfiguration configuration)
    {
        var entra = configuration.GetSection("Entra");

        if (bool.TryParse(entra["UseFakeDirectory"], out var useFake) && useFake)
        {
            services.AddSingleton<IUserManagementService, FakeUserManagementService>();
            return;
        }

        // Fail at startup, like a missing connection string, rather than on the first request.
        var options = new GraphUserManagementOptions(
            TenantId: NullIfEmpty(entra["TenantId"]),
            ManagedIdentityClientId: NullIfEmpty(entra["ManagedIdentityClientId"]),
            InviteRedirectUrl: Required(entra, "InviteRedirectUrl"),
            ActivatedGroupId: Required(entra, "ActivatedGroupId"),
            // On unless explicitly turned off: a misread setting must not send invitations.
            DryRun: !bool.TryParse(entra["DryRun"], out var dryRun) || dryRun);

        // The Graph client is built inside the service and never registered, so nothing else can take it.
        services.AddSingleton<IUserManagementService>(sp =>
            GraphUserManagementService.Create(options, sp.GetRequiredService<ILogger<GraphUserManagementService>>()));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Required(IConfigurationSection section, string key) =>
        NullIfEmpty(section[key])
        ?? throw new InvalidOperationException($"{section.Path}:{key} is not configured. Set it, or set {section.Path}:UseFakeDirectory to true.");
}
