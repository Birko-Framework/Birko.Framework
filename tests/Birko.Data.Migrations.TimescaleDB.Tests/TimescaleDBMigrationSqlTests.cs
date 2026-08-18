using Birko.Data.Migrations.TimescaleDB;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// Offline tests for the pure TimescaleDB DDL builders. These exercise the string generation only
/// (no live TimescaleDB server), covering CR-H070 (compression policy must not hardcode
/// 'time'/'device_id'), CR-H071 (continuous aggregate must not emit a dangling GROUP BY comma), and
/// TASK-253 (every interpolated name resolved through the connector's producers).
///
/// <para>
/// <b>Why these fixtures now use PascalCase names.</b> Every pre-TASK-253 test here named its table
/// <c>metrics</c> — already lower case, so PostgreSQL's folding lands on the same relation whether the name
/// is quoted or not, and the fixture <i>cannot distinguish the fix from the defect</i>. That is exactly what
/// TASK-472 found about its own equivalent fixture one layer over. The lowercase cases are kept below as a
/// **discrimination control**, and measurement says precisely what they discriminate: reverting the column
/// <i>folding</i> fails 5 of 34 and <see cref="CreateHypertable_LowercaseNameStillWorks"/> survives, because
/// an already-lowercase column folds to itself. Reverting the table <i>quoting</i> fails 15 of 34 and takes
/// that case with it — correctly, since a table is quoted whatever its case. So it is a control for one of the
/// two reverts, not for both; claiming otherwise would overstate what a green lowercase case proves.
/// </para>
///
/// <para>
/// <b>The four treatments under test</b>, because this file is where they are all visible at once: a
/// <c>regclass</c> inside a literal is quoted then escaped; a <c>name</c> inside a literal is pre-folded and
/// never quoted; a real identifier position gets <c>QuoteIdentifier</c> alone; an expression fragment gets
/// escaping alone. <c>BuildCompressionPolicySql</c> needs the first and third for the <i>same table</i> in one
/// statement, which is why it gets its own test.
/// </para>
/// </summary>
public class TimescaleDBMigrationSqlTests
{
    /// <summary>
    /// A real <c>TimescaleDBConnector</c> rather than a fake: it is what production uses, its constructor
    /// opens no connection, and the folding behaviour under test comes from
    /// <c>PostgreSQLConnector.FoldsUnquotedIdentifiers</c>. A fake would have to restate the provider's
    /// answers, which is what these tests exist to check.
    /// </summary>
    private static AbstractConnector Connector()
        => new TimescaleDBConnector(new TimescaleDBSettings("localhost", "db", "u", "p", 5432, "ts", "1 day"));

    // ── create_hypertable: the two arguments need opposite treatments ──

    /// <summary>
    /// The load-bearing case. Table quoted (it is a regclass, re-parsed as an identifier, so bare it would
    /// fold to a relation that does not exist); column folded (it is a name, compared textually against
    /// <c>pg_attribute.attname</c>, and column definitions are emitted bare).
    /// </summary>
    [Fact]
    public void CreateHypertable_QuotesThePascalCaseTableAndFoldsTheColumn()
    {
        var sql = TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "SensorReadings", "Ts");

