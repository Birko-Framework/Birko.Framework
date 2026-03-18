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

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// A store wrapper that adds event sourcing capabilities to any store.
    /// All changes are recorded as events in the event store.
    /// </summary>
    /// <typeparam name="TStore">The type of inner store.</typeparam>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class EventSourcingStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
        where TStore : IStore<T>
        where T : Data.Models.AbstractModel, IEventSourced
    {
        protected readonly TStore _innerStore;
        protected readonly IEventStore _eventStore;
        protected readonly ISerializer _serializer;
        protected readonly IDateTimeProvider _clock;

        /// <summary>
        /// Gets the current user ID for event tracking.
        /// </summary>
        public Guid? CurrentUserId { get; set; }

        /// <summary>
        /// Creates a new event sourcing store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner store to wrap.</param>
        /// <param name="eventStore">The event store for recording events.</param>
        /// <param name="serializer">The serializer for event data. Defaults to SystemJsonSerializer.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public EventSourcingStoreWrapper(TStore innerStore, IEventStore eventStore, ISerializer? serializer = null, IDateTimeProvider? clock = null)
        {
            _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _serializer = serializer ?? new SystemJsonSerializer();
            _clock = clock ?? new SystemDateTimeProvider();
        }

        /// <summary>
        /// Creates a new item and records a Created event.
        /// </summary>
        public virtual Guid Create(T item, StoreDataDelegate<T>? processDelegate = null)
        {
            processDelegate?.Invoke(item);

            // Generate new version
            var newVersion = _eventStore.GetVersion(item.Guid ?? Guid.Empty) + 1;

            // Create the event
            var @event = new DomainEvent(
                item.Guid ?? Guid.NewGuid(),
                newVersion,
                "Created",
                _serializer.Serialize(item),
                CurrentUserId,
                _clock
            );

            // Append event first
            _eventStore.Append(@event);

            // Update version
            item.Version = newVersion;

            // Then persist to the inner store
            return _innerStore.Create(item, null);
        }

        /// <summary>
        /// Reads an item by GUID.
        /// </summary>
        public virtual T? Read(Guid id)
        {
            return Read((new ModelByGuid<T>(id)).Filter());
        }

        /// <summary>
        /// Reads a single item matching a filter.
        /// </summary>
        public virtual T? Read(Expression<Func<T, bool>>? filter = null)
        {
            return _innerStore.Read(filter);
        }

        /// <summary>
        /// Counts items.
        /// </summary>
        public virtual long Count(Expression<Func<T, bool>>? filter = null)
        {
            return _innerStore.Count(filter);
        }

        /// <summary>
        /// Updates an item and records an Updated event.
        /// </summary>
        public virtual void Update(T data, StoreDataDelegate<T>? processDelegate = null)
        {
            if (data == null || data.Guid == null)
            {
                return;
            }

            processDelegate?.Invoke(data);

            // Get current version
            var currentVersion = _eventStore.GetVersion(data.Guid.Value);

            // Generate new version
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = new DomainEvent(
                data.Guid.Value,
                newVersion,
                "Updated",
                _serializer.Serialize(data),
                CurrentUserId,
                _clock
            );

            // Append event first
            _eventStore.Append(@event);

            // Update version
            data.Version = newVersion;

            // Then persist to the inner store
            _innerStore.Update(data, null);
        }

        /// <summary>
        /// Deletes an item and records a Deleted event.
        /// </summary>
        public virtual void Delete(T item)
        {
            if (item == null || item.Guid == null)
            {
                return;
            }

            // Get current version
            var currentVersion = _eventStore.GetVersion(item.Guid.Value);

            // Generate new version
            var newVersion = currentVersion + 1;

            // Create the event
            var @event = new DomainEvent(
                item.Guid.Value,
                newVersion,
                "Deleted",
                _serializer.Serialize(item),
                CurrentUserId,
                _clock
            );

            // Append event first
            _eventStore.Append(@event);

            // Then delete from the inner store
            _innerStore.Delete(item);
        }

        /// <summary>
        /// Saves an item (create or update).
        /// </summary>
        public virtual Guid Save(T data, StoreDataDelegate<T>? processDelegate = null)
        {
            if (data == null)
            {
                return Guid.Empty;
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return Create(data, processDelegate);
            }
            else
            {
                Update(data, processDelegate);
                return data.Guid ?? Guid.Empty;
            }
        }

        /// <summary>
        /// Initializes the store.
        /// </summary>
        public virtual void Init()
        {
            _innerStore.Init();
        }

        /// <summary>
        /// Destroys the store.
        /// </summary>
        public virtual void Destroy()
        {
            _innerStore.Destroy();
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
        public IEventStore EventStore => _eventStore;

        /// <summary>
        /// Replays events to rebuild an aggregate's state.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <returns>The rebuilt aggregate.</returns>
        public virtual T Replay(Guid aggregateId)
        {
            var events = _eventStore.Read(aggregateId).ToList();
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
        /// <returns>All events for the aggregate.</returns>
        public virtual IEnumerable<IEvent> GetHistory(Guid aggregateId)
        {
            return _eventStore.Read(aggregateId);
        }
    }
}
