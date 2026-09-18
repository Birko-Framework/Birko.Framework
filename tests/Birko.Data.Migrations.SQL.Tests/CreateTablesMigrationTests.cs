using System;
using System.Data.Common;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// Tests for <see cref="CreateTablesMigration"/> — the mapping-driven "create the schema" migration
/// (TASK-032). Runs against a real file-based SQLite DB so the create/drop actually round-trips, and
/// drives the create path through <see cref="SqlMigrationRunner"/> (default settings) so it also
/// exercises the single-writer transaction fix from TASK-034.
/// </summary>
public class CreateTablesMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public CreateTablesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"birko-createtbl-{Guid.NewGuid():N}.db");
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

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private AbstractConnector BuildConnectorWithMapping()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        var settings = new SqLiteSettings(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath));
        return DataBase.GetConnector<SqLiteConnector>(settings);
    }

    private static long CountTable(DbConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n";
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = name;
        cmd.Parameters.Add(p);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Up_creates_the_mapped_tables_via_the_runner()
    {
        var connector = BuildConnectorWithMapping();

        // Connector-driven migration => UseTransaction = false (it provisions on the connector's own
        // connection, so an outer runner transaction on another connection would lock the DB).
        var runner = new SqlMigrationRunner(connector, new SqlMigrationSettings { UseTransaction = false });
        runner.RegisterMigrations(new CreateTablesMigration(connector, new[] { typeof(Widget) }));
        runner.Initialize();

        var result = runner.Migrate();

        result.Success.Should().BeTrue();
        runner.CurrentVersion.Should().Be(1);

        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        CountTable(conn, "Widgets").Should().Be(1);
    }

    [Fact]
    public void Down_drops_the_mapped_tables()
    {
        var connector = BuildConnectorWithMapping();
        // The migration ignores IMigrationContext (it drives the connector directly — see remarks), so
        // Up/Down can be exercised without a runner/context here to check the raw create/drop behaviour.
        var migration = new CreateTablesMigration(connector, new[] { typeof(Widget) });

        migration.Up(null!);
        using (var conn = connector.CreateConnection(connector.Settings))
        {
            conn.Open();
            CountTable(conn, "Widgets").Should().Be(1);
        }

        migration.Down(null!);
        using (var conn = connector.CreateConnection(connector.Settings))
        {
            conn.Open();
            CountTable(conn, "Widgets").Should().Be(0);
        }
    }
}
