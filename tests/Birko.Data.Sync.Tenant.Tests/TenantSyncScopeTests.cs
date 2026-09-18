using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Sync.Tenant.Models;
using Birko.Data.Sync.Tenant.Providers;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// SH-H050 / SH-H051 / SH-H052 — <c>TenantSyncProvider</c> scoped only its SAVE predicates, so every other
/// path spanned every tenant:
/// <list type="bullet">
/// <item>SH-H052 — the fetch predicates were untouched (<c>ApplyTenantFiltering</c>'s own XML doc said so),
/// so every tenant's rows entered <c>localDict</c>/<c>remoteDict</c> and <c>PreviewAsync</c> under tenant
/// <i>t</i> enumerated and version-hashed another tenant's entities.</item>
/// <item>SH-H051 — the <c>SyncAction.Delete</c> arm consulted no predicate at all (unlike Create/Update),
/// so a row belonging to tenant <i>u</i> that resolved to Delete under a run scoped to <i>t</i> was
/// deleted.</item>
/// <item>SH-H050 — knowledge/last-sync were keyed by <c>options.TenantGuid</c> while the save filters read
/// the ambient tenant, so <c>SyncAsync(new TenantSyncOptions { TenantGuid = u })</c> with no ambient tenant
/// — the documented background-job shape — installed <b>no save predicate at all</b> and wrote every
/// tenant's items into both stores.</item>
/// </list>
/// The fix moves the tenant term onto the fetch predicates and resolves the run's tenant once, so the
/// read / compare / preview / delete / knowledge paths are scoped by construction rather than each needing
/// its own guard.
/// </summary>
public class TenantSyncScopeTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    /// <summary>Tenant-scoped entity with a nullable TenantGuid (the shape the earlier suites use).</summary>
    private class TenantItem : AbstractModel
    {
        public Guid? TenantGuid { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>
    /// Tenant-scoped entity with a NON-nullable TenantGuid — the shape canonical <c>ITenant</c> entities
    /// have. <c>BuildTenantPredicate</c> emits a different comparison for each, so both are exercised.
    /// </summary>
    private class StrictTenantItem : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>Entity with no TenantGuid property at all — not tenant-scoped, must stay unaffected.</summary>
    private class PlainItem : AbstractModel
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// Records every tenant it is keyed by, so "knowledge is keyed to the tenant the writes were scoped to"
    /// is assertable on the same run rather than inferred.
    /// </summary>
    private sealed class RecordingKnowledgeStore : ISyncKnowledgeStore
    {
        private readonly Dictionary<Guid, ISyncKnowledgeItem> _existing;
        private readonly DateTime? _lastSyncTime;

        public RecordingKnowledgeStore(
            DateTime? lastSyncTime = null,
            Dictionary<Guid, ISyncKnowledgeItem>? existing = null)
        {
            _lastSyncTime = lastSyncTime;
            _existing = existing ?? new Dictionary<Guid, ISyncKnowledgeItem>();
        }

        public List<Guid?> ReadTenantIds { get; } = new();
        public List<Guid?> WrittenLastSyncTenantIds { get; } = new();
        public List<ISyncKnowledgeItem> Written { get; } = new();

        public Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
        {
            ReadTenantIds.Add(tenantId);
            return Task.FromResult(_existing);
        }

        public Task<DateTime?> GetLastSyncTimeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
        {
            ReadTenantIds.Add(tenantId);
            return Task.FromResult(_lastSyncTime);
        }

        public Task UpdateKnowledgeAsync(IEnumerable<ISyncKnowledgeItem> items, CancellationToken ct = default)
        {
            Written.AddRange(items);
            return Task.CompletedTask;
        }

        public Task SetLastSyncTimeAsync(string scope, Guid? tenantId, DateTime syncTime, CancellationToken ct = default)
        {
            WrittenLastSyncTenantIds.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A store that DISCARDS the filter it is handed — standing in for a backend whose filter translation
    /// silently widens to match-all. This family has shipped that defect more than once (a NEST request with
    /// a null Query; an empty IN rendered always-true), which is why the provider re-checks the tenant on
    /// the materialized rows instead of trusting the predicate to have been honoured.
    /// </summary>
    private sealed class PredicateIgnoringStore<T> : AsyncInMemoryStore<T> where T : AbstractModel
    {
        protected override Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
            => base.ReadCoreAsync(null, orderBy, limit, offset, ct);
    }

    private static TenantContext ContextFor(Guid? tenantGuid)
    {
        var context = new TenantContext();
        if (tenantGuid.HasValue)
        {
            context.SetTenant(tenantGuid.Value);
        }
        return context;
    }

    private static TenantSyncOptions DownloadOptions(Guid? tenantGuid = null) => new()
    {
        Scope = "S",
        Direction = SyncDirection.Download,
        TenantGuid = tenantGuid
    };

    // ---- SH-H052: the fetch is scoped, so no foreign row reaches the compare/preview ----

    [Fact]
    public async Task Preview_UnderOneTenant_DoesNotSeeAnotherTenantsRows()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        var theirs = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(theirs);

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var preview = await provider.PreviewAsync(DownloadOptions());

        preview.Items.Select(i => i.Guid).Should().BeEquivalentTo(new[] { mine.Guid!.Value },
            "SH-H052: only the scoped tenant's entities may enter the compare — the other tenant's row was "
                + "previously enumerated, version-hashed and reported as ToCreate");
        preview.ToCreate.Should().Be(1);
    }

    [Fact]
    public async Task Preview_ReportsZeroActions_WhenOnlyForeignRowsExist()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var preview = await provider.PreviewAsync(DownloadOptions());

        preview.Items.Should().BeEmpty();
        preview.ToCreate.Should().Be(0);
        preview.ToUpdate.Should().Be(0);
        preview.ToDelete.Should().Be(0);
    }

    [Fact]
    public async Task Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var result = await provider.SyncAsync(DownloadOptions());

        result.Success.Should().BeTrue();
        var landed = (await local.ReadAsync(CancellationToken.None)).ToList();
        landed.Select(i => i.TenantGuid).Should().AllBeEquivalentTo(TenantA);
        landed.Should().HaveCount(1);
    }

    [Fact]
    public async Task NonNullableTenantGuidEntity_IsAlsoScoped()
    {
        // BuildTenantPredicate emits a different comparison node for Guid than for Guid?; the canonical
        // ITenant shape is the non-nullable one, so a fix that only handled Guid? would leave every real
        // tenant entity unscoped while the other tests stayed green.
        var local = new AsyncInMemoryStore<StrictTenantItem>();
        var remote = new AsyncInMemoryStore<StrictTenantItem>();
        var mine = new StrictTenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(new StrictTenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<StrictTenantItem>, StrictTenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var preview = await provider.PreviewAsync(DownloadOptions());

        preview.Items.Select(i => i.Guid).Should().BeEquivalentTo(new[] { mine.Guid!.Value });
    }

    [Fact]
    public async Task ForeignRowsAreDropped_EvenWhenTheStoreIgnoresTheFetchPredicate()
    {
        var local = new PredicateIgnoringStore<TenantItem>();
        var remote = new PredicateIgnoringStore<TenantItem>();
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<PredicateIgnoringStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var preview = await provider.PreviewAsync(DownloadOptions());

        preview.Items.Select(i => i.Guid).Should().BeEquivalentTo(new[] { mine.Guid!.Value },
            "the tenant guarantee must not depend on the backend honouring the predicate");
    }

    // ---- SH-H051: deletes ----

    [Fact]
    public async Task Delete_NeverRemovesAnotherTenantsItem()
    {
        // Non-initial sync + knowledge saying the row is gone from remote ⇒ the Delete arm fires for it.
        var theirs = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" };
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await local.CreateAsync(theirs);

        var knowledge = new Dictionary<Guid, ISyncKnowledgeItem>
        {
            [theirs.Guid!.Value] = new TenantSyncKnowledgeItem
            {
                EntityGuid = theirs.Guid!.Value,
                Scope = "S",
                TenantGuid = TenantB,
                IsRemoteDeleted = true
            }
        };
        var knowledgeStore = new RecordingKnowledgeStore(DateTime.UtcNow.AddDays(-1), knowledge);

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, knowledgeStore, ContextFor(TenantA));

        var result = await provider.SyncAsync(DownloadOptions());

        result.Deleted.Should().Be(0, "SH-H051: a run scoped to tenant A must not delete tenant B's row");
        (await local.ReadAsync(CancellationToken.None)).Should().HaveCount(1, "the victim row must still be there");
    }

    [Fact]
    public async Task Delete_ConsultsTheSavePredicate_LikeCreateAndUpdateDo()
    {
        // Same-tenant row, so the fetch scoping is not what stops this — the arm itself must consult the
        // predicate. Before the fix it consulted nothing, so a caller's CanSaveToLocal was ignored on the
        // one irreversible path.
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await local.CreateAsync(mine);

        var knowledge = new Dictionary<Guid, ISyncKnowledgeItem>
        {
            [mine.Guid!.Value] = new TenantSyncKnowledgeItem
            {
                EntityGuid = mine.Guid!.Value,
                Scope = "S",
                TenantGuid = TenantA,
                IsRemoteDeleted = true
            }
        };

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(DateTime.UtcNow.AddDays(-1), knowledge), ContextFor(TenantA));

        var result = await provider.SyncAsync(
            DownloadOptions(),
            new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false });

        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(1, "a blocked delete is skipped, exactly as a blocked create is");
        (await local.ReadAsync(CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_StillHappens_ForTheScopedTenant()
    {
        // The guard must not have turned deletes off altogether.
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await local.CreateAsync(mine);

        var knowledge = new Dictionary<Guid, ISyncKnowledgeItem>
        {
            [mine.Guid!.Value] = new TenantSyncKnowledgeItem
            {
                EntityGuid = mine.Guid!.Value,
                Scope = "S",
                TenantGuid = TenantA,
                IsRemoteDeleted = true
            }
        };

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(DateTime.UtcNow.AddDays(-1), knowledge), ContextFor(TenantA));

        var result = await provider.SyncAsync(DownloadOptions());

        result.Deleted.Should().Be(1);
        (await local.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    // ---- SH-H050: one tenant answer for the whole run ----

    [Fact]
    public async Task OptionsTenant_WithNoAmbientTenant_ScopesWritesAndNotOnlyKnowledge()
    {
        // The documented background-job shape, and the worst of the three findings: knowledge was keyed to
        // the requested tenant while NO save predicate existed at all, so every tenant's items were written.
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var knowledgeStore = new RecordingKnowledgeStore();
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, knowledgeStore, ContextFor(null));

        var result = await provider.SyncAsync(DownloadOptions(TenantA));

        result.Success.Should().BeTrue();
        var landed = (await local.ReadAsync(CancellationToken.None)).ToList();
        landed.Should().HaveCount(1, "SH-H050: with no ambient tenant there was no save predicate at all");
        landed[0].TenantGuid.Should().Be(TenantA);
    }

    [Fact]
    public async Task KnowledgeIsKeyedToTheSameTenantTheWritesWereScopedTo()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var mine = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" };
        await remote.CreateAsync(mine);
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var knowledgeStore = new RecordingKnowledgeStore();
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, knowledgeStore, ContextFor(null));

        await provider.SyncAsync(DownloadOptions(TenantA));

        knowledgeStore.ReadTenantIds.Should().AllBeEquivalentTo(TenantA);
        knowledgeStore.WrittenLastSyncTenantIds.Should().AllBeEquivalentTo(TenantA);
        knowledgeStore.Written.Should().HaveCount(1, "only the scoped tenant's entity was processed");
        knowledgeStore.Written.OfType<ITenantSyncKnowledgeItem>().Select(k => k.TenantGuid)
            .Should().AllBeEquivalentTo(TenantA);
        knowledgeStore.Written.Select(k => k.EntityGuid).Should().BeEquivalentTo(new[] { mine.Guid!.Value },
            "a foreign entity's guid must never reach the knowledge store");
    }

    [Fact]
    public async Task AmbientAndOptionsTenantDisagree_IsRefused()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            new AsyncInMemoryStore<TenantItem>(), new AsyncInMemoryStore<TenantItem>(),
            new RecordingKnowledgeStore(), ContextFor(TenantA));

        Func<Task> act = () => provider.SyncAsync(DownloadOptions(TenantB));

        await act.Should().ThrowAsync<TenantMismatchException>(
            "code running in tenant A's scope asking to sync tenant B is a cross-tenant escalation, not a "
                + "precedence question to resolve silently");
    }

    [Fact]
    public async Task AmbientAndOptionsTenantDisagree_IsRefusedOnPreviewToo()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            new AsyncInMemoryStore<TenantItem>(), new AsyncInMemoryStore<TenantItem>(),
            new RecordingKnowledgeStore(), ContextFor(TenantA));

        Func<Task> act = () => provider.PreviewAsync(DownloadOptions(TenantB));

        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    [Fact]
    public async Task AmbientAndOptionsTenantAgree_IsAllowed()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var result = await provider.SyncAsync(DownloadOptions(TenantA));

        result.Success.Should().BeTrue();
        (await local.ReadAsync(CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task NoTenantFromEitherSource_Throws_RatherThanSyncingEveryTenant()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(null));

        Func<Task> act = () => provider.SyncAsync(DownloadOptions());

        await act.Should().ThrowAsync<TenantScopeRequiredException>();
        (await local.ReadAsync(CancellationToken.None)).Should().BeEmpty("nothing may be written by a run that has no tenant");
    }

    [Fact]
    public async Task AllTenantsScope_IsTheSanctionedWayToSyncEveryTenant()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var context = ContextFor(null);
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), context);

        var result = await context.WithAllTenantsAsync(() => provider.SyncAsync(DownloadOptions()));

        result!.Success.Should().BeTrue();
        (await local.ReadAsync(CancellationToken.None)).Should().HaveCount(2, "an explicit all-tenants scope asks for every tenant");
    }

    [Fact]
    public async Task AllTenantsScope_WithAnExplicitTenant_NarrowsToThatTenant()
    {
        // The per-tenant admin loop: WithAllTenants(...) around a per-tenant call must still scope each run.
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var context = ContextFor(null);
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), context);

        await context.WithAllTenantsAsync(() => provider.SyncAsync(DownloadOptions(TenantB)));

        var landed = (await local.ReadAsync(CancellationToken.None)).ToList();
        landed.Should().HaveCount(1);
        landed[0].TenantGuid.Should().Be(TenantB);
    }

    // ---- back-compat: entity types that are not tenant-scoped, and caller-supplied predicates ----

    [Fact]
    public async Task EntityWithoutTenantGuid_SyncsWithNoTenantInScope()
    {
        // There is no tenant to scope a non-tenant entity by, so the missing-tenant refusal must not apply
        // to it — otherwise this provider stops working for every non-tenant type it supports today.
        var local = new AsyncInMemoryStore<PlainItem>();
        var remote = new AsyncInMemoryStore<PlainItem>();
        await remote.CreateAsync(new PlainItem { Guid = Guid.NewGuid(), Name = "x" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<PlainItem>, PlainItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(null));

        var result = await provider.SyncAsync(DownloadOptions());

        result.Success.Should().BeTrue();
        (await local.ReadAsync(CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task CallerFetchPredicateIsHonoured_AlongsideTheTenantTerm()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var wanted = new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "keep" };
        await remote.CreateAsync(wanted);
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "drop" });
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "keep" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var preview = await provider.PreviewAsync(
            DownloadOptions(),
            new SyncFilterOptions<TenantItem> { RemoteFetchPredicate = x => x.Name == "keep" });

        preview.Items.Select(i => i.Guid).Should().BeEquivalentTo(new[] { wanted.Guid!.Value },
            "the tenant term is composed with the caller's predicate, not substituted for it");
    }

    [Fact]
    public async Task OneFilterOptionsInstanceIsReusableAcrossTenants()
    {
        // The per-tenant admin loop reuses one SyncFilterOptions across iterations. If the provider wrote
        // its scoping terms back onto that shared instance, iteration two would carry `t1 && t2` — matching
        // nothing — and the loop would silently sync the first tenant and then nothing at all.
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "b" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(null));

        var shared = new SyncFilterOptions<TenantItem>();

        // Assert AFTER EACH iteration, not only at the end. "Both tenants present" at the end is satisfied
        // just as well by a single unscoped sync that copied everything on iteration one — which is exactly
        // what the pre-fix code did, so an end-state-only assertion passes for the wrong reason and pins
        // nothing.
        await provider.SyncAsync(DownloadOptions(TenantA), shared);
        (await local.ReadAsync(CancellationToken.None)).Select(i => i.TenantGuid)
            .Should().BeEquivalentTo(new Guid?[] { TenantA },
                "iteration one is scoped to A alone — B must not be dragged along");

        await provider.SyncAsync(DownloadOptions(TenantB), shared);
        (await local.ReadAsync(CancellationToken.None)).Select(i => i.TenantGuid)
            .Should().BeEquivalentTo(new Guid?[] { TenantA, TenantB },
                "iteration two must be scoped to B, not to `A && B` — a mutated shared instance matches nothing");

        shared.LocalFetchPredicate.Should().BeNull("the caller's instance must come back unmodified");
        shared.RemoteFetchPredicate.Should().BeNull();
        shared.CanSaveToLocal.Should().BeNull();
        shared.CanSaveToRemote.Should().BeNull();
    }

    [Fact]
    public async Task CallerSavePredicateIsStillHonoured()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = "a" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            local, remote, new RecordingKnowledgeStore(), ContextFor(TenantA));

        var result = await provider.SyncAsync(
            DownloadOptions(),
            new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false });

        result.Created.Should().Be(0);
        (await local.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
