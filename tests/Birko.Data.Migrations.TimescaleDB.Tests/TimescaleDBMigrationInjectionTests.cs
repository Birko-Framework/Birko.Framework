using System.Linq;
using Birko.Data.Migrations.TimescaleDB;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// TASK-253 — containment for every caller-derived name in <c>TimescaleDBMigration</c>. Modelled on
/// <c>Birko.Data.SQL.Tests.IndexManagement.IndexIdentifierInjectionTests</c>.
///
/// <para>
/// <b>Before this, these sinks had no escaping whatsoever.</b> Every table, view and column name was
/// interpolated raw — unlike <c>TimescaleDBConnector.BuildCreateHypertableSql</c> one layer over, which at
/// least doubled single quotes. A migration author is a developer rather than an end user, so this is
/// defence in depth rather than a public attack surface; the reason it still matters is that a migration is
/// exactly the place a table name gets built from configuration or a loop variable, and a broken statement
/// there fails a deployment.
/// </para>
///
/// <para>
/// <b>What containment means here, and why it is complete.</b> Almost every one of these arguments ends up
/// inside a single-quoted literal or as a quoted identifier, and doubling the relevant quote character means
/// the payload cannot leave its enclosure — so the statement stays one statement. The premise is
/// <c>standard_conforming_strings = on</c> (PostgreSQL's default since 9.1), stated on
/// <see cref="Birko.Data.SQL.SqlLiteral"/>.
/// </para>
///
/// <para>
/// <b>There is a THIRD containment mechanism as of TASK-255: refusal.</b>
/// <c>BuildContinuousAggregateSql</c>'s <c>timeColumn</c> is a column reference in real identifier position
/// inside the view body, so it must be emitted <b>bare</b> to resolve the folded column that bare-column
/// <c>CREATE TABLE</c> creates — which means no quote character encloses it and escaping would contain
/// nothing. It is guarded by <see cref="Birko.Data.SQL.DataBase.ValidateColumnIdentifier"/> instead, so its
/// test asserts a <b>throw</b> rather than an escaped form, unlike every other test in this file. Do not
/// "fix" that asymmetry by asserting an escaped payload: there is no enclosure to escape into.
/// </para>
///
/// <para>
/// <b>Two arguments are deliberately NOT covered</b> — <c>selectClause</c> and <c>groupByClause</c> are raw
/// SQL by design and cannot be contained at all. That boundary is pinned as current behaviour in
/// <see cref="TimescaleDBMigrationSqlTests.ContinuousAggregate_LeavesExpressionClausesIntact"/> and owned by
/// TASK-260. Adding an injection test for them would assert a guarantee this API does not make.
/// </para>
/// </summary>
public class TimescaleDBMigrationInjectionTests
{
    /// <summary>Closes the literal, appends a statement, comments out the tail — the SH-H023 shape.</summary>
    private const string LiteralBreakout = "Rank'); CREATE TABLE Pwned (x INTEGER); --";

    /// <summary>Closes the *identifier* instead, which is the door the literal escaping alone would leave.</summary>
    private const string IdentifierBreakout = "Rank\"); CREATE TABLE Pwned (x INTEGER); --";

    private static AbstractConnector Connector()
        => new TimescaleDBConnector(new TimescaleDBSettings("localhost", "db", "u", "p", 5432, "ts", "1 day"));

    /// <summary>
    /// <b>Containment does not mean the payload's characters disappear</b> -- they survive as inert text,
    /// which is the point. It means the payload cannot leave its enclosure, so the statement stays one
    /// statement. The first draft of this file asserted <c>NotContain(";")</c> and <c>NotContain("--")</c> and
    /// failed 16 of 16 against correct code, because a contained <c>--</c> is still a <c>--</c> sitting inside
    /// a string.
    /// <para>
    /// So each helper asserts the payload appears in exactly its <i>escaped</i> form for the treatment that
    /// sink uses, plus one global structural invariant: after collapsing every doubled quote, the remaining
    /// quotes -- the real delimiters -- must be even in number. An escape that dropped or added one would
    /// break that count.
    /// </para>
    /// </summary>
    private static void LiteralDelimitersBalanced(string sql)
    {
        var delimiters = sql.Replace("''", "").Count(c => c == '\'');
        (delimiters % 2).Should().Be(0, "an unbalanced literal delimiter is how a payload escapes its quotes");
    }