        sql.Should().Be("SELECT create_hypertable('\"SensorReadings\"', 'ts');");
    }

    /// <summary>
    /// The discrimination control: an already-lowercase name must keep working. If a revert breaks this too,
    /// the change was broader than intended.
    /// </summary>
    [Fact]
    public void CreateHypertable_LowercaseNameStillWorks()
    {
        var sql = TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "metrics", "ts");

        sql.Should().Be("SELECT create_hypertable('\"metrics\"', 'ts');");
    }

    [Fact]
    public void CreateHypertable_AppendsChunkIntervalWhenSupplied()
    {
        var sql = TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "Metrics", "Ts", "7 days");

        sql.Should().Be("SELECT create_hypertable('\"Metrics\"', 'ts', chunk_time_interval => interval '7 days');");
    }

    [Fact]
    public void CreateHypertable_OmitsChunkIntervalWhenNotSupplied()
        => TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "Metrics", "Ts")
            .Should().NotContain("chunk_time_interval");

    /// <summary>Both column arguments are names, so both fold; the partition count is an int.</summary>
    [Fact]
    public void CreateHypertableWithSpace_FoldsBothColumnsAndQuotesTheTable()
    {
        var sql = TimescaleDBMigration.BuildCreateHypertableWithSpaceSql(
            Connector(), "SensorReadings", "Ts", "DeviceId", 4, "1 day");

        sql.Should().Be("SELECT create_hypertable('\"SensorReadings\"', 'ts', 'deviceid', 4, chunk_time_interval => interval '1 day');");
    }

    // ── compression policy: the same table, two treatments, one statement ──

    /// <summary>
    /// <b>The clearest instance of the whole defect class.</b> <c>ALTER TABLE</c> takes a real identifier, so
    /// the table is quoted and nothing else; <c>add_compression_policy</c> takes a regclass inside a literal,
    /// so the same table is quoted <i>and</i> escaped for the literal. Reasoning from either alone leaves the
    /// statement broken at the other.
    /// </summary>
    [Fact]
    public void CompressionPolicy_TreatsTheTableAsIdentifierAndAsRegclass()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "SensorReadings", "7 days");

        sql.Should().Contain("ALTER TABLE \"SensorReadings\" SET");
        sql.Should().Contain("add_compression_policy('\"SensorReadings\"', INTERVAL '7 days')");
    }

    // CR-H070: segmentby must not be hardcoded to 'device_id' — it is opt-in and omitted by default.
    [Fact]
    public void CompressionPolicy_DefaultsOrderByTime_AndOmitsSegmentBy()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "metrics", "7 days");

        sql.Should().Contain("timescaledb.compress_orderby = 'time'");
        sql.Should().NotContain("compress_segmentby");
        sql.Should().NotContain("device_id");
    }

    /// <summary>
    /// The order/segment columns are expression fragments, <b>not</b> identifiers: they are passed through
    /// unfolded and unquoted, because the parser parses them (so its own folding applies) and a legitimate
    /// value may carry a direction keyword — see <see cref="CompressionPolicy_AcceptsADirectionKeyword"/>.
    /// </summary>
    [Fact]
    public void CompressionPolicy_UsesSuppliedOrderByAndSegmentBy()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "readings", "30 days", "recorded_at", "sensor_id");

        sql.Should().Contain("timescaledb.compress_orderby = 'recorded_at'");
        sql.Should().Contain("timescaledb.compress_segmentby = 'sensor_id'");
        sql.Should().NotContain("'time'");
        sql.Should().NotContain("device_id");
    }

    /// <summary>
    /// Pins that the fragment is <b>not</b> identifier-validated. <c>ts DESC</c> is a legal
    /// <c>compress_orderby</c> value, so a guard that demanded a bare identifier here would refuse working
    /// migrations. Escaping is the containment, and inside a literal it is complete.
    /// </summary>
    [Fact]
    public void CompressionPolicy_AcceptsADirectionKeyword()
        => TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "Readings", "30 days", "ts DESC")
            .Should().Contain("timescaledb.compress_orderby = 'ts DESC'");

    // ── retention and policy removal: regclass each ──

    [Fact]
    public void ContinuousAggregate_QuotesTheViewAndTheSourceTable()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "DailyStats", "Metrics", "1 day", "avg(value) AS avg_value");

        sql.Should().Contain("CREATE MATERIALIZED VIEW \"DailyStats\"");
        sql.Should().Contain("FROM \"Metrics\"");
    }

    // CR-H071: with no group-by columns, the GROUP BY clause must be just "GROUP BY bucket"
    // (no trailing comma).
    [Fact]
    public void ContinuousAggregate_EmptyGroupBy_HasNoDanglingComma()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "daily_stats", "metrics", "1 day", "avg(value) AS avg_value");

        sql.Should().Contain("GROUP BY bucket;");
        sql.Should().NotContain("GROUP BY bucket,");
        // The SELECT list separates bucket from the aggregate with exactly one comma.
        sql.Should().Contain("AS bucket,");
        sql.Should().NotContain("AS bucket ,");
    }

    [Fact]
    public void ContinuousAggregate_WithGroupBy_IncludesColumnsInSelectAndGroupBy()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "daily_by_device", "metrics", "1 day", "avg(value) AS avg_value", "device_id");

        sql.Should().Contain("AS bucket, device_id,");
        sql.Should().Contain("GROUP BY bucket, device_id;");
    }

    /// <summary>
    /// <b>Pins the boundary TASK-260 will remove, so nobody closes it by accident.</b> The select and group-by
    /// clauses are interpolated as raw SQL — an expression group-by such as <c>date_trunc('day', x)</c> is
    /// legitimate and must survive. Identifier-validating them would refuse working migrations while leaving
    /// the neighbouring argument open anyway, which is why the fix is an API change rather than a guard.
    /// </summary>
    [Fact]
    public void ContinuousAggregate_LeavesExpressionClausesIntact()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", "1 day", "sum(value) AS total", "date_trunc('day', x)");

        sql.Should().Contain("date_trunc('day', x)");
        sql.Should().Contain("sum(value) AS total");
    }

    /// <summary>
    /// The bucketing column is still the literal <c>time</c> — CR-H070's defect surviving in this method,
    /// owned by TASK-255. Pinned as <i>current</i> behaviour, not as correct behaviour, so the day it changes
    /// the change is deliberate and visible.
    /// </summary>
    [Fact]
    public void ContinuousAggregate_StillHardcodesTheTimeColumn_TASK255()
        => TimescaleDBMigration.BuildContinuousAggregateSql(
                Connector(), "Rollup", "Metrics", "1 day", "sum(value) AS total")
            .Should().Contain("time_bucket('1 day', time)");
}
