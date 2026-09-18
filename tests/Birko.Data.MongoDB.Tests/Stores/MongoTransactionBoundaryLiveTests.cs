using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-240 — MongoDB's half of the per-provider transaction proof.
///
/// <para>
/// <b>This needs a replica set, and that is the whole point.</b> MongoDB multi-document transactions
/// require a replica set or sharded cluster; against a standalone <c>mongod</c> <c>BeginAsync</c> succeeds
/// and the first write fails at runtime. A test run against a standalone server would therefore assert
/// nothing at all while reporting green — which is the failure mode this task exists to remove, so the
/// suite checks the topology and says so rather than quietly passing.
/// </para>
///
/// <para>
/// The read-your-own-writes tests pin a defect this task fixed: every read path
/// (<c>ReadCoreAsync</c>, <c>CountCoreAsync</c>, <c>ReadAllAsync</c>) used to call
/// <c>Collection.Find(...)</c> with no session, so a caller inside a transaction could not see its own
/// uncommitted writes. The write paths did pass the session, which is what made the store look
/// transactional while read-then-write logic silently read the pre-transaction snapshot.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to turn a skip into a failure.
/// Start one with:
/// <c>docker run -d -p 27017:27017 mongo:7 --replSet rs0 --bind_ip_all</c> then
/// <c>mongosh --eval "rs.initiate()"</c>.
/// </para>
/// </summary>
public class MongoTransactionBoundaryLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    private static string? Host => Environment.GetEnvironmentVariable(HostEnv);
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public MongoTransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST (a REPLICA SET — transactions "
                             + "do not exist on a standalone mongod); set BIRKO_REQUIRE_LIVE to make its "
                             + "absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    public class TxDoc : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static AsyncMongoDBStore<TxDoc> NewStore(string db)
    {
        var store = new AsyncMongoDBStore<TxDoc>();
        store.SetSettings(new Settings(Host!, db));
        return store;
    }

    private static string NewDb() => "birko_task240_" + Guid.NewGuid().ToString("N");

    private static async Task<int> CountAsync(AsyncMongoDBStore<TxDoc> store)
        => (await store.ReadAsync(CancellationToken.None)).Count();

    /// <summary>
    /// Proves the server really is a replica set, so nothing below can be a vacuous pass.
    /// </summary>
    [Fact]
    public async Task The_server_supports_transactions_at_all()
    {
        if (!RequireServer()) return;
        var store = NewStore(NewDb());
        try
        {
            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "probe" });

            await using var uow = new MongoDbUnitOfWork(store.Client!);
            var act = async () =>
            {
                await uow.BeginAsync();
                store.SetTransactionContext(uow.Context);
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "in-tx" });
                await uow.CommitAsync();
            };

            await act.Should().NotThrowAsync(
                "a standalone mongod raises 'Transaction numbers are only allowed on a replica set member "
              + "or mongos' here — if this fails, the tests below would be asserting nothing");
        }
        finally
        {
            store.SetTransactionContext(null);
            await store.DestroyAsync();
        }
    }

    [Fact]
    public async Task Two_writes_in_one_boundary_are_both_discarded_when_the_boundary_rolls_back()
    {
        if (!RequireServer()) return;
        var store = NewStore(NewDb());
        try
        {
            // The collection must exist before the transaction: MongoDB cannot create one implicitly
            // inside a transaction on older servers, and that would fail for the wrong reason.
            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });

            await using (var uow = new MongoDbUnitOfWork(store.Client!))
            {
                await uow.BeginAsync();
                store.SetTransactionContext(uow.Context);
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "first" });
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "second" });
                await uow.RollbackAsync();
            }
            store.SetTransactionContext(null);

            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().ContainSingle("only the seed may survive a rolled-back boundary");
            rows[0].Name.Should().Be("seed");
        }
        finally
        {
            store.SetTransactionContext(null);
            await store.DestroyAsync();
        }
    }

    [Fact]
    public async Task A_committed_boundary_persists_every_write()
    {
        if (!RequireServer()) return;
        var store = NewStore(NewDb());
        try
        {
            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });

            await using (var uow = new MongoDbUnitOfWork(store.Client!))
            {
                await uow.BeginAsync();
                store.SetTransactionContext(uow.Context);
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "first" });
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "second" });
                await uow.CommitAsync();
            }
            store.SetTransactionContext(null);

            (await CountAsync(store)).Should().Be(3);
        }
        finally
        {
            store.SetTransactionContext(null);
            await store.DestroyAsync();
        }
    }

    /// <summary>
    /// The defect this task fixed on the Mongo side: reads bypassed the session entirely.
    /// </summary>
    [Fact]
    public async Task A_read_inside_the_boundary_sees_the_boundarys_own_uncommitted_writes()
    {
        if (!RequireServer()) return;
        var store = NewStore(NewDb());
        try
        {
            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });

            await using var uow = new MongoDbUnitOfWork(store.Client!);
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);

            var inside = Guid.NewGuid();
            await store.CreateAsync(new TxDoc { Guid = inside, Name = "inside", Amount = 7 });

            // Every one of these went to Collection.Find/CountDocuments with no session before TASK-240,
            // so each returned the pre-transaction snapshot: 1, null and 1 respectively.
            (await CountAsync(store)).Should().Be(2, "bulk ReadCoreAsync must run in the session");
            (await store.ReadFirstAsync(x => x.Name == "inside"))
                .Should().NotBeNull("single ReadCoreAsync must run in the session");
            (await store.CountAsync(ct: CancellationToken.None)).Should().Be(2,
                "CountCoreAsync must run in the session");

            await uow.RollbackAsync();
            store.SetTransactionContext(null);

            (await CountAsync(store)).Should().Be(1, "the uncommitted write must be gone");
        }
        finally
        {
            store.SetTransactionContext(null);
            await store.DestroyAsync();
        }
    }

    /// <summary>
    /// A store with no session must not see another flow's uncommitted writes.
    /// </summary>
    [Fact]
    public async Task A_store_outside_the_boundary_does_not_see_its_uncommitted_rows()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        var inside = NewStore(db);
        var outside = NewStore(db);
        try
        {
            await inside.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });

            await using var uow = new MongoDbUnitOfWork(inside.Client!);
            await uow.BeginAsync();
            inside.SetTransactionContext(uow.Context);
            await inside.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "inside" });

            (await CountAsync(inside)).Should().Be(2, "the writer sees its own uncommitted row");
            (await CountAsync(outside)).Should().Be(1,
                "a store with no session must see only committed data");

            await uow.RollbackAsync();
            inside.SetTransactionContext(null);

            (await CountAsync(outside)).Should().Be(1);
        }
        finally
        {
            inside.SetTransactionContext(null);
            await inside.DestroyAsync();
        }
    }

    // ---------------------------------------------------------------- capabilities

    /// <summary>
    /// Needs no server: the point is that the contract states the replica-set requirement out loud.
    /// </summary>
    [Fact]
    public void The_mongo_unit_of_work_declares_that_it_needs_a_replica_set()
    {
        var uow = new MongoDbUnitOfWork(new MongoClient("mongodb://localhost:27017"));

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.Atomic);
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.Cluster);
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeTrue(
            "TASK-240 routed every read path through the session");
        uow.Capabilities.RequiresServerTopology.Should().BeTrue(
            "a standalone mongod cannot honour this boundary, and a caller must be able to find that out "
          + "without discovering it at the first write");
        uow.Capabilities.Limitations.Should().Contain("replica set");
    }
}
