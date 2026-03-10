using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.EventSourcing.Repositories
{
    /// <summary>
    /// Extension methods for registering event sourcing repositories.
    /// </summary>
    public static class EventSourcingRepositoryExtensions
    {
        /// <summary>
        /// Adds a sync repository with event sourcing enabled.
        /// </summary>
        /// <typeparam name="TStore">The store type.</typeparam>
        /// <typeparam name="TRepository">The repository type.</typeparam>
        /// <typeparam name="TModel">The model type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventSourcingSyncRepository<TStore, TRepository, TModel>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TStore : class, IStore<TModel>
            where TRepository : class, Data.Repositories.IViewModelRepository<Data.Models.ILoadable<TModel>, TModel>
            where TModel : Data.Models.AbstractModel, IEventSourced, Data.Models.ILoadable<Data.Models.ILoadable<TModel>>
        {
            services.Add(new ServiceDescriptor(
                typeof(TStore),
                sp =>
                {
                    var innerStore = ActivatorUtilities.CreateInstance<TStore>(sp);
                    var eventStore = sp.GetRequiredService<IEventStore>();
                    return innerStore.WithEventSourcing(eventStore) as TStore;
                },
                lifetime));

            services.Add(new ServiceDescriptor(
                typeof(TRepository),
                typeof(TRepository),
                lifetime));

            return services;
        }

        /// <summary>
        /// Adds an async repository with event sourcing enabled.
        /// </summary>
        /// <typeparam name="TStore">The async store type.</typeparam>
        /// <typeparam name="TRepository">The async repository type.</typeparam>
        /// <typeparam name="TModel">The model type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventSourcingAsyncRepository<TStore, TRepository, TModel>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TStore : class, IAsyncStore<TModel>
            where TRepository : class, Data.Repositories.IAsyncViewModelRepository<Data.Models.ILoadable<TModel>, TModel>
            where TModel : Data.Models.AbstractModel, IEventSourced, Data.Models.ILoadable<Data.Models.ILoadable<TModel>>
        {
            services.Add(new ServiceDescriptor(
                typeof(TStore),
                sp =>
                {
                    var innerStore = ActivatorUtilities.CreateInstance<TStore>(sp);
                    var eventStore = sp.GetRequiredService<IAsyncEventStore>();
                    return innerStore.WithEventSourcing(eventStore) as TStore;
                },
                lifetime));

            services.Add(new ServiceDescriptor(
                typeof(TRepository),
                typeof(TRepository),
                lifetime));

            return services;
        }

        /// <summary>
        /// Adds an async bulk repository with event sourcing enabled.
        /// </summary>
        /// <typeparam name="TStore">The async bulk store type.</typeparam>
        /// <typeparam name="TRepository">The async bulk repository type.</typeparam>
        /// <typeparam name="TModel">The model type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="lifetime">The service lifetime.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventSourcingAsyncBulkRepository<TStore, TRepository, TModel>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TStore : class, IAsyncBulkStore<TModel>
            where TRepository : class, Data.Repositories.IAsyncBulkViewModelRepository<Data.Models.ILoadable<TModel>, TModel>
            where TModel : Data.Models.AbstractModel, IEventSourced, Data.Models.ILoadable<Data.Models.ILoadable<TModel>>
        {
            services.Add(new ServiceDescriptor(
                typeof(TStore),
                sp =>
                {
                    var innerStore = ActivatorUtilities.CreateInstance<TStore>(sp);
                    var eventStore = sp.GetRequiredService<IAsyncEventStore>();
                    return innerStore.WithEventSourcing(eventStore) as TStore;
                },
                lifetime));

            services.Add(new ServiceDescriptor(
                typeof(TRepository),
                typeof(TRepository),
                lifetime));

            return services;
        }
    }
}
