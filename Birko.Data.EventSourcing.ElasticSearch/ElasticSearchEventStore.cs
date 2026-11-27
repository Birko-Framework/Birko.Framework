using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;

namespace Birko.Data.EventSourcing.ElasticSearch
{
    /// <summary>
    /// The Elasticsearch override of the default event store (TASK-995): the same records and the same concurrency (a
    /// create of a taken (aggregate, version) id is a 409), plus an index refresh after every append.
    /// </summary>
    /// <remarks>
    /// Elasticsearch is near-real-time: a search sees a write only after the next refresh (by default each second).
    /// Every read of an event store is a search, so without the refresh the current version read 0 straight after an append
    /// and a writer that reloaded would append the same version again (measured: 5 of 9 contract tests failed on the
    /// default store). The refresh runs after the batch, and after a failed batch's compensation too, so the deleted
    /// events stop showing as well. When an append returns, every reader sees it.
    /// </remarks>
    public class ElasticSearchEventStore : AsyncStoreEventStore
    {
        private readonly AsyncElasticSearchStore<EventRecord> _store;

        /// <summary>Creates the event store over an Elasticsearch store of <see cref="EventRecord"/>.</summary>
        public ElasticSearchEventStore(AsyncElasticSearchStore<EventRecord> store) : base(store)
        {
            _store = store;
        }

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            try
            {
                await base.AppendRangeCoreAsync(batch, ct).ConfigureAwait(false);
            }
            finally
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }

        // Not cancellable: the writes it makes visible have happened.
        private async Task RefreshAsync()
        {
            var connector = _store.Connector;
            if (connector == null) return;
            var index = _store.GetIndexName();
            var response = await connector.Indices.RefreshAsync(index, null, CancellationToken.None).ConfigureAwait(false);
            if (response.IsValid) return;
            throw new InvalidOperationException(
                $"Events were written to {index}, but the index refresh failed, so reads may not see them yet. "
                + $"DebugInfo: {response.DebugInformation}", response.OriginalException);
        }
    }

    /// <summary>Registers <see cref="ElasticSearchEventStore"/> as the event store of the Elasticsearch backend (TASK-995).</summary>
    public static class ElasticSearchEventStoreOverrides
    {
        /// <summary>The name this override is registered under.</summary>
        public const string Name = "ElasticSearch";

        /// <summary>
        /// Makes <c>factory.GetEventStore(o => o.AddElasticSearch())</c> answer an <see cref="ElasticSearchEventStore"/> for
        /// an <see cref="ElasticSearchStoreFactory"/>, over the factory's store of <see cref="EventRecord"/>.
        /// </summary>
        public static EventStoreOverrides AddElasticSearch(this EventStoreOverrides overrides)
        {
            if (overrides is null) throw new ArgumentNullException(nameof(overrides));
            return overrides.Add(Name, f => f is ElasticSearchStoreFactory,
                f => new ElasticSearchEventStore((AsyncElasticSearchStore<EventRecord>)f.GetAsyncStore<EventRecord>()));
        }
    }
}
