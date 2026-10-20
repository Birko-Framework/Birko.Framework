using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Birko.Data.Localization.Translations;

/// <summary>Registers <see cref="IEntityTranslations{T}"/> (TASK-682).</summary>
/// <remarks>
/// Needs <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>, which this project does not declare: a consumer
/// importing <c>Birko.Data.Repositories</c> has it already, any other adds the package (CLAUDE-maintenance.md § External
/// dependencies, shape 3).
/// </remarks>
public static class EntityTranslationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEntityTranslations{T}"/> built from the container's <c>IAsyncBulkStore&lt;T&gt;</c>,
    /// <c>IAsyncBulkStore&lt;TTranslation&gt;</c> and <see cref="IEntityLocalizationContext"/>. When the container's
    /// entity store is the localized wrapper, use the overload taking factories and pass the store it wraps.
    /// </summary>
    public static IServiceCollection AddEntityTranslations<T, TTranslation>(
        this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
        => services.AddEntityTranslations<T, TTranslation>(
            sp => sp.GetRequiredService<IAsyncBulkStore<T>>(),
            sp => sp.GetRequiredService<IAsyncBulkStore<TTranslation>>(),
            lifetime);

    /// <summary>Registers <see cref="IEntityTranslations{T}"/> over the stores the factories return.</summary>
    public static IServiceCollection AddEntityTranslations<T, TTranslation>(
        this IServiceCollection services,
        Func<IServiceProvider, IAsyncBulkStore<T>> entities,
        Func<IServiceProvider, IAsyncBulkStore<TTranslation>> translations,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : AbstractModel, ILocalizable
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(translations);
        services.Add(new ServiceDescriptor(
            typeof(IEntityTranslations<T>),
            sp => new EntityTranslations<T, TTranslation>(
                entities(sp), translations(sp), sp.GetRequiredService<IEntityLocalizationContext>()),
            lifetime));
        return services;
    }

    /// <summary><see cref="AddEntityTranslations{T, TTranslation}(IServiceCollection, ServiceLifetime)"/> with <see cref="EntityTranslationModel"/>.</summary>
    public static IServiceCollection AddEntityTranslations<T>(
        this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : AbstractModel, ILocalizable
        => services.AddEntityTranslations<T, EntityTranslationModel>(lifetime);
}
