using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-240 — RavenDB's half of the per-provider transaction proof.
///
/// <para>
/// RavenDB <i>looks</i> like the one backend that honoured the context on both reads and writes —
/// <c>AsyncRavenDBStore</c> does route <c>LoadAsync</c> and <c>Query&lt;T&gt;()</c> through the session as
/// well as <c>StoreAsync</c>. Measured against RavenDB 7.2, the read half splits. Load-by-id sees the
/// session's unsaved writes as of TASK-241, which aligned the document id with the entity Guid; a session
/// <b>query</b> never does, because it is answered by the server from indexes. That second half is a
/// RavenDB property, not a defect, and is why the capability declaration stays conservative — the
/// query-based reads are the common case.
/// </para>
///
/// <para>
/// <b>Which mode is promised is stated, not left to be discovered.</b> <see cref="RavenDbUnitOfWork"/>
/// opens its session with <see cref="TransactionMode.ClusterWide"/>, so the boundary is a cluster-wide
/// (compare-exchange backed) transaction. That is the stronger guarantee, and it is also the more
/// restrictive one: cluster-wide transactions do not support patching, attachments, counters or
/// time-series, and conflicts surface as concurrency exceptions at SaveChanges. A caller reads this off
/// <see cref="ITransactionCapabilities"/> rather than off the source.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to turn a skip into a failure.
/// </para>
/// </summary>
public class RavenTransactionBoundaryLiveTests
{
    private const string UrlEnv = "BIRKO_RAVEN_URL";

    private static string? Url => Environment.GetEnvironmentVariable(UrlEnv);
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public RavenTransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            return true;
        }
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
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

    private static AsyncRavenDBStore<TxDoc> NewStore(string db)
        => new(Url!, db);

    private static string NewDb() => "birko-task240-" + Guid.NewGuid().ToString("N");

    // ---------------------------------------------------------------- atomicity

    [Fact]
    public async Task Writes_in_a_rolled_back_session_never_reach_the_database()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        using var store = NewStore(db);
        try
        {
            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });

            await using (var uow = RavenDbUnitOfWork.FromStore(store))
            {
                await uow.BeginAsync();
                store.SetTransactionContext(uow.Context);
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "first" });
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "second" });
                await uow.RollbackAsync();
            }
            store.SetTransactionContext(null);

            // A Raven session is a unit of work: nothing is sent until SaveChanges, so a discarded
            // session leaves no trace at all.
            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().ContainSingle();
            rows[0].Name.Should().Be("seed");
        }
        finally
        {
            store.SetTransactionContext(null);
            try { await store.DestroyAsync(); } catch { }
        }
    }

    [Fact]
    public async Task A_committed_session_persists_every_write()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        using var store = NewStore(db);
        try
        {
            await using (var uow = RavenDbUnitOfWork.FromStore(store))
            {
                await uow.BeginAsync();
                store.SetTransactionContext(uow.Context);
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "first" });
                await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "second" });
                await uow.CommitAsync();
            }
            store.SetTransactionContext(null);

            (await store.ReadAsync(CancellationToken.None)).Count().Should().Be(2);
        }
        finally
        {
            store.SetTransactionContext(null);
            try { await store.DestroyAsync(); } catch { }
        }
    }

    /// <summary>
    /// A QUERY inside a Raven boundary does not see the session's unsaved writes — measured, not assumed.
    /// </summary>
    /// <remarks>
    /// The brief's survey marked RavenDB as honouring the context in its write paths, and the store does
    /// route reads through the session too, so it looked like the one backend that had both halves.
    /// Probed against RavenDB 7.2, only half holds: a session <c>Query</c> is answered by the server from
    /// indexes and never sees documents the session has not saved.
    /// <para>
    /// The other half was a genuine defect and is fixed — Load-by-id now consults the identity map, since
    /// TASK-241 aligned the document id with the entity Guid; that direction is pinned by
    /// <c>RavenDocumentIdLiveTests.Load_by_id_inside_a_session_now_sees_the_sessions_unsaved_write</c>.
    /// </para>
    /// <para>
    /// This one pins the part that is a RavenDB property rather than a bug, which is why
    /// <c>Capabilities.ReadsSeeUncommittedWrites</c> stays false: a single bool cannot say "id yes, query
    /// no", so it says the answer a caller is unsafe to get wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_query_inside_the_session_does_not_see_the_sessions_unsaved_writes()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        using var store = NewStore(db);
        try
        {
            await using var uow = RavenDbUnitOfWork.FromStore(store);
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);

            await store.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "unsaved", Amount = 7 });

            (await store.ReadFirstAsync(x => x.Name == "unsaved")).Should().BeNull(
                "a Raven session query is answered from server-side indexes; if this starts returning the "
              + "document, RavenDbUnitOfWork.Capabilities.ReadsSeeUncommittedWrites must be flipped to true");

            await uow.RollbackAsync();
            store.SetTransactionContext(null);
        }
        finally
        {
            store.SetTransactionContext(null);
            try { await store.DestroyAsync(); } catch { }
        }
    }

    /// <summary>
    /// A store outside the session sees nothing of an unsaved write, which is the isolation half.
    /// </summary>
    [Fact]
    public async Task A_store_outside_the_session_does_not_see_its_unsaved_writes()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        using var inside = NewStore(db);
        using var outside = NewStore(db);
        try
        {
            await inside.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "seed" });
            await Task.Delay(1500);

            await using var uow = RavenDbUnitOfWork.FromStore(inside);
            await uow.BeginAsync();
            inside.SetTransactionContext(uow.Context);
            await inside.CreateAsync(new TxDoc { Guid = Guid.NewGuid(), Name = "pending" });

            (await outside.ReadAsync(CancellationToken.None)).Count().Should().Be(1,
                "a store with no session must see only saved data");

            await uow.RollbackAsync();
            inside.SetTransactionContext(null);

            (await outside.ReadAsync(CancellationToken.None)).Count().Should().Be(1,
                "the discarded session must leave no trace");
        }
        finally
        {
            inside.SetTransactionContext(null);
            try { await inside.DestroyAsync(); } catch { }
        }
    }

    // ---------------------------------------------------------------- capabilities

    /// <summary>
    /// Needs no server — the point is that the promised mode is discoverable.
    /// </summary>
    [Fact]
    public void The_raven_unit_of_work_says_which_transaction_mode_it_promises()
    {
        using var docStore = new DocumentStore { Urls = new[] { "http://localhost:8080" }, Database = "unused" };
        var uow = new RavenDbUnitOfWork(docStore);

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.Atomic);
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.Cluster,
            "the session is opened with TransactionMode.ClusterWide, not single-node");
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeFalse(
            "deliberately conservative: Load-by-id does see the session's unsaved writes since TASK-241, "
          + "but every query-based read is answered from server-side indexes and does not. A single bool "
          + "must state the answer a caller is unsafe to get wrong, and query reads are the common case");
        uow.Capabilities.RequiresServerTopology.Should().BeFalse();
        uow.Capabilities.Limitations.Should().Contain("Cluster-wide",
            "cluster-wide transactions forbid patching, attachments, counters and time-series, and a "
          + "caller must be able to learn that without reading the source");
    }
}
