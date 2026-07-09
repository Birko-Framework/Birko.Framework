using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.Tests;

/// <summary>
/// CR-M102: the provider-agnostic runner rules had no direct coverage. These pin RegisterMigrations
/// dedup/sort, the Migrate/Rollback target guards, GetPending/AppliedMigrations partitioning,
/// EnsureInitialized, and the GetMigrationsToExecute version-range selection (incl. the Down boundary
/// m.Version <= from && m.Version > to, which is easy to get off-by-one).
/// </summary>
public class MigrationRunnerLogicTests
{
    private sealed class FakeStore : IMigrationStore
    {
        public readonly HashSet<long> Applied = new();
        public long Current;
        public void Initialize() { }
        public Task InitializeAsync() => Task.CompletedTask;
        public ISet<long> GetAppliedVersions() => Applied;
        public Task<ISet<long>> GetAppliedVersionsAsync() => Task.FromResult<ISet<long>>(Applied);
        public void RecordMigration(IMigration migration) { }
        public Task RecordMigrationAsync(IMigration migration) => Task.CompletedTask;
        public void RemoveMigration(IMigration migration) { }
        public Task RemoveMigrationAsync(IMigration migration) => Task.CompletedTask;
        public long GetCurrentVersion() => Current;
        public Task<long> GetCurrentVersionAsync() => Task.FromResult(Current);
    }

    private sealed class Mig : IMigration
    {
        public long Version { get; init; }
        public string Name => $"M{Version}";
        public string Description => Name;
        public DateTime CreatedAt => new(2026, 1, 1);
        public void Up(IMigrationContext context) { }
        public void Down(IMigrationContext context) { }
    }

    private sealed class Runner : AbstractMigrationRunner
    {
        public (long from, long to, MigrationDirection dir)? LastExecute;
        public Runner(IMigrationStore store) : base(store) { }
        protected override MigrationResult ExecuteMigrations(long from, long to, MigrationDirection direction)
        {
            LastExecute = (from, to, direction);
            return MigrationResult.Successful(from, to, direction, Array.Empty<ExecutedMigration>());
        }
        public IReadOnlyList<IMigration> Selection(long from, long to, MigrationDirection dir)
            => GetMigrationsToExecute(from, to, dir);
    }

    private static Runner NewRunner(FakeStore store, params long[] versions)
    {
        var runner = new Runner(store);
        runner.RegisterMigrations(versions.Select(v => (IMigration)new Mig { Version = v }).ToArray());
        runner.Initialize();
        return runner;
    }

    [Fact]
    public void RegisterMigrations_rejects_a_duplicate_version()
    {
        var runner = new Runner(new FakeStore());
        runner.RegisterMigrations(new Mig { Version = 1 });

        Action act = () => runner.RegisterMigrations(new Mig { Version = 1 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*version 1*already registered*");
    }

    [Fact]
    public void RegisterMigrations_sorts_by_version()
    {
        var runner = new Runner(new FakeStore());
        runner.RegisterMigrations(new Mig { Version = 3 }, new Mig { Version = 1 }, new Mig { Version = 2 });

        runner.Migrations.Select(m => m.Version).Should().ContainInOrder(1L, 2L, 3L);
        runner.LatestVersion.Should().Be(3);
    }

    [Fact]
    public void Migrate_to_current_is_a_successful_noop()
    {
        var runner = NewRunner(new FakeStore { Current = 0 }, 1, 2);

        var result = runner.Migrate(0);

        result.Success.Should().BeTrue();
        runner.LastExecute.Should().BeNull("no migrations run when already at target");
    }

    [Fact]
    public void Migrate_below_current_fails_and_does_not_execute()
    {
        var runner = NewRunner(new FakeStore { Current = 5 }, 1, 2, 3);

        var result = runner.Migrate(2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("less than");
        runner.LastExecute.Should().BeNull();
    }

    [Fact]
    public void Migrate_up_executes_from_current_to_target()
    {
        var runner = NewRunner(new FakeStore { Current = 1 }, 1, 2, 3);

        runner.Migrate(3);

        runner.LastExecute.Should().Be((1L, 3L, MigrationDirection.Up));
    }

    [Fact]
    public void Rollback_above_current_fails()
    {
        var runner = NewRunner(new FakeStore { Current = 2 }, 1, 2, 3);

        var result = runner.Rollback(5);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("greater than");
        runner.LastExecute.Should().BeNull();
    }

    [Fact]
    public void Rollback_down_executes_from_current_to_target()
    {
        var runner = NewRunner(new FakeStore { Current = 3 }, 1, 2, 3);

        runner.Rollback(1);

        runner.LastExecute.Should().Be((3L, 1L, MigrationDirection.Down));
    }

    [Fact]
    public void Pending_and_applied_partition_by_the_store_applied_set()
    {
        var store = new FakeStore();
        store.Applied.Add(1);
        var runner = NewRunner(store, 1, 2, 3);

        runner.GetAppliedMigrations().Select(m => m.Version).Should().BeEquivalentTo(new[] { 1L });
        runner.GetPendingMigrations().Select(m => m.Version).Should().BeEquivalentTo(new[] { 2L, 3L });
    }

    [Fact]
    public void Migrate_before_Initialize_throws()
    {
        var runner = new Runner(new FakeStore());
        runner.RegisterMigrations(new Mig { Version = 1 });

        Action act = () => runner.Migrate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetMigrationsToExecute_up_selects_open_lower_closed_upper_ascending()
    {
        var runner = NewRunner(new FakeStore(), 1, 2, 3, 4);

        // Up: version > from && version <= to
        runner.Selection(1, 3, MigrationDirection.Up).Select(m => m.Version)
            .Should().ContainInOrder(2L, 3L).And.HaveCount(2);
    }

    [Fact]
    public void GetMigrationsToExecute_down_selects_closed_upper_open_lower_descending()
    {
        var runner = NewRunner(new FakeStore(), 1, 2, 3, 4);

        // Down: version <= from && version > to (from=3,to=1 → 2,3 → reversed 3,2)
        runner.Selection(3, 1, MigrationDirection.Down).Select(m => m.Version)
            .Should().ContainInOrder(3L, 2L).And.HaveCount(2);
    }
}
