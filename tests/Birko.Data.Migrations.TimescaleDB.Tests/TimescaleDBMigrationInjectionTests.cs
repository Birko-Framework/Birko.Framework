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
/// <b>TASK-260 closed the last uncontained pair.</b> This file used to say two arguments were deliberately
/// NOT covered — <c>selectClause</c> and <c>groupByClause</c> were raw SQL in statement position, so no
/// escaping could contain them and an injection test would have asserted a guarantee the API did not make.
/// They are gone: the projection and grouping are structured values whose identifiers are validated and
/// whose one literal is escaped, so every caller-derived input in this class is now contained by one of the
/// three mechanisms. The tests below cover each new input.
/// </para>
/// <para>
/// <b>Note the function name is contained by refusal too, and is NOT a passthrough.</b> A passthrough would
/// accept arbitrary text; this accepts a single bare identifier. A name that merely does not exist is not an
/// injection — it is a statement that fails at DDL time with <c>42883</c> naming the function and its
/// argument types, which is the "wrong answer that reports itself" property the validator claims.
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
            Connector(), IdentifierBreakout, IdentifierBreakout, "1 day", "Ts", new[] { ContinuousAggregateProjection.OfAll("count", "n") });

        ContainedAsIdentifier(sql, IdentifierBreakout);
    }

    [Fact]
    public void ContinuousAggregate_ContainsABreakoutInTheTimeBucket()
    {
        var sql = TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", LiteralBreakout, "Ts", new[] { ContinuousAggregateProjection.OfAll("count", "n") });

        ContainedAsLiteral(sql, LiteralBreakout);
    }

    /// <summary>
    /// TASK-281's new policy emitter contains its view name the same way every other <c>regclass</c> sink
    /// does. Added with the emitter rather than after it, because a new sink that skips this file is how the
    /// containment rule gets a hole.
    /// </summary>
    [Fact]
    public void ContinuousAggregatePolicy_ContainsABreakoutInTheView()
        => ContainedAsRegclass(
            TimescaleDBMigration.BuildContinuousAggregatePolicySql(
                Connector(), LiteralBreakout, "30 days", "1 hour", "1 hour"),
            LiteralBreakout);

    /// <summary>The offsets are expression fragments inside literals, so escaping contains them completely.</summary>
    [Fact]
    public void ContinuousAggregatePolicy_ContainsABreakoutInAnOffset()
        => ContainedAsLiteral(
            TimescaleDBMigration.BuildContinuousAggregatePolicySql(
                Connector(), "DailyStats", LiteralBreakout, "1 hour", "1 hour"),
            LiteralBreakout);

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
            Connector(), "Rollup", "Metrics", "1 day", payload,
            new[] { ContinuousAggregateProjection.OfAll("count", "n") });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not a plain, unqualified column identifier*",
                "the column is emitted bare, so refusal is the only containment available");
    }

    // ── TASK-260: the structured projection and grouping, which replaced the two raw-SQL clauses ──

    /// <summary>
    /// Every identifier in a projection is refused when it is not a bare identifier — function, column and
    /// alias alike. This is the payload that reached the DDL uncontained through the old
    /// <c>selectClause</c>; it now has no path at all.
    /// </summary>
    [Theory]
    [InlineData("fn")]
    [InlineData("column")]
    [InlineData("alias")]
    public void ContinuousAggregateProjection_RefusesABreakoutInEveryIdentifier(string slot)
    {
        var projection = slot switch
        {
            "fn" => ContinuousAggregateProjection.Of(LiteralBreakout, "value", "total"),
            "column" => ContinuousAggregateProjection.Of("sum", LiteralBreakout, "total"),
            _ => ContinuousAggregateProjection.Of("sum", "value", LiteralBreakout),
        };

        var act = () => TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", "1 day", "Ts", new[] { projection });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not a plain, unqualified column identifier*",
                "these are emitted bare into the SELECT list, so refusal is the only containment available");
    }

    /// <summary>The second column of a two-argument aggregate is validated on the same terms.</summary>
    [Fact]
    public void ContinuousAggregateProjection_RefusesABreakoutInTheSecondColumn()
    {
        var act = () => TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", "1 day", "Ts",
            new[] { ContinuousAggregateProjection.OfPair("first", "value", LiteralBreakout, "f") });

        act.Should().Throw<ArgumentException>().WithMessage("*not a plain, unqualified column identifier*");
    }

    /// <summary>A grouping's function and column are identifiers; both are refused when they are not.</summary>
    [Theory]
    [InlineData("fn")]
    [InlineData("column")]
    public void ContinuousAggregateGrouping_RefusesABreakoutInEveryIdentifier(string slot)
    {
        var grouping = slot == "fn"
            ? ContinuousAggregateGrouping.Expression(LiteralBreakout, "day", "Ts")
            : ContinuousAggregateGrouping.Of(LiteralBreakout);

        var act = () => TimescaleDBMigration.BuildContinuousAggregateSql(
            Connector(), "Rollup", "Metrics", "1 day", "Ts",
            new[] { ContinuousAggregateProjection.OfAll("count", "n") }, new[] { grouping });

        act.Should().Throw<ArgumentException>().WithMessage("*not a plain, unqualified column identifier*");
    }

    /// <summary>
    /// The grouping's literal argument is a <i>value</i>, not an identifier — so it is contained by escaping
    /// rather than refusal, exactly as the time bucket and the policy intervals are. Refusing it would break
    /// legitimate values, which is the mistake TASK-253 warned against for expression fragments.
    /// </summary>
    [Fact]
    public void ContinuousAggregateGrouping_ContainsABreakoutInTheLiteralArgument()
        => ContainedAsLiteral(
            TimescaleDBMigration.BuildContinuousAggregateSql(
                Connector(), "Rollup", "Metrics", "1 day", "Ts",
                new[] { ContinuousAggregateProjection.OfAll("count", "n") },
                new[] { ContinuousAggregateGrouping.Expression("date_trunc", LiteralBreakout, "Ts") }),
            LiteralBreakout);

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

        TimescaleDBMigration.BuildContinuousAggregateSql(Connector(), "DailyStats", "Metrics", "1 day", "Ts", new[] { ContinuousAggregateProjection.OfAll("count", "n") })
            .Should().Contain("CREATE MATERIALIZED VIEW \"DailyStats\"");
    }
}