    /// <summary>A value inside a single-quoted literal: every <c>'</c> doubled, nothing else touched.</summary>
    private static void ContainedAsLiteral(string sql, string payload)
    {
        sql.Should().Contain(payload.Replace("'", "''"));
        LiteralDelimitersBalanced(sql);
    }

    /// <summary>
    /// A <c>regclass</c>: quoted as an identifier (doubling <c>"</c>), then the whole thing escaped for the
    /// literal (doubling <c>'</c>). Both kinds have to hold at once, which makes this the strictest of the
    /// three.
    /// </summary>
    private static void ContainedAsRegclass(string sql, string payload)
    {
        var quoted = "\"" + payload.Replace("\"", "\"\"") + "\"";
        sql.Should().Contain(quoted.Replace("'", "''"));
        LiteralDelimitersBalanced(sql);
    }

    /// <summary>A real identifier position: quoted only, no literal escaping.</summary>
    private static void ContainedAsIdentifier(string sql, string payload)
    {
        sql.Should().Contain("\"" + payload.Replace("\"", "\"\"") + "\"");
        LiteralDelimitersBalanced(sql);
    }

    [Fact]
    public void CreateHypertable_ContainsALiteralBreakoutInTheTable()
        => ContainedAsRegclass(TimescaleDBMigration.BuildCreateHypertableSql(Connector(), LiteralBreakout, "ts"), LiteralBreakout);

    [Fact]
    public void CreateHypertable_ContainsAnIdentifierBreakoutInTheTable()
        => ContainedAsRegclass(TimescaleDBMigration.BuildCreateHypertableSql(Connector(), IdentifierBreakout, "ts"), IdentifierBreakout);

