using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.Tests;

/// <summary>
/// Regression for CR-H054: the async runner methods were sync-over-async wrappers that called the
/// blocking store members. InitializeAsync/MigrateAsync/RollbackAsync now await the store's *Async
/// members and an overridable async execution hook. A tracking store proves the async path is used.
/// </summary>
public class AsyncMigrationRunnerTests
{
    private sealed class TrackingStore : IMigrationStore
    {
        public bool InitializeCalled, InitializeAsyncCalled;
        public bool GetCurrentVersionCalled, GetCurrentVersionAsyncCalled;
        public long Version;

        public void Initialize() => InitializeCalled = true;
        public Task InitializeAsync() { InitializeAsyncCalled = true; return Task.CompletedTask; }
        public ISet<long> GetAppliedVersions() => new HashSet<long>();
        public Task<ISet<long>> GetAppliedVersionsAsync() => Task.FromResult<ISet<long>>(new HashSet<long>());
        public void RecordMigration(IMigration migration) { }
        public Task RecordMigrationAsync(IMigration migration) => Task.CompletedTask;
        public void RemoveMigration(IMigration migration) { }
        public Task RemoveMigrationAsync(IMigration migration) => Task.CompletedTask;
        public long GetCurrentVersion() { GetCurrentVersionCalled = true; return Version; }
        public Task<long> GetCurrentVersionAsync() { GetCurrentVersionAsyncCalled = true; return Task.FromResult(Version); }
    }

    private sealed class TestMigration : IMigration
    {
        public long Version { get; init; }
        public string Name => $"M{Version}";
        public string Description => Name;
        public DateTime CreatedAt => new(2026, 1, 1);
        public void Up(IMigrationContext context) { }
        public void Down(IMigrationContext context) { }
    }

    private sealed class TrackingRunner : AbstractMigrationRunner
    {
        public int ExecuteCount;
        public TrackingRunner(IMigrationStore store) : base(store) { }
        protected override MigrationResult ExecuteMigrations(long from, long to, MigrationDirection direction)
        {
            ExecuteCount++;
            return MigrationResult.Successful(from, to, direction, Array.Empty<ExecutedMigration>());
        }
    }

    [Fact]
    public async Task InitializeAsync_UsesStoreInitializeAsync_NotSync()
    {
        var store = new TrackingStore();
        var runner = new TrackingRunner(store);

        await runner.InitializeAsync();

        store.InitializeAsyncCalled.Should().BeTrue("CR-H054: the async runner must await the store's async init");
        store.InitializeCalled.Should().BeFalse("it must not call the blocking Initialize()");
    }

    [Fact]
    public async Task MigrateAsync_UsesGetCurrentVersionAsync_AndAsyncExecution()
    {
        var store = new TrackingStore { Version = 0 };
        var runner = new TrackingRunner(store);
        runner.RegisterMigrations(new TestMigration { Version = 1 });
        await runner.InitializeAsync();

        var result = await runner.MigrateAsync();

        store.GetCurrentVersionAsyncCalled.Should().BeTrue("CR-H054: MigrateAsync must query the version asynchronously");
        store.GetCurrentVersionCalled.Should().BeFalse("it must not call the blocking GetCurrentVersion()");
        runner.ExecuteCount.Should().Be(1, "the async execution hook ran the migration");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RollbackAsync_UsesGetCurrentVersionAsync()
    {
        var store = new TrackingStore { Version = 5 };
        var runner = new TrackingRunner(store);
        await runner.InitializeAsync();

        await runner.RollbackAsync(2);

        store.GetCurrentVersionAsyncCalled.Should().BeTrue();
        store.GetCurrentVersionCalled.Should().BeFalse();
    }
}
