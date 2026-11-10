using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.TimescaleDB.Context;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-894 — the statements a TimescaleDB migration sends on the migration's connection reach the connector's
/// <c>OnExecute</c>. Measured before on TimescaleDB 2.x / PostgreSQL 16: none of the emitters or probes did.
/// </summary>
[Collection(TimescaleDbLiveCollection.Name)]
public sealed class Task894TimescaleMigrationAnnounceTests : IDisposable
{
    private const string Table = "T894Ts";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task894TimescaleMigrationAnnounceTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static TimescaleDBSettings Settings() => new(Host!, Database, User, Password, Port, "ts", "1 day");

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class Probe : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => nameof(Probe);
        public override string Description => "TASK-894 probe";
        public override DateTime CreatedAt => new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        public override void Up(IMigrationContext context) { }
        public override void Down(IMigrationContext context) { }

        public void Run(IMigrationContext c)
        {
            CreateHypertable(c, Table, "Ts", "1 day");
            IsHypertable(c, Table).Should().BeTrue();
            GetChunkInterval(c, Table).Should().NotBeNull();
            AddRetentionPolicy(c, Table, "30 days");
            RemoveRetentionPolicy(c, Table);
        }
    }

    [Fact]
    public void Every_statement_a_TimescaleDB_migration_sends_reaches_OnExecute()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE");
        Exec($"CREATE TABLE \"{Table}\" (Ts timestamptz NOT NULL, Value double precision)");

        var connector = new TimescaleDBConnector(Settings());
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        connector.OnExecute += record;
        using (var connection = new NpgsqlConnection(Settings().GetConnectionString()))
        {
            connection.Open();
            try { new Probe().Run(new TimescaleDBMigrationContext(connection, null, connector)); }
            finally { connector.OnExecute -= record; }
        }

        var expected = new[] { "create_hypertable", "timescaledb_information.hypertables", "timescaledb_information.dimensions", "add_retention_policy", "remove_retention_policy" };
        var missing = expected.Where(e => !received.Any(t => t.Contains(e, StringComparison.OrdinalIgnoreCase))).ToList();
        _output.WriteLine($"MEASURE TimescaleDB | migration emitters and probes | announced {received.Count} | missing {missing.Count}: {string.Join(", ", missing)}");
        foreach (var text in received) _output.WriteLine("  " + text);
        missing.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{Table}\" CASCADE"); } catch { }
    }
}
