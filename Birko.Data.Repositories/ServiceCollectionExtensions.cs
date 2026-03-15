using Microsoft.Extensions.DependencyInjection;
using System;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Extension methods for configuring repository services in ASP.NET Core dependency injection.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        #region Generic Repository Registration

        /// <summary>
        /// Add a repository with its store to the dependency injection container.
        /// </summary>
        /// <typeparam name="TStore">The type of store to register.</typeparam>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepository<TStore, TRepository>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TStore : class, Stores.IBaseStore
            where TRepository : class, IBaseRepository
        {
            // Register the store
            services.Add(new ServiceDescriptor(typeof(TStore), typeof(TStore), lifetime));

            // Register the repository with the store as a dependency
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var store = sp.GetRequiredService<TStore>();
                return Activator.CreateInstance(typeof(TRepository), store) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add a repository with an existing store instance to the dependency injection container.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="store">The store instance to use.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepository<TRepository>(
            this IServiceCollection services,
            Stores.IBaseStore store,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TRepository : class, IBaseRepository
        {
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                return Activator.CreateInstance(typeof(TRepository), store) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        /// <summary>
        /// Add a repository with a factory function to the dependency injection container.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository to register.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="storeFactory">A factory function to create the store.</param>
        /// <param name="lifetime">The service lifetime (defaults to Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepository<TRepository>(
            this IServiceCollection services,
            Func<IServiceProvider, Stores.IBaseStore> storeFactory,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TRepository : class, IBaseRepository
        {
            services.Add(new ServiceDescriptor(typeof(TRepository), sp =>
            {
                var store = storeFactory(sp);
                return Activator.CreateInstance(typeof(TRepository), store) as TRepository
                    ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TRepository).Name}");
            }, lifetime));

            return services;
        }

        #endregion

        #region Singleton Registration

        /// <summary>
        /// Add a repository as a singleton.
        /// </summary>
        /// <typeparam name="TStore">The type of store.</typeparam>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepositorySingleton<TStore, TRepository>(this IServiceCollection services)
            where TStore : class, Stores.IBaseStore
            where TRepository : class, IBaseRepository
        {
            return services.AddRepository<TStore, TRepository>(ServiceLifetime.Singleton);
        }

        #endregion

        #region Scoped Registration

        /// <summary>
        /// Add a repository as a scoped service.
        /// </summary>
        /// <typeparam name="TStore">The type of store.</typeparam>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepositoryScoped<TStore, TRepository>(this IServiceCollection services)
            where TStore : class, Stores.IBaseStore
            where TRepository : class, IBaseRepository
        {
            return services.AddRepository<TStore, TRepository>(ServiceLifetime.Scoped);
        }

        #endregion

        #region Transient Registration

        /// <summary>
        /// Add a repository as a transient service.
        /// </summary>
        /// <typeparam name="TStore">The type of store.</typeparam>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRepositoryTransient<TStore, TRepository>(this IServiceCollection services)
            where TStore : class, Stores.IBaseStore
            where TRepository : class, IBaseRepository
        {
            return services.AddRepository<TStore, TRepository>(ServiceLifetime.Transient);
        }

        #endregion
    }
}
