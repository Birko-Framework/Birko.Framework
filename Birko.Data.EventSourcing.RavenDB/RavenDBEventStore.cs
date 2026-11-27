using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Exceptions;
using Birko.Data.RavenDB.Stores;

namespace Birko.Data.EventSourcing.RavenDB
{
    /// <summary>
    /// The RavenDB override of the default event store (TASK-995): the same records and the same concurrency, but a batch
    /// is written in one session, so it is stored whole or not at all, even if the process dies.
    /// </summary>
    /// <remarks>
    /// The default store writes one event at a time and deletes its own events when a later one conflicts, which leaves the
    /// first events of a batch behind if the process dies in between. RavenDB's bulk create saves the whole batch in one
    /// <c>SaveChanges</c>, one server transaction: a batch with a taken id in the middle stored nothing (measured,
    /// TASK-995), so no compensation is needed. RavenDB does not say which document collided, so the conflicting version is
    /// looked up afterwards.
    /// </remarks>
    public class RavenDBEventStore : AsyncStoreEventStore
    {
        /// <summary>Creates the event store over a RavenDB store of <see cref="EventRecord"/>.</summary>
        public RavenDBEventStore(AsyncRavenDBStore<EventRecord> store) : base(store)
        {
        }

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            // A version twice in one batch would be refused by the server too; naming it here costs no round trip.
            if (DuplicateInBatch(batch) is { } duplicate)
                throw new EventStoreConcurrencyException(duplicate.AggregateGuid, duplicate.Version);
            var records = batch.Select(EventRecord.FromEvent).ToList();
            try
            {
                await Store.CreateAsync(records, ct: ct).ConfigureAwait(false);
            }
            catch (StoreUniqueViolationException ex)
            {
                var conflict = await FindConflictAsync(batch, CancellationToken.None).ConfigureAwait(false);
                throw new EventStoreConcurrencyException(conflict.AggregateGuid, conflict.Version, ex);
            }
        }
    }

    /// <summary>Registers <see cref="RavenDBEventStore"/> as the event store of the RavenDB backend (TASK-995).</summary>
    public static class RavenDBEventStoreOverrides
    {
        /// <summary>The name this override is registered under.</summary>
        public const string Name = "RavenDB";

        /// <summary>
        /// Makes <c>factory.GetEventStore(o => o.AddRavenDB())</c> answer a <see cref="RavenDBEventStore"/> for a
        /// <see cref="RavenDBStoreFactory"/>, over the factory's store of <see cref="EventRecord"/>.
        /// </summary>
        public static EventStoreOverrides AddRavenDB(this EventStoreOverrides overrides)
        {
            if (overrides is null) throw new ArgumentNullException(nameof(overrides));
            return overrides.Add(Name, f => f is RavenDBStoreFactory,
                f => new RavenDBEventStore((AsyncRavenDBStore<EventRecord>)f.GetAsyncStore<EventRecord>()));
        }
    }
}
