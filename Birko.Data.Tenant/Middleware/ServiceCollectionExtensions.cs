using Birko.Data.Tenant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.Tenant.Middleware;

/// <summary>
/// Extension methods for configuring tenant services in ASP.NET Core
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add tenant context to the dependency injection container
    /// </summary>
    public static IServiceCollection AddTenantContext(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        services.Add(new ServiceDescriptor(
            typeof(ITenantContext),
            typeof(TenantContext),
            lifetime
        ));

        return services;
    }

    /// <summary>
    /// Add a custom tenant context implementation to the dependency injection container
    /// </summary>
    public static IServiceCollection AddTenantContext<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : class, ITenantContext
    {
        services.Add(new ServiceDescriptor(
            typeof(ITenantContext),
            typeof(T),
            lifetime
        ));

        return services;
    }

    /// <summary>
    /// Add tenant context as a singleton (for non-web applications)
    /// </summary>
    public static IServiceCollection AddTenantContextSingleton(
        this IServiceCollection services)
    {
        services.AddSingleton<ITenantContext, TenantContext>();
        return services;
    }

    /// <summary>
    /// Add tenant context as a scoped service (for web applications)
    /// </summary>
    public static IServiceCollection AddTenantContextScoped(
        this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, TenantContext>();
        return services;
    }

    /// <summary>
    /// Add tenant context as a transient service
    /// </summary>
    public static IServiceCollection AddTenantContextTransient(
        this IServiceCollection services)
    {
        services.AddTransient<ITenantContext, TenantContext>();
        return services;
    }
}
