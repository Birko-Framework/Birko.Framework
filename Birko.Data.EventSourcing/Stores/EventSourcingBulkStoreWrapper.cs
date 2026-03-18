using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Stores;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// A bulk store wrapper that adds event sourcing capabilities to any bulk store.
    /// All changes are recorded as events in the event store.
    /// </summary>
    /// <typeparam name="TStore">The type of inner bulk store.</typeparam>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class EventSourcingBulkStoreWrapper<TStore, T> : EventSourcingStoreWrapper<TStore, T>, IBulkStore<T>, IStoreWrapper<T>
        where TStore : IBulkStore<T>
        where T : Data.Models.AbstractModel, IEventSourced
    {
        /// <summary>
        /// Creates a new event sourcing bulk store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner bulk store to wrap.</param>
        /// <param name="eventStore">The event store for recording events.</param>
        /// <param name="serializer">The serializer for event data. Defaults to SystemJsonSerializer.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public EventSourcingBulkStoreWrapper(TStore innerStore, IEventStore eventStore, Birko.Serialization.ISerializer? serializer = null, IDateTimeProvider? clock = null)
            : base(innerStore, eventStore, serializer, clock)
        {
        }

        /// <summary>
        /// Creates multiple items and records Created events.
        /// </summary>
        public virtual void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null) return;

            var items = data.ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                storeDelegate?.Invoke(item);

                var currentVersion = _eventStore.GetVersion(item.Guid ?? Guid.Empty);
                var newVersion = currentVersion + 1;

                var @event = new DomainEvent(
                    item.Guid ?? Guid.NewGuid(),
                    newVersion,
                    "Created",
                    _serializer.Serialize(item),
                    CurrentUserId,
                    _clock
                );

                events.Add(@event);
                item.Version = newVersion;
            }

            // Append all events first
            _eventStore.AppendRange(events);

            // Then persist to the inner store
            (_innerStore as IBulkStore<T>)?.Create(items, null);
        }

        /// <summary>
        /// Reads all items.
        /// </summary>
        public virtual IEnumerable<T> Read()
        {
            return Read(null, null, null, null);
        }

        /// <summary>
        /// Reads multiple items with optional filter, sorting and pagination.
        /// </summary>
        public virtual IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            return (_innerStore as IBulkStore<T>)?.Read(filter, orderBy, limit, offset) ?? Enumerable.Empty<T>();
        }

        /// <summary>
        /// Updates multiple items and records Updated events.
        /// </summary>
        public virtual void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data == null) return;

            var items = data.ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                storeDelegate?.Invoke(item);

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = new DomainEvent(
                    item.Guid.Value,
                    newVersion,
                    "Updated",
                    _serializer.Serialize(item),
                    CurrentUserId,
                    _clock
                );

                events.Add(@event);
                item.Version = newVersion;
            }

            // Append all events first
            _eventStore.AppendRange(events);

            // Then persist to the inner store
            (_innerStore as IBulkStore<T>)?.Update(items, null);
        }

        /// <summary>
        /// Deletes multiple items and records Deleted events.
        /// </summary>
        public virtual void Delete(IEnumerable<T> data)
        {
            if (data == null) return;

            var items = data.ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = new DomainEvent(
                    item.Guid.Value,
                    newVersion,
                    "Deleted",
                    _serializer.Serialize(item),
                    CurrentUserId,
                    _clock
                );

                events.Add(@event);
            }

            // Append all events first
            _eventStore.AppendRange(events);

            // Then delete from the inner store
            (_innerStore as IBulkStore<T>)?.Delete(items);
        }
    }
}
