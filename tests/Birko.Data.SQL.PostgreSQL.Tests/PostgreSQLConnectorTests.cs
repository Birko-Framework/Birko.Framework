using System;
using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// CR-H092: the PostgreSQL backend had no test project. These offline tests cover the pure-function
/// surface — ConvertType type mapping (including the DbType.Single -> REAL fix), the inherited
/// double-quote identifier quoting, and the connection-string assembly (no live PostgreSQL required).
/// </summary>
public class PostgreSQLConnectorTests
{
    private sealed class Sample
    {
        public DateTime When { get; set; }
    }

    private static PostgreSQLConnector NewConnector()
        => new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    private static DateTimeField DateTimeField()
        => new(typeof(Sample).GetProperty(nameof(Sample.When))!, "When");

    // CR-L176: the missing-table seam recognizes PostgreSQL's 'relation "x" does not exist' wording
    // (plus the inherited SQLite base match) so a reader over a missing table yields empty, not a fault.
    //
    // TASK-211 narrowed it. This seam decides whether the reader answers an error with an EMPTY RESULT,
    // which is a wrong answer rather than a failure, so everything it accepts had better really be a
    // missing table. It used to accept any message containing "does not exist" — which is also how
    // PostgreSQL phrases a missing COLUMN, function, type and object.
    [Theory]
    [InlineData("relation \"widgets\" does not exist", true)]
    [InlineData("no such table: widgets", true)]
    [InlineData("some other error", false)]
    // ── the shapes the old catch-all wrongly swallowed ──
    [InlineData("column \"Name\" does not exist", false)]
    [InlineData("function lower(integer) does not exist", false)]
    [InlineData("type \"foo\" does not exist", false)]
    // 42P01's OTHER message. The relation exists; the STATEMENT is wrong — and this is the exact error
    // the framework's own qualifier defect produced, so the swallow hid the bug that produced it.
    [InlineData("missing FROM-clause entry for table \"widgets\"", false)]
    public void IsMissingTableException_matches_postgres_and_base_wording(string message, bool expected)
    {
        NewConnector().IsMissingTableException(new Exception(message)).Should().Be(expected);
    }

    [Theory]
    [InlineData(DbType.DateTime, "TIMESTAMP")]
    [InlineData(DbType.DateTime2, "TIMESTAMP")]
    [InlineData(DbType.Date, "DATE")]
    [InlineData(DbType.Time, "TIME")]
    [InlineData(DbType.Single, "REAL")]          // was SMALLINT (truncating floats) — now fixed
    [InlineData(DbType.Double, "DOUBLE PRECISION")]
    [InlineData(DbType.Boolean, "BOOLEAN")]
    [InlineData(DbType.Guid, "UUID")]
    [InlineData(DbType.Int32, "INTEGER")]
    [InlineData(DbType.Int64, "BIGINT")]
    public void ConvertType_MapsTypes(DbType type, string expected)
    {
        NewConnector().ConvertType(type, DateTimeField()).Should().Be(expected);
    }

    [Fact]
    public void QuoteIdentifier_DoubleQuotes_And_EscapesQuote()
    {
        var connector = NewConnector();
        connector.QuoteIdentifier("Widgets").Should().Be("\"Widgets\"");
        connector.QuoteIdentifier("weird\"name").Should().Be("\"weird\"\"name\"");
    }

    [Fact]
    public void GetConnectionString_ContainsHostAndCredentials()
    {
        // CR-L189: composed via NpgsqlConnectionStringBuilder — a non-default port is emitted; the builder
        // omits keys at their Npgsql default (e.g. Port 5432, Timeout 15), so assert via a round-trip.
        var settings = new PostgreSqlSettings("srv", "mydb", "u", "p", port: 6000, useSecure: true);
        var cs = settings.GetConnectionString();
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(cs);

        parsed.Host.Should().Be("srv");
        parsed.Port.Should().Be(6000);
        parsed.Username.Should().Be("u");
        parsed.Password.Should().Be("p");
        parsed.Database.Should().Be("mydb");
        parsed.SslMode.Should().Be(Npgsql.SslMode.Require);
    }

    // CR-L189: a password containing ';' and '=' must survive the composition (builder quoting) rather
    // than breaking the key=value parsing or injecting extra keywords.
    [Fact]
    public void GetConnectionString_EscapesSpecialCharactersInValues()
    {
        var settings = new PostgreSqlSettings("srv", "mydb", "u", "p;a=ss'w", port: 6000);
        var cs = settings.GetConnectionString();

        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(cs);
        parsed.Password.Should().Be("p;a=ss'w");
        parsed.Database.Should().Be("mydb");
    }

    // ── TASK-253: the folding capability ──

    /// <summary>
    /// PostgreSQL is the one supported provider that case-folds an unquoted identifier, and
    /// <see cref="AbstractConnectorBase.FoldsUnquotedIdentifiers"/> is where the framework states it.
    /// <para>
    /// This assertion is the whole reason the base's <c>CatalogueNameLiteral</c> folds at all. It is
    /// inherited by <c>TimescaleDBConnector</c>, which is where TASK-472 measured the consequence: a time
    /// column emitted unfolded inside a <c>create_hypertable</c> literal raised
    /// <c>42703 column "Ts" does not exist</c>, because the parser never folds a string value and the
    /// framework emits column definitions bare (TASK-209).
    /// </para>
    /// </summary>
    [Fact]
    public void FoldsUnquotedIdentifiers_IsTrue()
        => NewConnector().FoldsUnquotedIdentifiers.Should().BeTrue();

    /// <summary>
    /// The two halves of one <c>create_hypertable</c> call, asserted together because they are the pair that
    /// needs OPPOSITE treatments and reasoning from either alone is what produced the defect: the table is a
    /// <c>regclass</c> re-parsed as an identifier, so it carries quotes and is never folded; the column is a
    /// <c>name</c> compared textually against the catalogue, so it is folded and never quoted.
    /// </summary>
    [Fact]
    public void RegclassAndCatalogueName_TakeOppositeTreatments()
    {
        var connector = NewConnector();

        connector.RegclassLiteral("SensorReadings").Should().Be("\"SensorReadings\"");
        connector.CatalogueNameLiteral("Ts").Should().Be("ts");
    }
}
