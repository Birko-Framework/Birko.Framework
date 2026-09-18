using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.CosmosDB.UnitOfWork;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-240 — CosmosDB's half of the per-provider transaction proof: the single-partition limit is
/// <b>enforced</b> rather than silently dropped.
///
/// <para>
/// A <c>TransactionalBatch</c> is scoped to one logical partition key, and <see cref="AsyncCosmosDBStore{T}"/>
/// derives an item's partition key from its <c>Guid</c> — so <b>every document is its own logical
/// partition</b> and a boundary spanning two entities is impossible by construction. Before this task the
/// second <c>CreateItem</c> was accepted without complaint and the whole batch then failed at
/// <c>ExecuteAsync</c> with an opaque BadRequest; a caller that did not inspect the response simply lost
/// the writes. "The API let me type it" is exactly how a boundary stops covering what a caller thinks it
/// covers.
/// </para>
///
/// <para>
/// <b>Deliberately not gated on a live account.</b> The guard is client-side and the batch is built
/// client-side, so a <c>Container</c> handle obtained from a <c>CosmosClient</c> without ever contacting
/// a server is enough to exercise it end to end. Gating this behind an emulator would have made the one
/// assertion that matters skippable — the failure mode this task exists to remove.
/// </para>
/// </summary>
public class CosmosTransactionBoundaryTests
{
    // Never contacted: CosmosClient does not connect on construction, and with the Container ctor the
    // store's lazy init is a no-op (it early-returns when _cosmosClient is null).
    private const string OfflineConnection =
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public class TxDoc : AbstractModel
    {
        public string? Name { get; set; }
    }

    private static Container OfflineContainer()
        => new CosmosClient(OfflineConnection).GetContainer("birko-task240", "TxDoc");

    private static (AsyncCosmosDBStore<TxDoc> Store, TransactionalBatch Batch, Guid Pinned) NewBatchStore()
    {
        var container = OfflineContainer();
        var store = new AsyncCosmosDBStore<TxDoc>(container);
        var pinned = Guid.NewGuid();
        var batch = container.CreateTransactionalBatch(new PartitionKey(pinned.ToString()));
        store.SetTransactionContext(batch);
        return (store, batch, pinned);
    }

    // ---------------------------------------------------------------- the limit is enforced

    [Fact]
    public async Task A_second_entity_in_one_batch_is_refused_rather_than_silently_dropped()
    {
        var (store, _, pinned) = NewBatchStore();

        await store.CreateAsync(new TxDoc { Guid = pinned, Name = "first" });

        var second = Guid.NewGuid();
        var act = async () => await store.CreateAsync(new TxDoc { Guid = second, Name = "second" });

        var ex = (await act.Should().ThrowAsync<CosmosTransactionScopeException>(
            "a Cosmos batch cannot span two logical partitions, and this store partitions by Guid"))
            .Which;

        // The message must name BOTH keys — an opaque BadRequest at commit time is what this replaces.
        ex.BatchPartitionKey.Should().Be(pinned.ToString());
        ex.AttemptedPartitionKey.Should().Be(second.ToString());
        ex.Message.Should().Contain("single logical partition");
    }

    [Fact]
    public async Task The_same_entity_may_be_written_more_than_once_in_one_batch()
    {
        var (store, _, pinned) = NewBatchStore();

        await store.CreateAsync(new TxDoc { Guid = pinned, Name = "created" });

        // Same partition key — a create-then-update of one document is the ONE boundary Cosmos can
        // actually honour here, and it must not be refused.
        var act = async () => await store.UpdateAsync(new TxDoc { Guid = pinned, Name = "updated" });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_bulk_write_spanning_two_entities_is_refused_on_the_second_item()
    {
        var (store, _, pinned) = NewBatchStore();

        var act = async () => await store.CreateAsync(new[]
        {
            new TxDoc { Guid = pinned, Name = "first" },
            new TxDoc { Guid = Guid.NewGuid(), Name = "second" },
        });

        await act.Should().ThrowAsync<CosmosTransactionScopeException>(
            "the bulk paths add to the same batch and must enforce the same limit as the single-item ones");
    }

    [Fact]
    public async Task A_delete_of_a_different_entity_is_refused_too()
    {
        var (store, _, pinned) = NewBatchStore();

        await store.CreateAsync(new TxDoc { Guid = pinned, Name = "first" });

        var act = async () => await store.DeleteAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "other" });
        await act.Should().ThrowAsync<CosmosTransactionScopeException>(
            "guard the whole verb family or none of it — a refused Create beside an escaping Delete is "
          + "the same defect wearing a quieter coat");
    }

    [Fact]
    public async Task Clearing_the_context_releases_the_pinned_partition()
    {
        var container = OfflineContainer();
        var store = new AsyncCosmosDBStore<TxDoc>(container);

        var first = Guid.NewGuid();
        store.SetTransactionContext(container.CreateTransactionalBatch(new PartitionKey(first.ToString())));
        await store.CreateAsync(new TxDoc { Guid = first, Name = "first" });

        // A NEW batch is a new boundary and must start unpinned, or the store would refuse for the rest
        // of its life after one transaction.
        var second = Guid.NewGuid();
        store.SetTransactionContext(container.CreateTransactionalBatch(new PartitionKey(second.ToString())));
        var act = async () => await store.CreateAsync(new TxDoc { Guid = second, Name = "second" });
        await act.Should().NotThrowAsync();

        store.SetTransactionContext(null);
    }

    [Fact]
    public async Task Without_a_batch_nothing_is_pinned_and_nothing_is_refused()
    {
        var store = new AsyncCosmosDBStore<TxDoc>(OfflineContainer());
        store.TransactionContext.Should().BeNull();

        // No boundary: these go to the container directly. They fail on the network rather than on the
        // guard, which is the point — the guard must not fire when there is no batch to overflow.
        var act = async () => await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "a" });
        (await act.Should().ThrowAsync<Exception>()).Which
            .Should().NotBeOfType<CosmosTransactionScopeException>();
    }

    // ---------------------------------------------------------------- capabilities

    [Fact]
    public void The_cosmos_unit_of_work_states_the_single_partition_limit()
    {
        var container = OfflineContainer();
        var uow = new CosmosDbUnitOfWork(container, new PartitionKey(Guid.NewGuid().ToString()));

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.Atomic);
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.SinglePartition,
            "a boundary spanning two partitions cannot exist, whatever the API lets a caller type");
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeFalse(
            "a TransactionalBatch buffers writes client-side until ExecuteAsync and exposes no read, so "
          + "read-then-write logic inside a Cosmos boundary reads the pre-transaction state");
        uow.Capabilities.Limitations.Should().Contain("single logical partition");
    }
}
