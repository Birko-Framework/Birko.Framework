using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// SH-H047, end-to-end against a real SQL backend.
///
/// <para>The in-memory suite (<see cref="TenantWriteGuardStoredRowTests"/>) proves the guard's logic; this
/// one proves it holds over the store the finding actually names. <c>DataBaseStore.UpdateCore</c> /
/// <c>DeleteCore</c> build their conditions from <c>GetPrimaryFields</c> — the Guid alone, with no tenant
/// term anywhere — so a cross-tenant write that gets past the wrapper reaches SQLite as a plain
/// <c>UPDATE … WHERE Guid = @g</c> and clobbers the victim's row.</para>
///
/// <para>It also exercises the two filter shapes the fix's stored-row read depends on against a real SQL
/// translator rather than a compiled delegate: <c>ModelByGuid</c> on the single paths, and
/// <c>ModelsByGuid</c>'s <c>Guids.Contains(x.Guid.Value)</c> on the bulk paths. A translation failure there
/// would look like "no stored row", which authorizes the write — so it has to be proved, not assumed.</para>
/// </summary>
public class TenantWriteGuardSqLiteEndToEndTests : IDisposable
{
    private readonly string _root;

    public TenantWriteGuardSqLiteEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-tenantguard-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("TenantDocs")]
    public class TenantDoc : AbstractLogModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    private static readonly Guid Attacker = Guid.NewGuid();
    private static readonly Guid Victim = Guid.NewGuid();

    private AsyncSQLiteStore<TenantDoc> NewStore(string dbName)
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = dbName });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(TenantDoc) });

        var store = new AsyncSQLiteStore<TenantDoc>();
        store.SetSettings(new SqLiteSettings(_root, dbName));
        return store;
    }

    private static TenantContext CtxFor(Guid tenant, string name)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant, name);
        return ctx;
    }

    /// <summary>Seeds one row owned by the victim tenant and returns its persisted Guid.</summary>
    private static async Task<Guid> SeedVictimRowAsync(AsyncSQLiteStore<TenantDoc> store)
    {
        await store.CreateAsync(new TenantDoc { TenantGuid = Victim, TenantName = "Victim", Name = "victim-data" });
        var row = (await store.ReadAsync(x => x.TenantGuid == Victim)).Single();
        return row.Guid!.Value;
    }

    [Fact]
    public async Task A_cross_tenant_update_does_not_reach_the_SQL_UPDATE()
    {
        var store = NewStore("update.db");
        var rowGuid = await SeedVictimRowAsync(store);

        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantDoc>, TenantDoc>(
            store, CtxFor(Attacker, "Attacker"));

        await wrapper.Awaiting(w => w.UpdateAsync(new TenantDoc
        {
            Guid = rowGuid,
            TenantGuid = Attacker,   // the caller's claim — the whole substance of the old guard
            TenantName = "Attacker",
            Name = "overwritten",
        })).Should().ThrowAsync<TenantMismatchException>();

        // Read back unscoped: the row must be exactly as the victim left it.
        var persisted = (await store.ReadAsync(x => x.TenantGuid == Victim)).Single();
        persisted.Name.Should().Be("victim-data");
        persisted.TenantGuid.Should().Be(Victim);
    }

    [Fact]
    public async Task A_cross_tenant_delete_does_not_reach_the_SQL_DELETE()
    {
        var store = NewStore("delete.db");
        var rowGuid = await SeedVictimRowAsync(store);

        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantDoc>, TenantDoc>(
            store, CtxFor(Attacker, "Attacker"));

        await wrapper.Awaiting(w => w.DeleteAsync(new TenantDoc
        {
            Guid = rowGuid,
            TenantGuid = Attacker,
            TenantName = "Attacker",
        })).Should().ThrowAsync<TenantMismatchException>();

        (await store.CountAsync(x => x.TenantGuid == Victim)).Should().Be(1);
    }

    [Fact]
    public async Task The_bulk_paths_resolve_stored_rows_through_a_real_SQL_IN_clause()
    {
        // ModelsByGuid renders Guids.Contains(x.Guid.Value) as a SQL IN. This catches the dangerous
        // direction — a translator that throws, or that matches too FEW rows, leaves the wrapper seeing
        // "no stored row", which authorizes the write. It does not isolate the harmless direction: a
        // predicate that degraded to match-all would resolve a superset and still refuse correctly, since
        // authorization looks each item up by its own Guid.
        var store = NewStore("bulk.db");
        var victimGuid = await SeedVictimRowAsync(store);

        await store.CreateAsync(new TenantDoc { TenantGuid = Attacker, TenantName = "Attacker", Name = "ours" });
        var ownGuid = (await store.ReadAsync(x => x.TenantGuid == Attacker)).Single().Guid!.Value;

        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantDoc>, TenantDoc>(
            store, CtxFor(Attacker, "Attacker"));

        await wrapper.Awaiting(w => w.UpdateAsync(new[]
        {
            new TenantDoc { Guid = ownGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "edited" },
            new TenantDoc { Guid = victimGuid, TenantGuid = Attacker, TenantName = "Attacker", Name = "overwritten" },
        })).Should().ThrowAsync<TenantMismatchException>();

        (await store.ReadAsync(x => x.TenantGuid == Victim)).Single().Name.Should().Be("victim-data");
        (await store.ReadAsync(x => x.TenantGuid == Attacker)).Single().Name.Should().Be("ours",
            "the batch is refused whole — the legitimate item is not written either");
    }

    [Fact]
    public async Task A_same_tenant_update_still_persists_and_cannot_re_home_the_row()
    {
        var store = NewStore("legit.db");
        await store.CreateAsync(new TenantDoc { TenantGuid = Attacker, TenantName = "Attacker", Name = "before" });
        var rowGuid = (await store.ReadAsync(x => x.TenantGuid == Attacker)).Single().Guid!.Value;

        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantDoc>, TenantDoc>(
            store, CtxFor(Attacker, "Attacker"));

        await wrapper.UpdateAsync(new TenantDoc
        {
            Guid = rowGuid,
            TenantGuid = Victim,      // an attempted re-home of a row the caller legitimately owns
            TenantName = "Victim",
            Name = "after",
        });

        (await store.CountAsync(x => x.TenantGuid == Victim)).Should().Be(0, "the row must not move tenant");
        var persisted = (await store.ReadAsync(x => x.TenantGuid == Attacker)).Single();
        persisted.Name.Should().Be("after", "the legitimate part of the update still applies");
        persisted.TenantName.Should().Be("Attacker");
    }
}
