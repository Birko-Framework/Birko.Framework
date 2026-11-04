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
        /// <param name="currentUser">Reads the current user for each event.</param>
        public EventSourcingBulkStoreWrapper(TStore innerStore, IEventStore eventStore, Birko.Serialization.ISerializer? serializer = null, IDateTimeProvider? clock = null, Func<Guid?>? currentUser = null)
            : base(innerStore, eventStore, serializer, clock, currentUser)
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

            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                item = StoreDelegates.Apply(storeDelegate, item);
                items[itemIndex] = item; // the list is what gets persisted

                // Assign the Guid up front so the event AggregateId, version lookup, and inner-store
                // row all key off the SAME id (CR-C06); the throwaway Guid.NewGuid() was never written
                // back, so the inner bulk store assigned a different Guid and orphaned the event.
                item.EnsureGuid();

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Created", newVersion);

                events.Add(@event);
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

            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                if (item?.Guid == null)
                {
                    // TASK-713: refused before any event is appended. It used to be skipped without an event while the
                    // events of the items before it were appended and the inner store then refused the call.
                    throw new Data.Exceptions.EntityNotFoundException(typeof(T), item?.Guid);
                }

                item = StoreDelegates.Apply(storeDelegate, item);
                items[itemIndex] = item; // the list is what gets persisted

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Updated", newVersion);

                events.Add(@event);
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

            // TASK-713: an item without a Guid names no row: it gets no event and is not passed on.
            var items = data.Where(x => x?.Guid != null).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Deleted", newVersion);

                events.Add(@event);
            }

            // Append all events first
            _eventStore.AppendRange(events);

            // Then delete from the inner store
            (_innerStore as IBulkStore<T>)?.Delete(items);
        }

        /// <summary>
        /// Updates specific properties on matching entities and records Updated events.
        /// Falls back to read-modify-save to ensure event recording.
        /// </summary>
        public virtual void Update(Expression<Func<T, bool>> filter, Data.Stores.PropertyUpdate<T> updates)
        {
            RequireBoundedFilter(filter, "update");
            if (updates.Assignments.Count == 0) return; // SH-M032 (TASK-170)
            Update(filter, entity => updates.ApplyTo(entity));
        }

        /// <summary>
        /// Updates all entities matching the filter by applying the specified action,
        /// and records Updated events for each.
        /// </summary>
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            RequireBoundedFilter(filter, "update");
            var bulkStore = _innerStore as IBulkStore<T>;
            if (bulkStore == null) return;

            var items = bulkStore.Read(filter, null, null, null).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                updateAction(item);

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Updated", newVersion);

                events.Add(@event);
            }

            _eventStore.AppendRange(events);
            bulkStore.Update(items);
        }

        /// <summary>
        /// Deletes all entities matching the filter and records Deleted events for each.
        /// </summary>
        public virtual void Delete(Expression<Func<T, bool>> filter)
        {
            RequireBoundedFilter(filter, "delete");
            var bulkStore = _innerStore as IBulkStore<T>;
            if (bulkStore == null) return;

            var items = bulkStore.Read(filter, null, null, null).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = _eventStore.GetVersion(item.Guid.Value);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Deleted", newVersion);

                events.Add(@event);
            }

            _eventStore.AppendRange(events);
            bulkStore.Delete(items);
        }
        /// <summary>
        /// TASK-713 (rules 1-3): a filtered write needs a filter that names rows. A null or always-true one read every row
        /// and rewrote or deleted each, with an event each; this wrapper offers no whole-table door.
        /// </summary>
        private static void RequireBoundedFilter(Expression<Func<T, bool>> filter, string operation)
        {
            if (filter is null)
            {
                throw new ArgumentNullException(nameof(filter), $"A filtered event-sourced {operation} on {typeof(T).Name} needs a filter.");
            }
            Birko.Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name, "a filter that names the rows");
        }
    }
}
