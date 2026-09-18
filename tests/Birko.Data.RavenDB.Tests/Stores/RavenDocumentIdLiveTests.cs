using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-241 — the entity <c>Guid</c> is the document id, so addressing a document by id works.
///
/// <para>
/// Both stores used to have two answers for what a document's id is: the write path called
/// <c>StoreAsync(data)</c> with no id, so Raven generated one from the collection
/// (<c>RavenDocumentIdLiveTests.Doc/1-A</c>), while every read, update and delete addressed
/// <c>data.Guid.ToString()</c>. Measured against RavenDB 7.2 with no transaction anywhere in the picture:
/// </para>
/// <list type="bullet">
/// <item><c>ReadAsync(Guid)</c> returned <c>null</c> for a document that exists;</item>
/// <item><c>DeleteAsync(entity)</c> deleted nothing <b>and reported success</b>;</item>
/// <item><c>UpdateAsync(entity)</c> created a duplicate instead of replacing.</item>
/// </list>
///
/// <para>
/// The silent no-op delete is the worst of the three: a caller deleting a record gets no exception and no
/// deletion. These tests therefore <b>count documents and read them back</b> rather than trusting that a
/// call did not throw — the defect's whole character was that it reported success.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure. There is
/// no offline half worth having here: the defect is entirely about what the server stores under which key.
/// </para>
/// </summary>
public class RavenDocumentIdLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public RavenDocumentIdLiveTests(ITestOutputHelper output) => _output = output;

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

    public class Doc : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    /// <summary>A second type deliberately sharing one identity with <see cref="Doc"/>.</summary>
    public class Profile : AbstractModel
    {
        public string? Nickname { get; set; }
    }

    private static string NewDb() => "birko-task241-" + Guid.NewGuid().ToString("N");

    private static AsyncRavenDBStore<Doc> NewStore(string db) => new(Url!, db);

    private static async Task Settle() => await Task.Delay(1500);

    // ---------------------------------------------------------------- read by id

    [Fact]
    public async Task A_created_document_can_be_read_back_by_its_Guid()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            var id = Guid.NewGuid();
            await store.CreateAsync(new Doc { Guid = id, Name = "alpha", Amount = 5 });

            var back = await store.ReadAsync(id, CancellationToken.None);

            back.Should().NotBeNull("the write and the read must agree about the document id");
            back!.Name.Should().Be("alpha");
            back.Amount.Should().Be(5);
            back.Guid.Should().Be(id);
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    [Fact]
    public async Task Reading_an_unknown_Guid_still_returns_null()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            await store.CreateAsync(new Doc { Guid = Guid.NewGuid(), Name = "alpha" });

            (await store.ReadAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull(
                "the fix must not make every id resolve to something");
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Deleting_an_entity_actually_removes_it()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            var id = Guid.NewGuid();
            await store.CreateAsync(new Doc { Guid = id, Name = "doomed" });
            await Settle();
            (await store.ReadAsync(CancellationToken.None)).Should().ContainSingle("seed");

            await store.DeleteAsync(new Doc { Guid = id, Name = "doomed" });
            await Settle();

            (await store.ReadAsync(CancellationToken.None)).Should().BeEmpty(
                "a delete that removes nothing while reporting success is the worst of the three symptoms");
            (await store.ReadAsync(id, CancellationToken.None)).Should().BeNull();
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    [Fact]
    public async Task Deleting_one_entity_leaves_the_others_alone()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            var keep = Guid.NewGuid();
            var drop = Guid.NewGuid();
            await store.CreateAsync(new Doc { Guid = keep, Name = "keep" });
            await store.CreateAsync(new Doc { Guid = drop, Name = "drop" });
            await Settle();

            await store.DeleteAsync(new Doc { Guid = drop, Name = "drop" });
            await Settle();

            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().ContainSingle();
            rows[0].Guid.Should().Be(keep);
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    // ---------------------------------------------------------------- update

    [Fact]
    public async Task Updating_an_entity_replaces_it_rather_than_duplicating()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            var id = Guid.NewGuid();
            await store.CreateAsync(new Doc { Guid = id, Name = "before", Amount = 1 });
            await Settle();

            await store.UpdateAsync(new Doc { Guid = id, Name = "after", Amount = 2 });
            await Settle();

            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().ContainSingle("an update must not leave the old document behind");
            rows[0].Name.Should().Be("after");
            rows[0].Amount.Should().Be(2);

            (await store.ReadAsync(id, CancellationToken.None))!.Name.Should().Be("after");
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    [Fact]
    public async Task Save_upserts_rather_than_duplicating()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            var id = Guid.NewGuid();
            await store.SaveAsync(new Doc { Guid = id, Name = "first" });
            await Settle();
            await store.SaveAsync(new Doc { Guid = id, Name = "second" });
            await Settle();

            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().ContainSingle();
            rows[0].Name.Should().Be("second");
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    // ---------------------------------------------------------------- bulk paths

    /// <summary>
    /// Guard the whole verb family or none of it: the bulk paths write through a different API
    /// (<c>BulkInsert</c>) and had the identical defect.
    /// </summary>
    [Fact]
    public async Task Bulk_created_documents_are_addressable_updatable_and_deletable_by_id()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            await store.CreateAsync(new[]
            {
                new Doc { Name = "a", Amount = 1 },
                new Doc { Name = "b", Amount = 2 },
                new Doc { Name = "c", Amount = 3 },
            });
            await Settle();

            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            rows.Should().HaveCount(3);

            // Every bulk-inserted document must be reachable by the Guid it was given.
            foreach (var row in rows)
            {
                (await store.ReadAsync(row.Guid!.Value, CancellationToken.None))
                    .Should().NotBeNull("bulk insert must use the same id scheme as everything else");
            }

            var target = rows.Single(x => x.Name == "b");
            target.Amount = 20;
            await store.UpdateAsync(new[] { target });
            await Settle();
            (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3, "bulk update must not duplicate");
            (await store.ReadAsync(target.Guid!.Value, CancellationToken.None))!.Amount.Should().Be(20);

            await store.DeleteAsync(new[] { target });
            await Settle();
            (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(2, "bulk delete must remove");
        }
        finally { try { await store.DestroyAsync(); } catch { } }
    }

    // ---------------------------------------------------------------- the collection prefix

    /// <summary>
    /// Two types deliberately sharing one identity must not collide.
    /// </summary>
    /// <remarks>
    /// This is why the id is <c>{collection}/{guid}</c> rather than the bare guid. A <c>User</c> and its
    /// <c>UserProfile</c> keyed by the same <c>Guid</c> is an ordinary modelling pattern, and a bare-guid
    /// id would make the second silently overwrite the first — the same class of silent data loss this
    /// task exists to remove.
    /// </remarks>
    [Fact]
    public async Task Two_types_sharing_one_Guid_do_not_overwrite_each_other()
    {
        if (!RequireServer()) return;
        var db = NewDb();
        using var docs = new AsyncRavenDBStore<Doc>(Url!, db);
        using var profiles = new AsyncRavenDBStore<Profile>(Url!, db);
        try
        {
            var shared = Guid.NewGuid();
            await docs.CreateAsync(new Doc { Guid = shared, Name = "the doc", Amount = 9 });
            await profiles.CreateAsync(new Profile { Guid = shared, Nickname = "the profile" });
            await Settle();

            var doc = await docs.ReadAsync(shared, CancellationToken.None);
            var profile = await profiles.ReadAsync(shared, CancellationToken.None);

            doc.Should().NotBeNull("a bare-guid id would have let the profile overwrite this");
            doc!.Name.Should().Be("the doc");
            doc.Amount.Should().Be(9);

            profile.Should().NotBeNull();
            profile!.Nickname.Should().Be("the profile");
        }
        finally { try { await docs.DestroyAsync(); } catch { } }
    }

    /// <summary>
    /// The id is derived from the store's own conventions, so it agrees with the document's collection.
    /// </summary>
    [Fact]
    public void The_document_id_is_the_collection_name_and_the_guid()
    {
        if (!RequireServer()) return;
        using var documentStore = new DocumentStore { Urls = new[] { Url! }, Database = NewDb() };
        documentStore.Initialize();

        var guid = Guid.NewGuid();
        var id = RavenDocumentId.For(documentStore, typeof(Doc), guid);

        id.Should().Be(documentStore.Conventions.GetCollectionName(typeof(Doc)) + "/" + guid);
        id.Should().EndWith(guid.ToString());
        id.Should().NotBe(guid.ToString(), "the collection prefix is what stops two types colliding");
    }

    // ---------------------------------------------------------------- inside a boundary

    /// <summary>
    /// TASK-240 declared <c>ReadsSeeUncommittedWrites = false</c> for Raven partly because Load-by-id was
    /// unusable. With the ids aligned, Load-by-id inside a session now consults the identity map.
    /// </summary>
    [Fact]
    public async Task Load_by_id_inside_a_session_now_sees_the_sessions_unsaved_write()
    {
        if (!RequireServer()) return;
        using var store = NewStore(NewDb());
        try
        {
            await using var uow = Birko.Data.RavenDB.UnitOfWork.RavenDbUnitOfWork.FromStore(store);
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);

            var id = Guid.NewGuid();
            await store.CreateAsync(new Doc { Guid = id, Name = "unsaved", Amount = 7 });

            var seen = await store.ReadAsync(id, CancellationToken.None);
            seen.Should().NotBeNull("the session tracks its own unsaved entity under the id it was stored with");
            seen!.Amount.Should().Be(7);

            await uow.RollbackAsync();
            store.SetTransactionContext(null);

            (await store.ReadAsync(id, CancellationToken.None)).Should().BeNull(
                "the rolled-back write never reached the database");
        }
        finally
        {
            store.SetTransactionContext(null);
            try { await store.DestroyAsync(); } catch { }
        }
    }

    // ---------------------------------------------------------------- the sync store

    /// <summary>
    /// The sync store had the identical defect and got the identical fix.
    /// </summary>
    /// <remarks>
    /// Guard the whole verb family or none of it: shipping a fixed async store beside an unfixed sync one
    /// would leave the same silent no-op delete reachable through a different door. And a parallel fix
    /// that nothing exercises is the trap the framework has been bitten by before — an offline test pins
    /// a helper, only an end-to-end run pins that anything calls it.
    /// </remarks>
    [Fact]
    public void The_sync_store_round_trips_updates_and_deletes_by_id()
    {
        if (!RequireServer()) return;
        using var store = new RavenDBStore<Doc>(Url!, NewDb());
        try
        {
            var id = Guid.NewGuid();
            store.Create(new Doc { Guid = id, Name = "before", Amount = 1 });

            var back = store.Read(id);
            back.Should().NotBeNull("the sync write and the sync read must agree about the document id");
            back!.Name.Should().Be("before");

            store.Update(new Doc { Guid = id, Name = "after", Amount = 2 });
            Thread.Sleep(1500);
            store.Read().Should().ContainSingle("a sync update must replace rather than duplicate");
            store.Read(id)!.Name.Should().Be("after");

            store.Delete(new Doc { Guid = id, Name = "after" });
            Thread.Sleep(1500);
            store.Read().Should().BeEmpty("a sync delete that removes nothing would report success too");
            store.Read(id).Should().BeNull();
        }
        finally { try { store.Destroy(); } catch { } }
    }
}
