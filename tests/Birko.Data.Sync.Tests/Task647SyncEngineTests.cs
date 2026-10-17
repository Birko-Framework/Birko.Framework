using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// TASK-647: (1) the Custom policy is asked about an entity both sides hold with no recorded pair; (2) a store that does
/// not keep UpdatedAt exactly (measured: SQLite, PostgreSQL, MySQL, SQL Server and MongoDB) does not read as changed on
/// every run; (3) a failure is reported once, on both channels, whatever failed.
/// </summary>
public class Task647SyncEngineTests
{
    private const string Scope = "Default";

    /// <summary>Keeps a copy whose UpdatedAt lost what MongoDB loses (sub-millisecond ticks) and what the SQL providers lose (Kind).</summary>
    private sealed class LossyStore : TestAsyncBulkStore
    {
        private static TestSyncModel Stored(TestSyncModel data) => new()
        {
            Guid = data.Guid,
            Name = data.Name,
            UpdatedAt = DateTime.SpecifyKind(new DateTime(data.UpdatedAt.Ticks - data.UpdatedAt.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified),
        };

        protected override Task<Guid> CreateCoreAsync(TestSyncModel data, StoreDataDelegate<TestSyncModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _items[data.Guid.Value] = Stored(data);
            return Task.FromResult(data.Guid.Value);
        }

        protected override Task UpdateCoreAsync(TestSyncModel data, StoreDataDelegate<TestSyncModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data?.Guid != null && _items.ContainsKey(data.Guid.Value))
                _items[data.Guid.Value] = Stored(data);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCreateStore : TestAsyncBulkStore
    {
        protected override Task<Guid> CreateCoreAsync(TestSyncModel data, StoreDataDelegate<TestSyncModel>? processDelegate = null, CancellationToken ct = default)
            => throw new InvalidOperationException("create refused");
    }

    private static AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge> Provider(
        TestAsyncBulkStore local, TestAsyncBulkStore remote, TestAsyncSyncKnowledgeItemStore knowledge) => new(local, remote, knowledge);

    private static async Task<TestAsyncSyncKnowledgeItemStore> NotInitial()
    {
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);
        return knowledge;
    }

    private static DateTime Precise(int second) => new DateTime(2026, 10, 17, 12, 0, second, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public async Task An_entity_written_to_a_lossy_store_is_unchanged_on_the_next_runs()
    {
        var local = new TestAsyncBulkStore();
        var remote = new LossyStore();
        var knowledge = await NotInitial();
        await local.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = Precise(1) });
        var provider = Provider(local, remote, knowledge);
        var options = new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope };

        (await provider.SyncAsync(options)).Created.Should().Be(1);
        var second = await provider.SyncAsync(options);
        var third = await provider.SyncAsync(options);

