using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// End-to-end regression for the single-writer SQLite migration deadlock (TASK-034). The runner used
/// to record the applied version on a SECOND connection opened by the store, while the outer
/// migration transaction (default <c>UseTransaction = true</c>) still held SQLite's writer lock from
/// the DDL — so the version INSERT blocked and failed with SQLITE_BUSY. Consumers worked around it by
/// setting <c>UseTransaction = false</c>. The runner now records on its own connection/transaction, so
/// the default settings apply cleanly. Uses a real <b>file</b>-based SQLite DB (not <c>:memory:</c>,
/// where each connection is a distinct database) so the two-connection contention is reproducible.
/// </summary>
public class SqliteMigrationRunnerTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteMigrationRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"birko-mig-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best-effort temp cleanup; a leaked temp file must not fail the test run.
        }
    }

    private sealed class CreateWidgetsMigration : AbstractMigration
    {
        public override long Version => 1;
        public override string Name => "CreateWidgets";

        public override void Up(IMigrationContext context)
        {
            context.Schema.CreateCollection("Widgets")
                .WithField("Id", FieldType.Guid, isPrimary: true)
                .WithField("Name", FieldType.String, maxLength: 100)
                .Build();
        }
    }

    [Fact]
    public void Migrate_with_default_transaction_settings_succeeds_on_file_sqlite()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);

        // No SqlMigrationSettings passed => defaults, i.e. UseTransaction = true (the case that used to deadlock).
        var runner = new SqlMigrationRunner(connector);
        runner.RegisterMigrations(new CreateWidgetsMigration());
        runner.Initialize();

        var result = runner.Migrate();

        result.Success.Should().BeTrue();
        result.ExecutedMigrations.Should().HaveCount(1);
        runner.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public void Migrate_is_idempotent_across_reruns()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);

        var first = new SqlMigrationRunner(connector);
        first.RegisterMigrations(new CreateWidgetsMigration());
        first.Initialize();
        first.Migrate();

        // A fresh runner over the same DB should see the version already applied and do nothing.
        var second = new SqlMigrationRunner(connector);
        second.RegisterMigrations(new CreateWidgetsMigration());
        second.Initialize();
        var result = second.Migrate();

        result.ExecutedMigrations.Should().BeEmpty();
        second.CurrentVersion.Should().Be(1);
    }

    /// <summary>
    /// CR-M101: the CancellationToken now threads from the runner through SqlMigrationStore into the
    /// real ADO.NET async calls (connection.OpenAsync(ct)), so a pre-cancelled token aborts the
    /// operation instead of being ignored.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_CancelledToken_Throws_OnRealSqlitePath()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);
        var runner = new SqlMigrationRunner(connector);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => runner.InitializeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MigrateAsync_CancelledToken_Throws_OnRealSqlitePath()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var connector = DataBase.GetConnector<SqLiteConnector>(settings);
        var runner = new SqlMigrationRunner(connector);
        runner.RegisterMigrations(new CreateWidgetsMigration());
        runner.Initialize();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // MigrateAsync threads the token into Store.GetCurrentVersionAsync(ct) → connection.OpenAsync(ct).
        Func<Task> act = () => runner.MigrateAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
