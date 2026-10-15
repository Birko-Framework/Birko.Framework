using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Birko.Data.Tenant.Repositories
{
    /// <summary>
    /// Extension methods for configuring tenant-aware repository services
    /// </summary>
    public static class RepositoryServiceCollectionExtensions
    {
        /// <summary>
        /// Add a tenant-aware repository with a wrapped store to the dependency injection container.
        /// </summary>
        /// <typeparam name="TStore">The type of inner store to wrap.</typeparam>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <typeparam name="TModel">The model type that implements ITenant.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTenantRepository<TStore, TRepository, TModel>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TStore : class, IStore<TModel>
            where TRepository : class, IBaseRepository
            where TModel : Data.Models.AbstractModel, ITenant
        {
            // Register the inner store
            services.Add(new ServiceDescriptor(typeof(TStore), typeof(TStore), lifetime));

            // Register the repository with a tenant-wrapped store
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var innerStore = sp.GetRequiredService<TStore>();
                var tenantContext = sp.GetService<ITenantContext>() ?? Models.Tenant.Current;

                // SH-M339 (TASK-161): AsTenantAware picks the bulk wrapper for a bulk store, as everywhere else. This always built
                // the non-bulk one, so a bulk repository could not be constructed or lost the tenant-scoped filtered writes.
                var wrappedStore = innerStore.AsTenantAware(tenantContext, mode);

                return Activator.CreateInstance(typeof(TRepository), wrappedStore) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add an async tenant-aware repository with a wrapped store to the dependency injection container.
        /// </summary>
        /// <typeparam name="TStore">The type of inner async store to wrap.</typeparam>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <typeparam name="TModel">The model type that implements ITenant.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTenantAsyncRepository<TStore, TRepository, TModel>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TStore : class, IAsyncStore<TModel>
            where TRepository : class, IAsyncBaseRepository // TASK-161: the async base; IBaseRepository made every async repository unregistrable
            where TModel : Data.Models.AbstractModel, ITenant
        {
            // Register the inner store
            services.Add(new ServiceDescriptor(typeof(TStore), typeof(TStore), lifetime));

            // Register the repository with a tenant-wrapped store
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var innerStore = sp.GetRequiredService<TStore>();
                var tenantContext = sp.GetService<ITenantContext>() ?? Models.Tenant.Current;

                // SH-M339 (TASK-161): AsTenantAware picks the bulk wrapper for a bulk store.
                var wrappedStore = innerStore.AsTenantAware(tenantContext, mode);

                return Activator.CreateInstance(typeof(TRepository), wrappedStore) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add a tenant-aware repository with a factory function to create the store.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <typeparam name="TModel">The model type that implements ITenant.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="storeFactory">A factory function to create the inner store.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTenantRepository<TRepository, TModel>(
            this IServiceCollection services,
            Func<IServiceProvider, IStore<TModel>> storeFactory,
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TRepository : class, IBaseRepository
            where TModel : Data.Models.AbstractModel, ITenant
        {
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var innerStore = storeFactory(sp);
                var tenantContext = sp.GetService<ITenantContext>() ?? Models.Tenant.Current;

                var wrappedStore = innerStore.AsTenantAware(tenantContext, mode); // SH-M339 (TASK-161)

                return Activator.CreateInstance(typeof(TRepository), wrappedStore) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add an async tenant-aware repository with a factory function to create the store.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <typeparam name="TModel">The model type that implements ITenant.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="storeFactory">A factory function to create the inner store.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTenantAsyncRepository<TRepository, TModel>(
            this IServiceCollection services,
            Func<IServiceProvider, IAsyncStore<TModel>> storeFactory,
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TRepository : class, IAsyncBaseRepository
            where TModel : Data.Models.AbstractModel, ITenant
        {
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var innerStore = storeFactory(sp);
                var tenantContext = sp.GetService<ITenantContext>() ?? Models.Tenant.Current;

                var wrappedStore = innerStore.AsTenantAware(tenantContext, mode); // SH-M339 (TASK-161)

                return Activator.CreateInstance(typeof(TRepository), wrappedStore) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add a tenant-aware repository as a scoped service.
        /// </summary>
        public static IServiceCollection AddTenantRepositoryScoped<TStore, TRepository, TModel>(this IServiceCollection services, TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TStore : class, IStore<TModel>
            where TRepository : class, IBaseRepository
            where TModel : Data.Models.AbstractModel, ITenant
        {
            return services.AddTenantRepository<TStore, TRepository, TModel>(ServiceLifetime.Scoped, mode);
        }

        /// <summary>
        /// Add an async tenant-aware repository as a scoped service.
        /// </summary>
        public static IServiceCollection AddTenantAsyncRepositoryScoped<TStore, TRepository, TModel>(this IServiceCollection services, TenantIsolationMode mode = TenantIsolationMode.Permissive)
            where TStore : class, IAsyncStore<TModel>
            where TRepository : class, IAsyncBaseRepository
            where TModel : Data.Models.AbstractModel, ITenant
        {
            return services.AddTenantAsyncRepository<TStore, TRepository, TModel>(ServiceLifetime.Scoped, mode);
        }
    }
}
