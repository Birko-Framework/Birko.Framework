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
using Birko.Data.Sync.Tenant.Models;
using Birko.Data.Sync.Tenant.Providers;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// TASK-630: SH-M342 to SH-M352, the eleven ways the tenant provider's copy of the engine had drifted, asserted on the
/// rebuilt provider (the shared engine run under one tenant). Measured on the stores and the knowledge rows afterwards.
/// </summary>
public class TenantSyncEngineTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime T0 = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    public class TenantItem : AbstractModel
    {
        public Guid? TenantGuid { get; set; }
        public string? Name { get; set; }
        public DateTime UpdatedAt { get; set; } = T0;
    }

    /// <summary>Refuses a single-item create of an item named "bad" (the engine's create path), so one item fails.</summary>
    private sealed class FailingStore : AsyncInMemoryStore<TenantItem>
    {
        protected override Task<Guid> CreateCoreAsync(TenantItem data, StoreDataDelegate<TenantItem>? processDelegate = null, CancellationToken ct = default)
            => data.Name == "bad" ? throw new InvalidOperationException("refused") : base.CreateCoreAsync(data, processDelegate, ct);
    }

    /// <summary>Answers every read with each row twice: a store holding a duplicate Guid.</summary>
    private sealed class DuplicatingStore : AsyncInMemoryStore<TenantItem>
    {
        protected override async Task<IEnumerable<TenantItem>> ReadCoreAsync(Expression<Func<TenantItem, bool>>? filter = null,
            OrderBy<TenantItem>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            var rows = (await base.ReadCoreAsync(filter, orderBy, limit, offset, ct)).ToList();
            return rows.Concat(rows).ToList();
        }
    }

    private static TenantContext ContextFor(Guid tenant)
    {
        var context = new TenantContext();
        context.SetTenant(tenant);
        return context;
    }

    private static TenantSyncProvider<TStore, TenantItem, Knowledge> Provider<TStore>(TStore local, TStore remote, TestKnowledgeStore knowledge)
        where TStore : IAsyncBulkStore<TenantItem>
        => new(local, remote, knowledge, ContextFor(TenantA));

    private static TenantItem Mine(string name = "a") => new() { Guid = Guid.NewGuid(), TenantGuid = TenantA, Name = name };

    [Fact]
    public async Task SH_M342_each_batch_reports_its_own_errors()
    {
        var local = new FailingStore();
        var remote = new FailingStore();
        await remote.CreateAsync(new[] { Mine("bad"), Mine("bad") });
        var perBatch = new List<int>();

        await Provider(local, remote, new TestKnowledgeStore()).SyncAsync(new SyncOptions
        {
            Scope = "S", BatchSize = 1, OnBatchCompleted = b => perBatch.Add(b.Errors.Count)
        });

        perBatch.Should().Equal(1, 1);
    }

    [Fact]
    public async Task SH_M343_a_bidirectional_run_creates_on_the_missing_side()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var up = Mine("up");
        var down = Mine("down");
        await local.CreateAsync(up);
        await remote.CreateAsync(down);

        var result = await Provider(local, remote, new TestKnowledgeStore(T0)).SyncAsync(new SyncOptions { Scope = "S" });

        result.Created.Should().Be(2);
        (await remote.ReadAsync(up.Guid!.Value)).Should().NotBeNull();
        (await local.ReadAsync(down.Guid!.Value)).Should().NotBeNull();
    }

    [Fact]
    public async Task SH_M344_the_preview_honours_the_direction_like_the_sync()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await local.CreateAsync(Mine("local only"));
        var provider = Provider(local, remote, new TestKnowledgeStore(T0));

        var preview = await provider.PreviewAsync(new SyncOptions { Scope = "S", Direction = SyncDirection.Download });

        preview.ToCreate.Should().Be(0, "a download never uploads a local-only item");
        preview.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task SH_M344_the_preview_reports_a_conflict()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var item = Mine();
        await remote.CreateAsync(item);
        var knowledge = new TestKnowledgeStore();
        var provider = Provider(local, remote, knowledge);
        await provider.SyncAsync(new SyncOptions { Scope = "S" });
        var l = (await local.ReadAsync(item.Guid!.Value))!; l.Name = "l"; l.UpdatedAt = T0.AddHours(1); await local.UpdateAsync(l);
        var r = (await remote.ReadAsync(item.Guid!.Value))!; r.Name = "r"; r.UpdatedAt = T0.AddHours(2); await remote.UpdateAsync(r);

        var preview = await provider.PreviewAsync(new SyncOptions { Scope = "S" });

        preview.Conflicts.Should().Be(1);
    }

    [Fact]
    public async Task SH_M345_a_blocked_update_is_counted()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        var item = Mine();
        await remote.CreateAsync(item);
        var provider = Provider(local, remote, new TestKnowledgeStore());
        await provider.SyncAsync(new SyncOptions { Scope = "S" });
        var r = (await remote.ReadAsync(item.Guid!.Value))!; r.Name = "changed"; r.UpdatedAt = T0.AddHours(1); await remote.UpdateAsync(r);

        var result = await provider.SyncAsync(new SyncOptions { Scope = "S" }, new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false });

        result.Skipped.Should().Be(1);
        result.Updated.Should().Be(0);
    }

    [Fact]
    public async Task SH_M346_a_skipped_write_records_no_knowledge()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(Mine());
        await remote.CreateAsync(new TenantItem { Guid = Guid.NewGuid(), TenantGuid = TenantB, Name = "theirs" });
        var knowledge = new TestKnowledgeStore();

        await Provider(local, remote, knowledge).SyncAsync(new SyncOptions { Scope = "S" },
            new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false });

        knowledge.EntityRows().Should().BeEmpty("nothing was reconciled, and the other tenant's row was never fetched");
    }

    [Fact]
    public async Task SH_M347_items_that_failed_are_retried_by_the_next_run()
    {
        var local = new FailingStore();
        var remote = new FailingStore();
        var item = Mine("bad");
        await remote.CreateAsync(new[] { item }); // the bulk path does not refuse; only the engine's per-item create does
        var knowledge = new TestKnowledgeStore();
        var provider = Provider(local, remote, knowledge);

        var first = await provider.SyncAsync(new SyncOptions { Scope = "S" });
        var fixedRow = (await remote.ReadAsync(item.Guid!.Value))!; fixedRow.Name = "good"; await remote.UpdateAsync(fixedRow);
        var second = await provider.SyncAsync(new SyncOptions { Scope = "S" });

        first.Success.Should().BeFalse();
        second.Created.Should().Be(1, "the stamp after a failed run does not settle the item that failed");
        (await local.ReadAsync(item.Guid!.Value)).Should().NotBeNull();
    }

    [Fact]
    public async Task SH_M348_a_ThrowException_save_filter_fails_the_item_and_LogAsError_reaches_OnError()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        await remote.CreateAsync(Mine());
        var logged = new List<SyncError>();

        var thrown = await Provider(local, remote, new TestKnowledgeStore()).SyncAsync(new SyncOptions { Scope = "S" },
            new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false, OnSaveFilterBlock = SaveFilterBlockAction.ThrowException });
        await Provider(local, remote, new TestKnowledgeStore()).SyncAsync(new SyncOptions { Scope = "S", OnError = logged.Add },
            new SyncFilterOptions<TenantItem> { CanSaveToLocal = _ => false, OnSaveFilterBlock = SaveFilterBlockAction.LogAsError });

        thrown.Success.Should().BeFalse();
        logged.Should().ContainSingle();
    }

    [Fact]
    public async Task SH_M349_MaxItems_and_OnBatchStarting_are_honoured()
    {
        var local = new AsyncInMemoryStore<TenantItem>();
        var remote = new AsyncInMemoryStore<TenantItem>();
        for (var i = 0; i < 5; i++) await remote.CreateAsync(Mine("r" + i));
        var batches = new List<int>();

        var result = await Provider(local, remote, new TestKnowledgeStore()).SyncAsync(new SyncOptions
        {
            Scope = "S", MaxItems = 2, BatchSize = 1, OnBatchStarting = batches.Add
        });

        result.Created.Should().Be(2);
        batches.Should().Equal(1, 2);
    }

    [Fact]
    public async Task SH_M350_two_tenants_sync_concurrently_and_one_tenant_serially()
    {
        var queue = new TenantSyncQueue(maxConcurrentSyncs: 2);
        var running = new Dictionary<Guid, int>();
        var peakAll = 0;
        var peakPerTenant = 0;
        var gate = new object();
        Func<Task<int>> Op(Guid tenant) => async () =>
        {
            lock (gate)
            {
                running[tenant] = running.GetValueOrDefault(tenant) + 1;
                peakAll = Math.Max(peakAll, running.Values.Sum());
                peakPerTenant = Math.Max(peakPerTenant, running[tenant]);
            }
            await Task.Delay(60);
            lock (gate) running[tenant]--;
            return 0;
        };

        await Task.WhenAll(
            queue.EnqueueAsync("S", TenantA, Op(TenantA)),
            queue.EnqueueAsync("S", TenantA, Op(TenantA)),
            queue.EnqueueAsync("S", TenantB, Op(TenantB)));

        peakAll.Should().Be(2, "tenant B runs beside tenant A");
        peakPerTenant.Should().Be(1, "one tenant's syncs of a scope never overlap");
    }

    [Fact]
    public async Task SH_M351_a_duplicate_guid_fails_the_run_naming_the_guid()
    {
        var local = new DuplicatingStore();
        var remote = new DuplicatingStore();
        var item = Mine();
        await remote.CreateAsync(item);

        var result = await Provider(local, remote, new TestKnowledgeStore()).SyncAsync(new SyncOptions { Scope = "S" });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Details.Should().Contain(item.Guid!.Value.ToString());
    }

    [Fact]
    public async Task SH_M352_a_filter_of_another_type_is_refused()
    {
        ISyncProvider provider = Provider(new AsyncInMemoryStore<TenantItem>(), new AsyncInMemoryStore<TenantItem>(), new TestKnowledgeStore());

        var act = () => provider.SyncAsync(new SyncOptions { Scope = "S" }, new SyncFilterOptions<Knowledge>());

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
