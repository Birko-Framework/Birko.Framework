using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Filters;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// An async store wrapper that adds event sourcing capabilities to any async store.
    /// All changes are recorded as events in the event store.
    /// </summary>
    /// <typeparam name="TStore">The type of inner async store.</typeparam>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class AsyncEventSourcingStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
        where TStore : IAsyncStore<T>
        where T : Data.Models.AbstractModel, IEventSourced
    {
        protected readonly TStore _innerStore;
        protected readonly IAsyncEventStore _eventStore;

        /// <summary>
        /// Gets the current user ID for event tracking.
        /// </summary>
        public Guid? CurrentUserId { get; set; }

        /// <summary>
        /// Creates a new async event sourcing store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner async store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        public AsyncEventSourcingStoreWrapper(TStore innerStore, IAsyncEventStore eventStore)
        {
            _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        }

        /// <summary>
        /// Creates a new item and records a Created event.
        /// </summary>
        public virtual async Task<Guid> CreateAsync(T item, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
        {
            processDelegate?.Invoke(item);

            // Generate new version
            var currentVersion = await _eventStore.GetVersionAsync(item.Guid ?? Guid.Empty, cancellationToken);
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = new DomainEvent(
                item.Guid ?? Guid.NewGuid(),
                newVersion,
                "Created",
                System.Text.Json.JsonSerializer.Serialize(item),
                CurrentUserId
            );

            // Append event first
            await _eventStore.AppendAsync(@event, cancellationToken);

            // Update version
            item.Version = newVersion;

            // Then persist to the inner store
            return await _innerStore.CreateAsync(item, null, cancellationToken);
        }

        /// <summary>
        /// Reads an item by GUID.
        /// </summary>
        public virtual async Task<T> ReadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await ReadAsync((new ModelByGuid<T>(id)).Filter(), cancellationToken);
        }

        /// <summary>
        /// Reads a single item.
        /// </summary>
        public virtual async Task<T> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            return await _innerStore.ReadAsync(filter, cancellationToken);
        }

        /// <summary>
        /// Counts items.
        /// </summary>
        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            return await _innerStore.CountAsync(filter, cancellationToken);
        }

        /// <summary>
        /// Updates an item and records an Updated event.
        /// </summary>
        public virtual async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Guid == null)
            {
                return;
            }

            processDelegate?.Invoke(data);

            // Get current version
            var currentVersion = await _eventStore.GetVersionAsync(data.Guid.Value, cancellationToken);

            // Generate new version
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = new DomainEvent(
                data.Guid.Value,
                newVersion,
                "Updated",
                System.Text.Json.JsonSerializer.Serialize(data),
                CurrentUserId
            );

            // Append event first
            await _eventStore.AppendAsync(@event, cancellationToken);

            // Update version
            data.Version = newVersion;

            // Then persist to the inner store
            await _innerStore.UpdateAsync(data, null, cancellationToken);
        }

        /// <summary>
        /// Deletes an item and records a Deleted event.
        /// </summary>
        public virtual async Task DeleteAsync(T item, CancellationToken cancellationToken = default)
        {
            if (item == null || item.Guid == null)
            {
                return;
            }

            // Get current version
            var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);

            // Generate new version
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = new DomainEvent(
                item.Guid.Value,
                newVersion,
                "Deleted",
                System.Text.Json.JsonSerializer.Serialize(item),
                CurrentUserId
            );

            // Append event first
            await _eventStore.AppendAsync(@event, cancellationToken);

            // Then delete from the inner store
            await _innerStore.DeleteAsync(item, cancellationToken);
        }

        /// <summary>
        /// Saves an item (create or update).
        /// </summary>
        public virtual async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
        {
            if (data == null)
            {
                return Guid.Empty;
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                await CreateAsync(data, processDelegate, cancellationToken);
            }
            else
            {
                await UpdateAsync(data, processDelegate, cancellationToken);
            }

            return data.Guid ?? Guid.Empty;
        }

        /// <summary>
        /// Initializes the store.
        /// </summary>
        public virtual async Task InitAsync(CancellationToken cancellationToken = default)
        {
            await _innerStore.InitAsync(cancellationToken);
        }

        /// <summary>
        /// Destroys the store.
        /// </summary>
        public virtual async Task DestroyAsync(CancellationToken cancellationToken = default)
        {
            await _innerStore.DestroyAsync(cancellationToken);
        }

        /// <summary>
        /// Creates a new instance of T.
        /// </summary>
        public virtual T CreateInstance()
        {
            return _innerStore.CreateInstance();
        }

        /// <summary>
        /// Gets the inner wrapped store.
        /// </summary>
        object? IStoreWrapper.GetInnerStore()
        {
            return _innerStore;
        }

        /// <summary>
        /// Gets the inner wrapped store as the specified type.
        /// </summary>
        public TInner? GetInnerStoreAs<TInner>() where TInner : class
        {
            return _innerStore as TInner;
        }

        /// <summary>
        /// Gets the event store being used.
        /// </summary>
        public IAsyncEventStore EventStore => _eventStore;

        /// <summary>
        /// Replays events to rebuild an aggregate's state.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The rebuilt aggregate.</returns>
        public virtual async Task<T> ReplayAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            var events = await _eventStore.ReadAsync(aggregateId, cancellationToken);
            var instance = CreateInstance();

            if (instance is IEventSourced eventSourced)
            {
                eventSourced.LoadFromEvents(events);
            }

            return instance;
        }

        /// <summary>
        /// Gets the event history for an aggregate.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All events for the aggregate.</returns>
        public virtual async Task<IEnumerable<IEvent>> GetHistoryAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            return await _eventStore.ReadAsync(aggregateId, cancellationToken);
        }
    }
}
