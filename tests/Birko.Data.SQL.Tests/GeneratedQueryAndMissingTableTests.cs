using System;
using System.Data;
using Birko.Data.SQL;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// CR-L173: DataBase.GetGeneratedQuery must replace longer parameter names first, otherwise a name that
/// is a prefix of another (@WHEREName0_5 vs @WHEREName0_50) corrupts the rendered diagnostic SQL.
/// CR-L176: AbstractConnectorBase.IsMissingTableException is the overridable seam whose base match is
/// SQLite's "no such table" wording.
/// </summary>
public class GeneratedQueryAndMissingTableTests
{
    private static TestDbParameter Param(string name, DbType type, object? value)
        => new() { ParameterName = name, DbType = type, Value = value };

    [Fact]
    public void GetGeneratedQuery_replaces_longer_parameter_names_first()
    {
        var cmd = new TestDbCommand
        {
            CommandText = "SELECT * FROM T WHERE A = @WHEREName0_5 AND B = @WHEREName0_50",
        };
        // Added shorter-name-first on purpose — the render must still resolve each independently.
        cmd.Parameters.Add(Param("@WHEREName0_5", DbType.Int32, 7));
        cmd.Parameters.Add(Param("@WHEREName0_50", DbType.Int32, 42));

        var rendered = global::Birko.Data.SQL.DataBase.GetGeneratedQuery(cmd);

        rendered.Should().Be("SELECT * FROM T WHERE A = 7 AND B = 42");
    }

    [Fact]
    public void GetGeneratedQuery_quotes_string_parameters()
    {
        var cmd = new TestDbCommand { CommandText = "SELECT * FROM T WHERE Name = @p0" };
        cmd.Parameters.Add(Param("@p0", DbType.String, "abc"));

        global::Birko.Data.SQL.DataBase.GetGeneratedQuery(cmd).Should().Be("SELECT * FROM T WHERE Name = 'abc'");
    }

    [Theory]
    [InlineData("SQLite Error 1: 'no such table: Widgets'.", true)]
    [InlineData("NO SUCH TABLE: widgets", true)]
    [InlineData("some unrelated error", false)]
    public void Base_IsMissingTableException_matches_sqlite_wording(string message, bool expected)
    {
        var connector = new FakeConnector();

        connector.IsMissingTableException(new Exception(message)).Should().Be(expected);
    }


    // ─────────────────────────────── TASK-293: which table is missing ───────────────────────────────

    /// <summary>
    /// The base extractor reads SQLite's wording, matching <c>IsMissingTableException</c>. It is the
    /// exact discriminator that replaced a substring search over the whole statement, so the shapes it
    /// has to survive are pinned here rather than only through a live database.
    /// </summary>
    [Theory]
    [InlineData("SQLite Error 1: 'no such table: Widgets'.", "Widgets")]
    [InlineData("no such table: Widgets", "Widgets")]
    // An explicit database prefix: TablesCreated is keyed by the bare framework table name, so the
    // qualifier has to go or the lookup fails to find the entry it is looking for.
    [InlineData("SQLite Error 1: 'no such table: main.Widgets'.", "Widgets")]
    [InlineData("NO SUCH TABLE: widgets", "widgets")]
    [InlineData("some unrelated error", null)]
    // Classified as a missing table by IsMissingTableException (it contains the wording) but with no
    // name to extract. This is the ONLY trigger for the substring fallback in CreatedTablesNamedIn, and
    // it is pinned so the fallback's reachability is a measured fact rather than an assumption.
    [InlineData("no such table", null)]
    public void Base_MissingTableName_reads_sqlite_wording(string message, string? expected)
    {
        var connector = new FakeConnector();

        connector.MissingTableName(new Exception(message)).Should().Be(expected);
    }

    /// <summary>
    /// The chain, not the outermost message — <c>InitException</c> rewraps as
    /// <c>new Exception(commandText, ex)</c>, so the provider's own message is never the outer one on
    /// any path that reaches the anomaly decision. A single-message version of this compiles, runs and
    /// silently never matches, which is the inert guard this codebase has already shipped once.
    /// </summary>
    [Fact]
    public void MissingTableNameChain_walks_the_rewraps()
    {
        var connector = new FakeConnector();
        var inner = new Exception("SQLite Error 1: 'no such table: Widgets'.");
        var wrapped = new Exception("SELECT count(*) as count FROM \"Widgets\"", inner);
        var twice = new Exception("outer", wrapped);

        connector.MissingTableName(twice).Should().BeNull("the outer message is the SQL, not the error");
        connector.MissingTableNameChain(twice).Should().Be("Widgets");
        connector.MissingTableNameChain(null).Should().BeNull();
    }

    /// <summary>
    /// ⚠ The name must NOT be taken from the statement text, which is what the replaced implementation
    /// effectively did. A rewrap's outer message is the SQL and mentions every table the statement
    /// touches — including ones that exist — so extracting from it would reintroduce exactly the
    /// multi-table false positive TASK-293 removed.
    /// </summary>
    [Fact]
    public void MissingTableNameChain_does_not_read_the_statement()
    {
        var connector = new FakeConnector();
        var inner = new Exception("SQLite Error 1: 'no such table: StockMovements'.");
        var wrapped = new Exception(
            "SELECT count(*) as count FROM \"Ledger\" AS Ledger, \"StockMovements\" AS StockMovements",
            inner);

        connector.MissingTableNameChain(wrapped).Should().Be("StockMovements",
            "the error names the one table that is missing; the statement names the ones that are fine "
            + "as well, and cannot discriminate");
    }

    // Minimal concrete connector to reach the base virtual (the base is abstract).
    private sealed class FakeConnector : Birko.Data.SQL.Connectors.AbstractConnectorBase
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }
}
