using Birko.Data.Migrations.TimescaleDB;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// Offline tests for the pure TimescaleDB DDL builders. These exercise the string generation only
/// (no live TimescaleDB server), covering CR-H070 (compression policy must not hardcode
/// 'time'/'device_id') and CR-H071 (continuous aggregate must not emit a dangling GROUP BY comma).
/// </summary>
public class TimescaleDBMigrationSqlTests
{
    // CR-H070: segmentby must not be hardcoded to 'device_id' — it is opt-in and omitted by default.
    [Fact]
    public void CompressionPolicy_DefaultsOrderByTime_AndOmitsSegmentBy()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql("metrics", "7 days");

        sql.Should().Contain("timescaledb.compress_orderby = 'time'");
        sql.Should().NotContain("compress_segmentby");
        sql.Should().NotContain("device_id");
        sql.Should().Contain("ALTER TABLE metrics SET");
        sql.Should().Contain("add_compression_policy('metrics', INTERVAL '7 days')");
    }

    [Fact]
    public void CompressionPolicy_UsesSuppliedOrderByAndSegmentBy()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql("readings", "30 days", "recorded_at", "sensor_id");

        sql.Should().Contain("timescaledb.compress_orderby = 'recorded_at'");
        sql.Should().Contain("timescaledb.compress_segmentby = 'sensor_id'");
        sql.Should().NotContain("'time'");
        sql.Should().NotContain("device_id");
    }

    // CR-H071: with no group-by columns, the GROUP BY clause must be just "GROUP BY bucket"
    // (no trailing comma).
    [Fact]
    public void ContinuousAggregate_EmptyGroupBy_HasNoDanglingComma()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            "daily_stats", "metrics", "1 day", "avg(value) AS avg_value");

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
            "daily_by_device", "metrics", "1 day", "avg(value) AS avg_value", "device_id");

        sql.Should().Contain("AS bucket, device_id,");
        sql.Should().Contain("GROUP BY bucket, device_id;");
    }
}
