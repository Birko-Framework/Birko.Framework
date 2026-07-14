using System;
using System.IO;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// Covers the reintroduced raw-SQL migration base <see cref="SqlScriptMigration"/>: an Up script runs
/// end-to-end through <see cref="SqlMigrationRunner"/> against a real file-based SQLite database, a
/// DownSql script reverts, a null DownSql falls back to the base NotImplementedException, and the
/// context/argument guards fire.
/// </summary>
public class SqlScriptMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public SqlScriptMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"birko-script-mig-{Guid.NewGuid():N}.db");
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
            // Best-effort temp cleanup.
        }
    }

    private sealed class CreateGadgetsScript : SqlScriptMigration
    {
        public override long Version => 1;
        public override string Name => "CreateGadgets";
        protected override string UpSql => "CREATE TABLE Gadgets (Id TEXT PRIMARY KEY, Name TEXT);";
        protected override string? DownSql => "DROP TABLE Gadgets;";
    }

    private sealed class NoDownScript : SqlScriptMigration
    {
        public override long Version => 1;
        public override string Name => "NoDown";
        protected override string UpSql => "CREATE TABLE Things (Id TEXT PRIMARY KEY);";
    }

    private sealed class EmptyUpScript : SqlScriptMigration
    {
        public override long Version => 1;
        public override string Name => "EmptyUp";
        protected override string UpSql => "   ";
    }

    private sealed class FakeContext : IMigrationContext
    {
        public ISchemaBuilder Schema => throw new NotSupportedException();
        public IDataMigrator Data => throw new NotSupportedException();
        public string ProviderName => "Fake";
        public void Raw(Action<object> providerAction) => throw new NotSupportedException();
    }

    private AbstractConnector Connector()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        return DataBase.GetConnector<SqLiteConnector>(settings);
    }

    private long TableCount(AbstractConnector connector, string table)
    {
        using var connection = connector.CreateConnection(connector.Settings);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void UpScript_RunsThroughRunner_CreatesTable()
    {
        var connector = Connector();
        var runner = new SqlMigrationRunner(connector);
        runner.RegisterMigrations(new CreateGadgetsScript());
        runner.Initialize();

        var result = runner.Migrate();

        result.Success.Should().BeTrue();
        runner.CurrentVersion.Should().Be(1);
        TableCount(connector, "Gadgets").Should().Be(1);
    }

    [Fact]
    public void DownScript_RunsThroughRunner_DropsTable()
    {
        var connector = Connector();
        var runner = new SqlMigrationRunner(connector);
        runner.RegisterMigrations(new CreateGadgetsScript());
        runner.Initialize();
        runner.Migrate();
        TableCount(connector, "Gadgets").Should().Be(1);

        var result = runner.Rollback(0);

        result.Success.Should().BeTrue();
        TableCount(connector, "Gadgets").Should().Be(0);
    }

    [Fact]
    public void Down_WithNullDownSql_ThrowsNotImplemented()
    {
        var migration = new NoDownScript();

        Action act = () => migration.Down(new FakeContext());

        act.Should().Throw<NotImplementedException>();
    }

    [Fact]
    public void Up_WithNonSqlContext_ThrowsInvalidOperation()
    {
        var migration = new CreateGadgetsScript();

        Action act = () => migration.Up(new FakeContext());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Up_WithEmptySql_ThrowsArgument()
    {
        var migration = new EmptyUpScript();

        Action act = () => migration.Up(new FakeContext());

        act.Should().Throw<ArgumentException>();
    }
}
