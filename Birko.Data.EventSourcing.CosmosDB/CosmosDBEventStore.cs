using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.EventSourcing.CosmosDB
{
    /// <summary>
    /// The Cosmos DB event store (TASK-995): one container partitioned by aggregate, so an aggregate's events are one
    /// logical partition. An append is one transactional batch, all or nothing, and a stream's reads stay in its partition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it does not run over the generic store.</b> A Cosmos transaction covers one logical partition, and the Cosmos
    /// stores partition every document on its own id (TASK-614, TASK-240), so no batch could be atomic, and reading a
    /// stream or its current version would query across every partition. Partitioning by aggregate is the event-store layout
    /// Microsoft documents for Cosmos DB. The user chose it (TASK-995, 2026-11-27).
    /// </para>
    /// <list type="bullet">
    /// <item><b>Concurrency:</b> a document's <c>id</c> is its version, unique within the aggregate's partition, so Cosmos
    /// refuses a second writer of one version with 409. The batch's result names the conflicting operation.</item>
    /// <item><b>A batch holds one aggregate's events</b>, at most <see cref="MaxBatchSize"/> (Cosmos's limit per
    /// transactional batch). A batch that mixes aggregates is refused, because it could not be atomic.</item>
    /// <item><b>Time:</b> <c>ReadAllFrom</c> filters on the UTC ticks stored beside <c>occurredAt</c>, not on the date's
    /// text, and orders in memory (a multi-field <c>ORDER BY</c> would need a composite index).</item>
    /// </list>
    /// </remarks>
    public class CosmosDBEventStore : AbstractAsyncEventStore
    {
        /// <summary>The container's name in the factory's database.</summary>
        public const string ContainerName = "EventStreams";

        /// <summary>The partition key path: one logical partition per aggregate.</summary>
        public const string PartitionKeyPath = "/aggregateGuid";

        /// <summary>The most operations Cosmos DB accepts in one transactional batch.</summary>
        public const int MaxBatchSize = 100;

        private readonly CosmosClient _client;
        private readonly string _databaseName;
        private Container? _container;

        /// <summary>Creates the event store over <paramref name="client"/>, in database <paramref name="databaseName"/>.</summary>
        public CosmosDBEventStore(CosmosClient client, string databaseName)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("A database name is required.", nameof(databaseName));
            _databaseName = databaseName;
        }

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            var aggregate = batch[0].AggregateGuid;
            if (batch.Any(e => e.AggregateGuid != aggregate))
                throw new NotSupportedException(
                    "A Cosmos DB event store appends one aggregate's events per batch: a transaction covers one partition, and each "
                    + "aggregate is its own. Append each aggregate's events separately.");
            if (batch.Count > MaxBatchSize)
                throw new NotSupportedException(
                    $"A Cosmos DB transactional batch holds at most {MaxBatchSize} operations; this batch has {batch.Count} events.");
            if (DuplicateInBatch(batch) is { } duplicate)
                throw new EventStoreConcurrencyException(duplicate.AggregateGuid, duplicate.Version);

            var container = await ContainerAsync(ct).ConfigureAwait(false);
            var tx = container.CreateTransactionalBatch(new PartitionKey(Key(aggregate)));
            foreach (var e in batch) tx.CreateItem(EventDocument.From(e));
            using var response = await tx.ExecuteAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return;

            for (var i = 0; i < response.Count && i < batch.Count; i++)
            {
                if (response[i].StatusCode == HttpStatusCode.Conflict)
                    throw new EventStoreConcurrencyException(aggregate, batch[i].Version);
            }
            throw new InvalidOperationException(
                $"The Cosmos DB batch for aggregate {aggregate} failed with {(int)response.StatusCode} {response.StatusCode}: "
                + response.ErrorMessage);
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<IEvent>> ReadCoreAsync(Guid aggregateId, long? fromVersion, long? toVersion, CancellationToken ct)
        {
            var sql = "SELECT * FROM c WHERE c.aggregateGuid = @a"
                + (fromVersion.HasValue ? " AND c.version >= @from" : "")
                + (toVersion.HasValue ? " AND c.version <= @to" : "")
                + " ORDER BY c.version";
            var query = new QueryDefinition(sql).WithParameter("@a", Key(aggregateId));
            if (fromVersion.HasValue) query = query.WithParameter("@from", fromVersion.Value);
            if (toVersion.HasValue) query = query.WithParameter("@to", toVersion.Value);
            var docs = await QueryAsync<EventDocument>(query, aggregateId, ct).ConfigureAwait(false);
            return docs.Select(d => (IEvent)d.ToEvent()).ToList();
        }

        /// <inheritdoc />
        protected override async Task<long> GetVersionCoreAsync(Guid aggregateId, CancellationToken ct)
        {
            var query = new QueryDefinition("SELECT TOP 1 VALUE c.version FROM c WHERE c.aggregateGuid = @a ORDER BY c.version DESC")
                .WithParameter("@a", Key(aggregateId));
            var top = await QueryAsync<long>(query, aggregateId, ct).ConfigureAwait(false);
            return top.Count == 0 ? 0 : top[0];
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<IEvent>> ReadAllFromCoreAsync(DateTime from, CancellationToken ct)
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.occurredAtTicks >= @from")
                .WithParameter("@from", ToUtc(from).Ticks);
            var docs = await QueryAsync<EventDocument>(query, null, ct).ConfigureAwait(false);
            return docs.OrderBy(d => d.OccurredAtTicks).ThenBy(d => d.AggregateGuid).ThenBy(d => d.Version)
                .Select(d => (IEvent)d.ToEvent()).ToList();
        }

        private async Task<List<T>> QueryAsync<T>(QueryDefinition query, Guid? partition, CancellationToken ct)
        {
            var container = await ContainerAsync(ct).ConfigureAwait(false);
            var options = partition is { } p ? new QueryRequestOptions { PartitionKey = new PartitionKey(Key(p)) } : null;
            var results = new List<T>();
            using var feed = container.GetItemQueryIterator<T>(query, requestOptions: options);
            while (feed.HasMoreResults)
                results.AddRange(await feed.ReadNextAsync(ct).ConfigureAwait(false));
            return results;
        }

        // The database and the container are created once per event store, partitioned by aggregate. Concurrent creators
        // are fine: "if not exists" answers the existing one.
        private async Task<Container> ContainerAsync(CancellationToken ct)
        {
            if (_container != null) return _container;
            var database = (await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct).ConfigureAwait(false)).Database;
            var container = (await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(ContainerName, PartitionKeyPath), cancellationToken: ct).ConfigureAwait(false)).Container;
            var properties = (await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false)).Resource;
            if (properties.PartitionKeyPath != PartitionKeyPath)
                throw new InvalidOperationException(
                    $"Container {ContainerName} in {_databaseName} is partitioned on {properties.PartitionKeyPath}, not {PartitionKeyPath}; "
                    + "it was not created by this event store.");
            _container = container;
            return container;
        }

        private static string Key(Guid aggregate) => aggregate.ToString("D");

        private static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        /// <summary>One event as a Cosmos document. Public for the serializer only.</summary>
        public sealed class EventDocument
        {
            [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
            [JsonPropertyName("aggregateGuid")] public string AggregateGuid { get; set; } = string.Empty;
            [JsonPropertyName("version")] public long Version { get; set; }
            [JsonPropertyName("eventGuid")] public Guid EventGuid { get; set; }
            [JsonPropertyName("eventType")] public string EventType { get; set; } = string.Empty;
            [JsonPropertyName("occurredAt")] public DateTime OccurredAt { get; set; }
            [JsonPropertyName("occurredAtTicks")] public long OccurredAtTicks { get; set; }
            [JsonPropertyName("eventData")] public string EventData { get; set; } = string.Empty;
            [JsonPropertyName("metadata")] public string? Metadata { get; set; }
            [JsonPropertyName("userGuid")] public Guid? UserGuid { get; set; }

            internal static EventDocument From(IEvent e)
            {
                var occurred = ToUtc(e.OccurredAt);
                return new EventDocument
                {
                    Id = e.Version.ToString(CultureInfo.InvariantCulture),
                    AggregateGuid = Key(e.AggregateGuid),
                    Version = e.Version,
                    EventGuid = e.EventGuid,
                    EventType = e.EventType,
                    OccurredAt = occurred,
                    OccurredAtTicks = occurred.Ticks,
                    EventData = e.EventData,
                    Metadata = e.Metadata,
                    UserGuid = e.UserGuid,
                };
            }

            internal DomainEvent ToEvent() => new()
            {
                EventGuid = EventGuid,
                AggregateGuid = Guid.Parse(AggregateGuid),
                Version = Version,
                EventType = EventType,
                OccurredAt = DateTime.SpecifyKind(new DateTime(OccurredAtTicks), DateTimeKind.Utc),
                EventData = EventData,
                Metadata = Metadata,
                UserGuid = UserGuid,
            };
        }
    }

    /// <summary>Registers <see cref="CosmosDBEventStore"/> as the event store of the Cosmos DB backend (TASK-995).</summary>
    public static class CosmosDBEventStoreOverrides
    {
        /// <summary>The name this override is registered under.</summary>
        public const string Name = "CosmosDB";

        /// <summary>
        /// Makes <c>factory.GetEventStore(o => o.AddCosmosDB())</c> answer a <see cref="CosmosDBEventStore"/> for a
        /// <see cref="CosmosDBStoreFactory"/>, in the factory's database, on the client the factory already holds.
        /// </summary>
        public static EventStoreOverrides AddCosmosDB(this EventStoreOverrides overrides)
        {
            if (overrides is null) throw new ArgumentNullException(nameof(overrides));
            return overrides.Add(Name, f => f is CosmosDBStoreFactory, f =>
            {
                var factory = (CosmosDBStoreFactory)f;
                // The factory's cached store for EventRecord owns the client; asking for its client does not initialise it,
                // so its own '/id' container is never created.
                var client = ((AsyncCosmosDBStore<EventRecord>)factory.GetAsyncStore<EventRecord>()).Client
                    ?? throw new InvalidOperationException("The Cosmos DB factory's store has no client.");
                return new CosmosDBEventStore(client, factory.BackendSettings.Name);
            });
        }
    }
}
