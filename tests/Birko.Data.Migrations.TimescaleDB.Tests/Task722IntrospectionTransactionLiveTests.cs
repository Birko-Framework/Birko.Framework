using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.TimescaleDB;
using Birko.Data.Migrations.TimescaleDB.Context;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-722, live: the claim that <c>IsHypertable</c> / <c>GetChunkInterval</c>, whose commands never set
/// <c>command.Transaction</c>, fail or miss the migration's own uncommitted work inside its transaction. Npgsql runs every
/// command on a connection in that connection's open transaction whatever <c>Transaction</c> says, so the lookup sees a
/// hypertable created earlier in the same transaction — this test pins that, and it passed on the old code.
/// </summary>
[Collection(TimescaleDbLiveCollection.Name)]
public sealed class Task722IntrospectionTransactionLiveTests : IDisposable
{
    private const string Table = "T722Metrics";
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private readonly ITestOutputHelper _output;

    public Task722IntrospectionTransactionLiveTests(ITestOutputHelper output) => _output = output;

    private static TimescaleDBSettings Settings()
        => new(Host!, LiveDatabase.Name("TS"),
            Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres",
            Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres", Port, "ts", "1 day");

    private sealed class Probe : TimescaleDBMigration
    {
        public override long Version => 1;
        public override string Name => nameof(Probe);
        public override string Description => "TASK-722 live probe";
        public override DateTime CreatedAt => new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
        public override void Up(IMigrationContext context) { }
        public override void Down(IMigrationContext context) { }
        public bool Hyper(IMigrationContext c, string t) => IsHypertable(c, t);
        public string? Chunk(IMigrationContext c, string t) => GetChunkInterval(c, t);
    }

    [Fact]
    public void Introspection_inside_the_migration_s_transaction_sees_its_own_hypertable()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            _output.WriteLine("SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException("BIRKO_REQUIRE_LIVE");
            return;
        }
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = $"CREATE TABLE \"{Table}\" (Ts timestamptz NOT NULL, Value double precision); "
                               + $"SELECT create_hypertable('\"{Table}\"', 'ts', chunk_time_interval => INTERVAL '2 days');";
            create.ExecuteNonQuery();
        }
        var context = new TimescaleDBMigrationContext(connection, transaction, new TimescaleDBConnector(Settings()));

        new Probe().Hyper(context, Table).Should().BeTrue();
        new Probe().Chunk(context, Table).Should().Contain("2 days");
        transaction.Rollback();
    }

    public void Dispose() { }
}
