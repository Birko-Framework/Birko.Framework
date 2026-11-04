using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Stores;
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
    /// An async bulk store wrapper that adds event sourcing capabilities to any async bulk store.
    /// All changes are recorded as events in the event store.
    /// </summary>
    /// <typeparam name="TStore">The type of inner async bulk store.</typeparam>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class AsyncEventSourcingBulkStoreWrapper<TStore, T> : AsyncEventSourcingStoreWrapper<TStore, T>, IAsyncBulkStore<T>, IStoreWrapper<T>, IAsyncAggregatableStore<T>
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel, IEventSourced
    {
        /// <summary>Forwards aggregation: this decorator does not scope reads (TASK-631).</summary>
        public Task<IReadOnlyList<AggregateResult>> AggregateAsync(AggregateQuery<T> query, CancellationToken ct = default)
            => StoreChain.AggregateThrough(_innerStore, query, ct);

        /// <summary>
        /// Creates a new async event sourcing bulk store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner async bulk store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        /// <param name="serializer">The serializer for event data. Defaults to SystemJsonSerializer.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        /// <param name="currentUser">Reads the current user for each event.</param>
        public AsyncEventSourcingBulkStoreWrapper(TStore innerStore, IAsyncEventStore eventStore, Birko.Serialization.ISerializer? serializer = null, IDateTimeProvider? clock = null, Func<Guid?>? currentUser = null)
            : base(innerStore, eventStore, serializer, clock, currentUser)
        {
        }

        /// <summary>
        /// Creates multiple items and records Created events.
        /// </summary>
        public virtual async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken cancellationToken = default)
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

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Created", newVersion);

                events.Add(@event);
            }

            // Append all events first
            await _eventStore.AppendRangeAsync(events, cancellationToken);

            // Then persist to the inner store
            await (_innerStore as IAsyncBulkStore<T>)!.CreateAsync(items, null, cancellationToken);
        }

        /// <summary>
        /// Reads multiple items.
        /// </summary>
        public virtual async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        {
            return await (_innerStore as IAsyncBulkStore<T>)!.ReadAsync(filter, orderBy, limit, offset, cancellationToken);
        }

        /// <summary>
        /// Reads all items.
        /// </summary>
        public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken cancellationToken = default)
        {
            return await ReadAsync(null, null, null, null, cancellationToken);
        }

        /// <summary>
        /// Updates multiple items and records Updated events.
        /// </summary>
        public virtual async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken cancellationToken = default)
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

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Updated", newVersion);

                events.Add(@event);
            }

            // Append all events first
            await _eventStore.AppendRangeAsync(events, cancellationToken);

            // Then persist to the inner store
            await (_innerStore as IAsyncBulkStore<T>)!.UpdateAsync(items, null, cancellationToken);
        }

        /// <summary>
        /// Deletes multiple items and records Deleted events.
        /// </summary>
        public virtual async Task DeleteAsync(IEnumerable<T> data, CancellationToken cancellationToken = default)
        {
            if (data == null) return;

            // TASK-713: an item without a Guid names no row: it gets no event and is not passed on.
            var items = data.Where(x => x?.Guid != null).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Deleted", newVersion);

                events.Add(@event);
            }

            // Append all events first
            await _eventStore.AppendRangeAsync(events, cancellationToken);

            // Then delete from the inner store
            await (_innerStore as IAsyncBulkStore<T>)!.DeleteAsync(items, cancellationToken);
        }

        /// <summary>
        /// Asynchronously updates specific properties on matching entities and records Updated events.
        /// Falls back to read-modify-save to ensure event recording.
        /// </summary>
        public virtual Task UpdateAsync(Expression<Func<T, bool>> filter, Data.Stores.PropertyUpdate<T> updates, CancellationToken cancellationToken = default)
        {
            RequireBoundedFilter(filter, "update");
            if (updates.Assignments.Count == 0) return Task.CompletedTask; // SH-M032 (TASK-170)
            return UpdateAsync(filter, entity => updates.ApplyTo(entity), cancellationToken);
        }

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action,
        /// and records Updated events for each.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken cancellationToken = default)
        {
            RequireBoundedFilter(filter, "update");
            var bulkStore = (_innerStore as IAsyncBulkStore<T>)!;
            var items = (await bulkStore.ReadAsync(filter, null, null, null, cancellationToken)).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                updateAction(item);

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Updated", newVersion);

                events.Add(@event);
            }

            await _eventStore.AppendRangeAsync(events, cancellationToken);
            await bulkStore.UpdateAsync(items, null, cancellationToken);
        }

        /// <summary>
        /// Asynchronously deletes all entities matching the filter and records Deleted events for each.
        /// </summary>
        public virtual async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            RequireBoundedFilter(filter, "delete");
            var bulkStore = (_innerStore as IAsyncBulkStore<T>)!;
            var items = (await bulkStore.ReadAsync(filter, null, null, null, cancellationToken)).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
                var newVersion = currentVersion + 1;

                var @event = NewEvent(item, "Deleted", newVersion);

                events.Add(@event);
            }

            await _eventStore.AppendRangeAsync(events, cancellationToken);
            await bulkStore.DeleteAsync(items, cancellationToken);
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
