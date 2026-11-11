using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// TASK-706 (SH-T696-01) — an aggregate's time bucket on the one provider that has <c>time_bucket</c>, live.
///
/// <para>An interval <c>TimeIntervalParser.ToSqlInterval</c> could not parse was put inside <c>time_bucket('…')</c> as
/// written, so a caller's interval string was SQL. It is now parsed or refused, and the SQL carries only the numbers
/// the parse produced. A time column that is no property was put in as written too.</para>
///
/// <para>Gated on <c>BIRKO_TS_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>);
/// <c>BIRKO_REQUIRE_LIVE</c> makes its absence a failure.</para>
/// </summary>
public class Task706TimeBucketLiveTests : IDisposable
{
    private const string TableName = "TsAgg706Rows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => LiveDatabase.Name("TS");
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task706TimeBucketLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static TimescaleDBSettings Settings() => new(Host!, Database, User, Password, Port, "Ts", "1 day");

    public class AggRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public DateTime Ts { get; set; }
    }

    private sealed class AggRowMapping : IModelMapping<AggRow>
    {
        public void Configure(ModelMap<AggRow> map)
        {
            map.ToTable(TableName).HasPrimary(x => x.Ts);
            map.Property(x => x.Name);
            map.Property(x => x.Amount);
            map.Property(x => x.Ts);
        }
    }

    static Task706TimeBucketLiveTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AggRowMapping());
        registry.ApplyToDatabase();
    }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE"); } catch { }
    }

    private static TimescaleDBStore<AggRow> SeededStore()
    {
        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
        new TimescaleDBConnector(Settings()).CreateTable(new[] { typeof(AggRow) });
        var store = new TimescaleDBStore<AggRow>();
        store.SetSettings(Settings());
        var at = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Create(new List<AggRow>
        {
            new() { Guid = Guid.NewGuid(), Name = "a", Amount = 1, Ts = at.AddMinutes(5) },
            new() { Guid = Guid.NewGuid(), Name = "a", Amount = 2, Ts = at.AddMinutes(40) },
            new() { Guid = Guid.NewGuid(), Name = "b", Amount = 4, Ts = at.AddMinutes(70) },
        });
        return store;
    }

    [Fact]
    public void An_hourly_bucket_sums_per_hour()
    {
        if (!RequireServer()) return;
        var rows = SeededStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "Ts",
            TimeBucketInterval = "1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount", "total") },
            OrderBy = new Dictionary<string, bool> { ["bucket_time"] = false },
        });

        rows.Select(r => (r.GetBucketTime()!.Value.Hour, Convert.ToInt64(r.Values["total"]))).Should().Equal((10, 3L), (11, 4L));
    }

    [Fact]
    public void A_timespan_interval_buckets_too()
    {
        if (!RequireServer()) return;
        var rows = SeededStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "Ts",
            TimeBucketInterval = "00:30:00",
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount", "n") },
        });

        rows.Should().HaveCount(3, "10:05, 10:40 and 11:10 fall into three half-hour buckets");
    }

    [Fact]
    public void An_interval_that_does_not_parse_is_refused_not_put_into_the_SQL()
    {
        if (!RequireServer()) return;
        // Measured before the fix: this ran, no error — the caller's text was SQL.
        var act = () => SeededStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "Ts",
            TimeBucketInterval = "1 hour', Ts) AS b, (SELECT 1) AS z, time_bucket('1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount", "n") },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*interval*");
    }

    [Fact]
    public async Task The_async_store_buckets_and_refuses_the_same()
    {
        if (!RequireServer()) return;
        SeededStore();
        var store = new AsyncTimescaleDBStore<AggRow>();
        store.SetSettings(Settings());

        var rows = await store.AggregateAsync(new AggregateQuery<AggRow>
        {
            TimeColumn = "Ts",
            TimeBucketInterval = "1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount", "total") },
        });
        rows.Should().HaveCount(2);

        var bad = async () => await store.AggregateAsync(new AggregateQuery<AggRow>
        {
            TimeColumn = "now()",
            TimeBucketInterval = "1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount", "total") },
        });
        await bad.Should().ThrowAsync<ArgumentException>().WithMessage("*now()*");
    }
}