        second.Updated.Should().Be(0, "nothing changed since the first run");
        second.Conflicts.Should().Be(0);
        third.Updated.Should().Be(0);
        local.Items[FirstKey(local)].UpdatedAt.Should().Be(Precise(1), "the source keeps its precise timestamp");
    }

    private static Guid FirstKey(TestAsyncBulkStore store) { foreach (var k in store.Items.Keys) return k; return Guid.Empty; }

    [Fact]
    public async Task Custom_is_asked_about_an_entity_both_sides_hold_with_no_recorded_pair()
    {
        var guid = Guid.NewGuid();
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "local", UpdatedAt = Precise(2) });
        await remote.CreateAsync(new TestSyncModel { Guid = guid, Name = "remote", UpdatedAt = Precise(1) });
        var asked = new List<ConflictInfo>();

        var result = await Provider(local, remote, await NotInitial()).SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.Custom,
            CustomConflictResolver = c => { asked.Add(c); return ConflictResolution.UseRemote; },
        });

        asked.Should().ContainSingle(c => c.Guid == guid);
        result.Conflicts.Should().Be(1);
        local.Items[guid].Name.Should().Be("remote", "the resolver chose the remote copy");
    }

    [Fact]
    public async Task Custom_is_not_asked_about_an_identical_entity_with_no_recorded_pair()
    {
        var guid = Guid.NewGuid();
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "same", UpdatedAt = Precise(1) });
        await remote.CreateAsync(new TestSyncModel { Guid = guid, Name = "same", UpdatedAt = Precise(1) });
        var asked = 0;

        var result = await Provider(local, remote, await NotInitial()).SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.Custom,
            CustomConflictResolver = _ => { asked++; return ConflictResolution.UseLocal; },
        });

        asked.Should().Be(0);
        result.Conflicts.Should().Be(0);
    }

    [Fact]
    public async Task A_failed_conflict_resolution_is_reported_once_on_each_channel()
    {
        var guid = Guid.NewGuid();
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "local", UpdatedAt = Precise(2) });
        await remote.CreateAsync(new TestSyncModel { Guid = guid, Name = "remote", UpdatedAt = Precise(1) });
        var reported = new List<SyncError>();

        var result = await Provider(local, remote, await NotInitial()).SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.Custom,
            CustomConflictResolver = _ => ConflictResolution.Merge,
            OnError = reported.Add,
        });

        result.Errors.Should().ContainSingle(e => e.ItemGuid == guid);
        reported.Should().ContainSingle(e => e.ItemGuid == guid);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Any_failed_item_reaches_OnError_too()
    {
        var local = new TestAsyncBulkStore();
        var remote = new FailingCreateStore();
        await local.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = Precise(1) });
        var reported = new List<SyncError>();

        var result = await Provider(local, remote, await NotInitial()).SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            OnError = reported.Add,
        });

        result.Errors.Should().ContainSingle();
        reported.Should().ContainSingle().Which.Details.Should().Contain("create refused");
    }

    [Fact]
    public async Task A_throwing_OnError_handler_does_not_change_the_result()
    {
        var local = new TestAsyncBulkStore();
        var remote = new FailingCreateStore();
        await local.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = Precise(1) });

        var result = await Provider(local, remote, await NotInitial()).SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            OnError = _ => throw new InvalidOperationException("handler bug"),
        });

        result.Errors.Should().ContainSingle().Which.Details.Should().Contain("create refused", "the item's own error, not the handler's (rule 25)");
    }

    /// <summary>The sync provider's twin of the lossy store: stores a millisecond, Kind-less copy.</summary>
    private sealed class SyncLossyStore : TestBulkStore
    {
        private static TestSyncModel Stored(TestSyncModel data) => new()
        {
            Guid = data.Guid,
            Name = data.Name,
            UpdatedAt = DateTime.SpecifyKind(new DateTime(data.UpdatedAt.Ticks - data.UpdatedAt.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified),
        };

        protected override Guid CreateCore(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            return base.CreateCore(Stored(data), storeDelegate);
        }

        protected override void UpdateCore(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
            => base.UpdateCore(Stored(data), storeDelegate);
    }

    private sealed class SyncFailingCreateStore : TestBulkStore
    {
        protected override Guid CreateCore(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
            => throw new InvalidOperationException("create refused");
    }

    [Fact]
    public void Sync_provider_an_entity_written_to_a_lossy_store_is_unchanged_on_the_next_run()
    {
        var local = new TestBulkStore();
        var remote = new SyncLossyStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));
        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = Precise(1) });
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var options = new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope };

        provider.Sync(options).Created.Should().Be(1);
        var second = provider.Sync(options);

        second.Updated.Should().Be(0);
        second.Conflicts.Should().Be(0);
    }

    [Fact]
    public void Sync_provider_a_failed_item_is_reported_once_on_each_channel()
    {
        var local = new TestBulkStore();
        var remote = new SyncFailingCreateStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));
        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a", UpdatedAt = Precise(1) });
        var reported = new List<SyncError>();

        var result = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope, OnError = reported.Add });

        result.Errors.Should().ContainSingle();
        reported.Should().ContainSingle();
    }
}
