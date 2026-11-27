using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Exceptions;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.EventSourcing.MongoDB
{
    /// <summary>
    /// The MongoDB override of the default event store (TASK-995). On a replica set or a sharded cluster, a batch is
    /// written in one transaction, so it is stored whole or not at all, even if the process dies. On a standalone server,
    /// which has no transactions, it is the default store.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A batch inside the caller's own MongoDB transaction (a <c>MongoDbUnitOfWork</c> on the same database) joins
    /// it, as <c>SqlEventStore</c> joins a <c>SqlUnitOfWork</c>. A conflict then aborts the caller's transaction too: the
    /// events were part of it.</item>
    /// <item>Otherwise, on a server with transactions, the batch runs in a transaction of its own. A duplicate id aborts
    /// it, and nothing of the batch is stored.</item>
    /// <item>Otherwise (standalone <c>mongod</c>) the default store's one-event-at-a-time append with compensation runs.
    /// Its gap, a crash partway leaving the batch's first events, is documented (decision 4).</item>
    /// </list>
    /// Whether the server has transactions is asked once per event store, with <c>hello</c>: a replica set member answers a
    /// <c>setName</c>, a <c>mongos</c> answers <c>msg: "isdbgrid"</c>.
    /// </remarks>
    public class MongoDBEventStore : AsyncStoreEventStore
    {
        private readonly AsyncMongoDBStore<EventRecord> _store;
        private bool? _supportsTransactions;

        /// <summary>Creates the event store over a MongoDB store of <see cref="EventRecord"/>.</summary>
        public MongoDBEventStore(AsyncMongoDBStore<EventRecord> store) : base(store)
        {
            _store = store;
        }

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            var client = _store.Client
                ?? throw new InvalidOperationException("The MongoDB store has no client. Call SetSettings() first.");

            if (DuplicateInBatch(batch) is { } duplicate)
                throw new EventStoreConcurrencyException(duplicate.AggregateGuid, duplicate.Version);
            var records = batch.Select(EventRecord.FromEvent).ToList();

            var ambient = Birko.Data.Stores.AmbientSession<IClientSessionHandle>.Find(_store.AmbientKey);
            if (ambient is { IsInTransaction: true })
            {
                // The caller's transaction: a conflict aborts it, so the lookup below may be refused and fall back.
                try
                {
                    await Store.CreateAsync(records, ct: ct).ConfigureAwait(false);
                }
                catch (StoreUniqueViolationException ex)
                {
                    var conflict = await FindConflictAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    throw new EventStoreConcurrencyException(conflict.AggregateGuid, conflict.Version, ex);
                }
                return;
            }

            if (!await SupportsTransactionsAsync(client.Client, ct).ConfigureAwait(false))
            {
                await base.AppendRangeCoreAsync(batch, ct).ConfigureAwait(false);
                return;
            }

            // The store's lazy init and the collection run before the transaction, not inside it: two first transactions
            // that create the collection implicitly collide at commit ("Collection namespace … is already in use",
            // measured).
            await _store.InitAsync(ct).ConfigureAwait(false);
            await EnsureCollectionAsync(client.Database, ct).ConfigureAwait(false);

            // Two transactions inserting one _id at once: the second gets a write conflict labelled TransientTransactionError,
            // not a duplicate key (measured). MongoDB's rule is to retry the whole transaction; by then the other has
            // committed, and the retry meets the taken id as a duplicate key.
            StoreUniqueViolationException? taken = null;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    taken = await TryAppendInTransactionAsync(records, ct).ConfigureAwait(false);
                    break;
                }
                catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") && attempt < MaxTransactionAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), ct).ConfigureAwait(false);
                }
            }
            if (taken == null) return;

            // Looked up after the rollback: inside the aborted transaction the lookup is refused (measured).
            var found = await FindConflictAsync(batch, CancellationToken.None).ConfigureAwait(false);
            throw new EventStoreConcurrencyException(found.AggregateGuid, found.Version, taken);
        }

        /// <summary>How many times a transaction that MongoDB reports as transient is run before its error is thrown.</summary>
        public const int MaxTransactionAttempts = 10;

        // One transaction: the batch committed (null), or refused for a taken id (the exception). Anything else throws.
        private async Task<StoreUniqueViolationException?> TryAppendInTransactionAsync(List<EventRecord> records, CancellationToken ct)
        {
            await using var uow = MongoDbUnitOfWork.FromStore(_store);
            await uow.BeginAsync(ct).ConfigureAwait(false);
            try
            {
                await Store.CreateAsync(records, ct: ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);
                return null;
            }
            catch (StoreUniqueViolationException ex)
            {
                return ex;
            }
            finally
            {
                if (uow.IsActive) await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private bool _collectionEnsured;

        private async Task EnsureCollectionAsync(IMongoDatabase database, CancellationToken ct)
        {
            if (_collectionEnsured) return;
            var name = nameof(EventRecord);
            var existing = await (await database.ListCollectionNamesAsync(
                new ListCollectionNamesOptions { Filter = new BsonDocument("name", name) }, ct).ConfigureAwait(false))
                .ToListAsync(ct).ConfigureAwait(false);
            if (existing.Count == 0)
            {
                try
                {
                    await database.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (MongoCommandException ex) when (ex.CodeName == "NamespaceExists")
                {
                    // another client created it first
                }
            }
            _collectionEnsured = true;
        }

        // Asked once per event store; a probe that fails is not remembered. Two appends racing on the first ask both ask.
        private async Task<bool> SupportsTransactionsAsync(IMongoClient client, CancellationToken ct)
        {
            if (_supportsTransactions is { } known) return known;
            var hello = await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct).ConfigureAwait(false);
            var supports = hello.Contains("setName") || (hello.TryGetValue("msg", out var msg) && msg == "isdbgrid");
            _supportsTransactions = supports;
            return supports;
        }
    }

    /// <summary>Registers <see cref="MongoDBEventStore"/> as the event store of the MongoDB backend (TASK-995).</summary>
    public static class MongoDBEventStoreOverrides
    {
        /// <summary>The name this override is registered under.</summary>
        public const string Name = "MongoDB";

        /// <summary>
        /// Makes <c>factory.GetEventStore(o => o.AddMongoDB())</c> answer a <see cref="MongoDBEventStore"/> for a
        /// <see cref="MongoDBStoreFactory"/>, over the factory's store of <see cref="EventRecord"/>.
        /// </summary>
        public static EventStoreOverrides AddMongoDB(this EventStoreOverrides overrides)
        {
            if (overrides is null) throw new ArgumentNullException(nameof(overrides));
            return overrides.Add(Name, f => f is MongoDBStoreFactory,
                f => new MongoDBEventStore((AsyncMongoDBStore<EventRecord>)f.GetAsyncStore<EventRecord>()));
        }
    }
}