    [Fact]
    public void CreateHypertable_ContainsABreakoutInTheTimeColumn()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "Metrics", LiteralBreakout), LiteralBreakout.ToLowerInvariant());

    [Fact]
    public void CreateHypertable_ContainsABreakoutInTheChunkInterval()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCreateHypertableSql(Connector(), "Metrics", "ts", LiteralBreakout), LiteralBreakout);

    [Fact]
    public void CreateHypertableWithSpace_ContainsABreakoutInTheSpaceColumn()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCreateHypertableWithSpaceSql(
            Connector(), "Metrics", "ts", LiteralBreakout, 4), LiteralBreakout.ToLowerInvariant());

    /// <summary>
    /// The compression policy is the interesting one: the table reaches an <c>ALTER TABLE</c> identifier
    /// position <b>and</b> a regclass literal in the same statement, so a payload has two doors and both must
    /// hold.
    /// </summary>
    [Fact]
    public void CompressionPolicy_ContainsABreakoutInTheTable()
    {
        var sql = TimescaleDBMigration.BuildCompressionPolicySql(Connector(), IdentifierBreakout, "7 days");

        ContainedAsIdentifier(sql, IdentifierBreakout);   // the ALTER TABLE door
        ContainedAsRegclass(sql, IdentifierBreakout);     // the add_compression_policy door
    }

    [Fact]
    public void CompressionPolicy_ContainsABreakoutInTheOrderByFragment()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "Metrics", "7 days", LiteralBreakout), LiteralBreakout);

    [Fact]
    public void CompressionPolicy_ContainsABreakoutInTheSegmentByFragment()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "Metrics", "7 days", "ts", LiteralBreakout), LiteralBreakout);

    [Fact]
    public void CompressionPolicy_ContainsABreakoutInTheInterval()
        => ContainedAsLiteral(TimescaleDBMigration.BuildCompressionPolicySql(Connector(), "Metrics", LiteralBreakout), LiteralBreakout);

    [Fact]
    public void RetentionPolicy_ContainsABreakoutInTheTable()
        => ContainedAsRegclass(TimescaleDBMigration.BuildRetentionPolicySql(Connector(), LiteralBreakout, "30 days"), LiteralBreakout);

    [Fact]
    public void RetentionPolicy_ContainsABreakoutInTheInterval()
        => ContainedAsLiteral(TimescaleDBMigration.BuildRetentionPolicySql(Connector(), "Metrics", LiteralBreakout), LiteralBreakout);

    [Fact]
    public void RemoveCompressionPolicy_ContainsABreakoutInTheTable()
        => ContainedAsRegclass(TimescaleDBMigration.BuildRemoveCompressionPolicySql(Connector(), LiteralBreakout), LiteralBreakout);

    [Fact]
    public void RemoveRetentionPolicy_ContainsABreakoutInTheTable()
        => ContainedAsRegclass(TimescaleDBMigration.BuildRemoveRetentionPolicySql(Connector(), LiteralBreakout), LiteralBreakout);

    [Fact]
    public void RefreshContinuousAggregate_ContainsABreakoutInTheView()
        => ContainedAsRegclass(TimescaleDBMigration.BuildRefreshContinuousAggregateSql(Connector(), LiteralBreakout), LiteralBreakout);

    /// <summary>
    /// The aggregate's <i>identifiers</i> are contained even though its clauses are not — the view name and
    /// source table are quoted identifiers, so a payload in either cannot escape.
    /// </summary>
    [Fact]
    public void ContinuousAggregate_ContainsABreakoutInTheViewAndSourceTable()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), IdentifierBreakout, IdentifierBreakout, "1 day", "Ts", "count(*) AS n");

        ContainedAsIdentifier(sql, IdentifierBreakout);
    }

    [Fact]
    public void ContinuousAggregate_ContainsABreakoutInTheTimeBucket()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", LiteralBreakout, "Ts", "count(*) AS n");

        ContainedAsLiteral(sql, LiteralBreakout);
    }

    /// <summary>
    /// <b>The one argument contained by refusal rather than by escaping</b> (TASK-255) — so this asserts a
    /// throw, not an escaped payload. See the class remarks: <c>timeColumn</c> is emitted bare, because a
    /// quoted identifier cannot resolve the folded column that bare-column <c>CREATE TABLE</c> creates, and
    /// bare leaves no enclosure for escaping to contain.
    /// <para>
    /// Both payload shapes are tried, because the literal one is what the escaping-based sinks defend
    /// against and the identifier one is the door quoting would leave — neither is available here, and the
    /// guard must refuse both.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(LiteralBreakout)]
    [InlineData(IdentifierBreakout)]
    public void ContinuousAggregate_RefusesABreakoutInTheTimeColumn(string payload)
    {
        var act = () => TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", "1 day", payload, "count(*) AS n");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not a plain, unqualified column identifier*",
                "the column is emitted bare, so refusal is the only containment available");
    }

    // ── the four extracted builders also need their happy path pinned ──

    [Fact]
    public void RetentionPolicy_QuotesTheTableAsARegclass()
        => TimescaleDBMigration.BuildRetentionPolicySql(Connector(), "SensorReadings", "30 days")
            .Should().Be("SELECT add_retention_policy('\"SensorReadings\"', INTERVAL '30 days');");

    [Fact]
    public void RemoveCompressionPolicy_QuotesTheTableAsARegclass()
        => TimescaleDBMigration.BuildRemoveCompressionPolicySql(Connector(), "SensorReadings")
            .Should().Be("SELECT remove_compression_policy('\"SensorReadings\"');");

    [Fact]
    public void RemoveRetentionPolicy_QuotesTheTableAsARegclass()
        => TimescaleDBMigration.BuildRemoveRetentionPolicySql(Connector(), "SensorReadings")
            .Should().Be("SELECT remove_retention_policy('\"SensorReadings\"');");

    /// <summary>
    /// The same view name is a <b>regclass</b> here and a <b>plain identifier</b> in
    /// <c>BuildContinuousAggregateSql</c>. Asserted side by side so the difference reads as deliberate.
    /// </summary>
    [Fact]
    public void RefreshContinuousAggregate_TreatsTheViewAsARegclass_UnlikeTheCreate()
    {
        TimescaleDBMigration.BuildRefreshContinuousAggregateSql(Connector(), "DailyStats")
            .Should().Be("CALL refresh_continuous_aggregate('\"DailyStats\"', NULL, NULL);");

        TimescaleDBMigration.BuildContinuousAggregateSql(Connector(), "DailyStats", "Metrics", "1 day", "Ts", "count(*) AS n")
            .Should().Contain("CREATE MATERIALIZED VIEW \"DailyStats\"");
    }
}
