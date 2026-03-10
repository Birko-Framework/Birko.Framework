using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Stores;
using System;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// Extension methods for adding event sourcing to stores.
    /// </summary>
    public static class EventSourcingStoreExtensions
    {
        /// <summary>
        /// Wraps a store with event sourcing capabilities.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="store">The store to wrap.</param>
        /// <param name="eventStore">The event store for recording events.</param>
        /// <returns>An event sourcing wrapper around the store.</returns>
        public static IStore<T> WithEventSourcing<T>(
            this IStore<T> store,
            IEventStore eventStore)
            where T : Data.Models.AbstractModel, IEventSourced
        {
            if (store is IBulkStore<T> bulkStore)
            {
                return new EventSourcingBulkStoreWrapper<IBulkStore<T>, T>(bulkStore, eventStore);
            }
            return new EventSourcingStoreWrapper<IStore<T>, T>(store, eventStore);
        }

        /// <summary>
        /// Wraps an async store with event sourcing capabilities.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="store">The async store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        /// <returns>An async event sourcing wrapper around the store.</returns>
        public static IAsyncStore<T> WithEventSourcing<T>(
            this IAsyncStore<T> store,
            IAsyncEventStore eventStore)
            where T : Data.Models.AbstractModel, IEventSourced
        {
            if (store is IAsyncBulkStore<T> bulkStore)
            {
                return new AsyncEventSourcingBulkStoreWrapper<IAsyncBulkStore<T>, T>(bulkStore, eventStore);
            }
            return new AsyncEventSourcingStoreWrapper<IAsyncStore<T>, T>(store, eventStore);
        }

        /// <summary>
        /// Wraps an async bulk store with event sourcing capabilities.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="store">The async bulk store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        /// <returns>An async event sourcing bulk wrapper around the store.</returns>
        public static IAsyncBulkStore<T> WithEventSourcing<T>(
            this IAsyncBulkStore<T> store,
            IAsyncEventStore eventStore)
            where T : Data.Models.AbstractModel, IEventSourced
        {
            return new AsyncEventSourcingBulkStoreWrapper<IAsyncBulkStore<T>, T>(store, eventStore);
        }

        /// <summary>
        /// Wraps a bulk store with event sourcing capabilities.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="store">The bulk store to wrap.</param>
        /// <param name="eventStore">The event store for recording events.</param>
        /// <returns>An event sourcing bulk wrapper around the store.</returns>
        public static IBulkStore<T> WithEventSourcing<T>(
            this IBulkStore<T> store,
            IEventStore eventStore)
            where T : Data.Models.AbstractModel, IEventSourced
        {
            return new EventSourcingBulkStoreWrapper<IBulkStore<T>, T>(store, eventStore);
        }
    }
}
