using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Infrastructure.Interceptors;

namespace PF.BlazorReference.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "ConsentIdentity";

    /// <summary>Registers everything Infrastructure owns: the DbContext factory, the data port and the audit interceptor.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditTimestampInterceptor>();

        services.AddDbContextFactory<ConsentIdentityDbContext>((sp, options) => options
            .UseSqlite(connectionString)
            .AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>()));

        services.AddSingleton<IConsentIdentityDbFactory, ConsentIdentityDbFactory>();
        return services;
    }
}
