using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Serialization;
using Birko.Serialization.Json;
using Birko.Time;
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
        protected readonly ISerializer _serializer;
        protected readonly IDateTimeProvider _clock;

        /// <summary>The caller's current user, read at each write and stamped on its events; null for none.</summary>
        protected readonly Func<Guid?>? _currentUser;

        /// <summary>
        /// Creates a new async event sourcing store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner async store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        /// <param name="serializer">The serializer for event data. Defaults to SystemJsonSerializer.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        /// <param name="currentUser">Reads the current user for each event (e.g. <c>() =&gt; auditContext.CurrentUserGuid</c>).</param>
        public AsyncEventSourcingStoreWrapper(TStore innerStore, IAsyncEventStore eventStore, ISerializer? serializer = null, IDateTimeProvider? clock = null, Func<Guid?>? currentUser = null)
        {
            _currentUser = currentUser;
            _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _serializer = serializer ?? new SystemJsonSerializer();
            _clock = clock ?? new SystemDateTimeProvider();
        }

        /// <summary>
        /// The one producer of this wrapper's events (TASK-713). The entity's <c>Version</c> is set before it is
        /// serialized, so <c>EventData</c> carries the version it is the event of (it carried the previous one), and the
        /// user is read from the caller's <c>currentUser</c> at each write (it was a settable property on a wrapper the
        /// builder shares, and nothing set it).
        /// </summary>
        protected DomainEvent NewEvent(T item, string eventType, long version)
        {
            item.Version = version;
            return new DomainEvent(item.Guid!.Value, version, eventType, _serializer.Serialize(item), _currentUser?.Invoke(), _clock);
        }

        /// <summary>
        /// Creates a new item and records a Created event.
        /// </summary>
        public virtual async Task<Guid> CreateAsync(T item, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
        {
            item = StoreDelegates.Apply(processDelegate, item);

            // Assign the Guid up front so the event AggregateId, version lookup, and inner-store
            // row all key off the SAME id (CR-C06); the throwaway Guid.NewGuid() was never written
            // back, orphaning the Created event from the persisted row.
            item.EnsureGuid();

            // Generate new version
            var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = NewEvent(item, "Created", newVersion);

            // Append event first
            await _eventStore.AppendAsync(@event, cancellationToken);

            // Then persist to the inner store
            return await _innerStore.CreateAsync(item, null, cancellationToken);
        }

        /// <summary>
        /// Reads an item by GUID.
        /// </summary>
        public virtual async Task<T?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await ReadAsync((new ModelByGuid<T>(id)).Filter(), cancellationToken);
        }

        /// <summary>
        /// Reads a single item.
        /// </summary>
        public virtual async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
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

            data = StoreDelegates.Apply(processDelegate, data);

            // Get current version
            var currentVersion = await _eventStore.GetVersionAsync(data.Guid.Value, cancellationToken);

            // Generate new version
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = NewEvent(data, "Updated", newVersion);

            // Append event first
            await _eventStore.AppendAsync(@event, cancellationToken);

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
            var @event = NewEvent(item, "Deleted", newVersion);

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
            // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
            return await StoreSave.SaveAsync(data, processDelegate, CreateAsync, UpdateAsync, cancellationToken);
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
