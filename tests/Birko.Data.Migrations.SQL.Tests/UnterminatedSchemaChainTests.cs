using System;
using System.IO;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.Exceptions;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-627 (SH-M245): a migration whose <c>CreateIndex</c> / <c>CreateCollection</c> chain never reached
/// <c>Build()</c> emitted no DDL and was recorded as applied. It now fails, naming the chain, and is not recorded.
/// On a real file SQLite database, through <see cref="SqlMigrationRunner"/>.
/// </summary>
public class UnterminatedSchemaChainTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"birko-task627-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private sealed class Migration : AbstractMigration
    {
        private readonly Action<IMigrationContext> _up;
        private readonly Action<IMigrationContext>? _down;

        public Migration(long version, Action<IMigrationContext> up, Action<IMigrationContext>? down = null)
        {
            Version = version;
            _up = up;
            _down = down;
        }

        public override long Version { get; }
        public override string Name => "M" + Version;
        public override void Up(IMigrationContext context) => _up(context);
        public override void Down(IMigrationContext context) => _down?.Invoke(context);
    }

    private static readonly Migration CreateWidgets = new(1, c => c.Schema.CreateCollection("Widgets")
        .WithField("Id", FieldType.Guid, isPrimary: true)
        .WithField("Name", FieldType.String, maxLength: 100)
        .Build());

    private SqlMigrationRunner Runner(params IMigration[] migrations)
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        var runner = new SqlMigrationRunner(DataBase.GetConnector<SqLiteConnector>(settings));
        runner.RegisterMigrations(migrations);
        runner.Initialize();
        return runner;
    }

    private long Count(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private long IndexCount(string name) => Count($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{name}'");
    private long TableCount(string name) => Count($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{name}'");

    [Fact]
    public void An_index_chain_without_Build_fails_the_migration_and_is_not_recorded()
    {
        var runner = Runner(CreateWidgets, new Migration(2, c => c.Schema.CreateIndex("Widgets", "IX_Widgets_Name").WithField("Name")));
        runner.Migrate(1); // its own run: a run is one transaction, so a failure rolls back the whole run

        var act = () => runner.Migrate();

        act.Should().Throw<MigrationException>()
            .Which.ToString().Should().Contain("CreateIndex(\"Widgets\", \"IX_Widgets_Name\")").And.Contain("Build()");
        runner.CurrentVersion.Should().Be(1, "migration 1 completed; migration 2 is not recorded");
        IndexCount("IX_Widgets_Name").Should().Be(0);
    }

    [Fact]
    public void A_collection_chain_without_Build_fails_the_migration_and_is_not_recorded()
    {
        var runner = Runner(new Migration(1, c => c.Schema.CreateCollection("Ghosts").WithField("Id", FieldType.Guid, isPrimary: true)));

        var act = () => runner.Migrate();

        act.Should().Throw<MigrationException>().Which.ToString().Should().Contain("CreateCollection(\"Ghosts\")");
        runner.CurrentVersion.Should().Be(0);
        TableCount("Ghosts").Should().Be(0);
    }

    [Fact]
    public void Every_unfinished_chain_is_named()
    {
        var runner = Runner(CreateWidgets, new Migration(2, c =>
        {
            c.Schema.CreateIndex("Widgets", "IX_A").WithField("Name");
            c.Schema.CreateIndex("Widgets", "IX_B").WithField("Name").Build();
            c.Schema.CreateIndex("Widgets", "IX_C").WithField("Name").Unique();
        }));

        var message = runner.Invoking(r => r.Migrate()).Should().Throw<MigrationException>().Which.ToString();

        message.Should().Contain("\"IX_A\"").And.Contain("\"IX_C\"").And.NotContain("\"IX_B\"");
    }

    [Fact]
    public void A_terminated_chain_is_applied_and_recorded_as_before()
    {
        var runner = Runner(CreateWidgets, new Migration(2, c => c.Schema.CreateIndex("Widgets", "IX_Widgets_Name").WithField("Name").Build()));

        runner.Migrate().Success.Should().BeTrue();

        runner.CurrentVersion.Should().Be(2);
        IndexCount("IX_Widgets_Name").Should().Be(1);
    }

    [Fact]
    public void A_down_with_an_unfinished_chain_fails_and_keeps_the_version()
    {
        var runner = Runner(CreateWidgets, new Migration(2,
            c => c.Schema.CreateIndex("Widgets", "IX_Up").WithField("Name").Build(),
            c => c.Schema.CreateIndex("Widgets", "IX_Restored").WithField("Name")));
        runner.Migrate();

        runner.Invoking(r => r.Rollback(1)).Should().Throw<MigrationException>().Which.ToString().Should().Contain("IX_Restored");
        runner.CurrentVersion.Should().Be(2);
    }
}
