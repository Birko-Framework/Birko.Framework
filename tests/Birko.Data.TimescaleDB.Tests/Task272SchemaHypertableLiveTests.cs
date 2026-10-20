using System;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// TASK-272, live on TimescaleDB: an entity mapped with <c>ToTable(name, schema)</c> is created in that schema and becomes
/// a hypertable there. <c>create_hypertable</c> was handed the bare table name, which resolves on the search path only.
/// Gated on <c>BIRKO_TS_HOST</c>.
/// </summary>
public class Task272SchemaHypertableLiveTests : IDisposable
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private readonly ITestOutputHelper _output;

    public Task272SchemaHypertableLiveTests(ITestOutputHelper output) => _output = output;

    public class T272TsRow : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime Ts { get; set; }
    }

    private sealed class Mapping : IModelMapping<T272TsRow>
    {
        public void Configure(ModelMap<T272TsRow> map)
        {
            map.ToTable("T272TsRows", "t272ts").HasPrimary(x => x.Ts);
            map.Property(x => x.Name);
            map.Property(x => x.Ts);
        }
    }

    static Task272SchemaHypertableLiveTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new Mapping());
        registry.ApplyToDatabase();
    }

    private static TimescaleDBSettings Settings()
        => new(Host!, Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview",
            Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres",
            Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres", Port, "Ts", "1 day");

    private static string Scalar(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "<null>";
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Scalar("DROP TABLE IF EXISTS t272ts.\"T272TsRows\" CASCADE; SELECT 1"); } catch { }
    }

    [Fact]
    public void A_mapped_entity_in_a_schema_becomes_a_hypertable_there()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            _output.WriteLine("SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_TS_HOST is not.");
            return;
        }
        Scalar("DROP TABLE IF EXISTS t272ts.\"T272TsRows\" CASCADE; SELECT 1");

        new TimescaleDBConnector(Settings()).CreateTable(new[] { typeof(T272TsRow) });

        Scalar("SELECT to_regclass('t272ts.\"T272TsRows\"') IS NOT NULL").Should().Be("True", "created in its schema");
        Scalar("SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_schema = 't272ts' AND hypertable_name = 'T272TsRows'")
            .Should().Be("1", "and converted there");
    }
}
