using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Stores;
using Birko.Configuration;
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
    public class AsyncEventSourcingBulkStoreWrapper<TStore, T> : AsyncEventSourcingStoreWrapper<TStore, T>, IAsyncBulkStore<T>, IStoreWrapper<T>
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel, IEventSourced
    {
        /// <summary>
        /// Creates a new async event sourcing bulk store wrapper.
        /// </summary>
        /// <param name="innerStore">The inner async bulk store to wrap.</param>
        /// <param name="eventStore">The async event store for recording events.</param>
        /// <param name="serializer">The serializer for event data. Defaults to SystemJsonSerializer.</param>
        /// <param name="clock">Optional clock provider. Defaults to SystemDateTimeProvider.</param>
        public AsyncEventSourcingBulkStoreWrapper(TStore innerStore, IAsyncEventStore eventStore, Birko.Serialization.ISerializer? serializer = null, IDateTimeProvider? clock = null)
            : base(innerStore, eventStore, serializer, clock)
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

            foreach (var item in items)
            {
                storeDelegate?.Invoke(item);

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid ?? Guid.Empty, cancellationToken);
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

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                storeDelegate?.Invoke(item);

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
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

            var items = data.ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
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
            return UpdateAsync(filter, entity => updates.ApplyTo(entity), cancellationToken);
        }

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action,
        /// and records Updated events for each.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken cancellationToken = default)
        {
            var bulkStore = (_innerStore as IAsyncBulkStore<T>)!;
            var items = (await bulkStore.ReadAsync(filter, null, null, null, cancellationToken)).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                updateAction(item);

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
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

            await _eventStore.AppendRangeAsync(events, cancellationToken);
            await bulkStore.UpdateAsync(items, null, cancellationToken);
        }

        /// <summary>
        /// Asynchronously deletes all entities matching the filter and records Deleted events for each.
        /// </summary>
        public virtual async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            var bulkStore = (_innerStore as IAsyncBulkStore<T>)!;
            var items = (await bulkStore.ReadAsync(filter, null, null, null, cancellationToken)).ToList();
            var events = new List<IEvent>();

            foreach (var item in items)
            {
                if (item?.Guid == null) continue;

                var currentVersion = await _eventStore.GetVersionAsync(item.Guid.Value, cancellationToken);
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

            await _eventStore.AppendRangeAsync(events, cancellationToken);
            await bulkStore.DeleteAsync(items, cancellationToken);
        }
    }
}
