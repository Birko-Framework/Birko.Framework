using System;
using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// CR-H091: the MySQL backend had no test project. These offline tests cover the pure-function
/// surface — ConvertType type mapping, QuoteIdentifier backtick escaping, and the connection-string
/// assembly (no live MySQL required).
/// </summary>
public class MySQLConnectorTests
{
    private sealed class Sample
    {
        public DateTime When { get; set; }
    }

    private static MySQLConnector NewConnector()
        => new(new MySqlSettings("localhost", "db", "user", "pass"));

    private static DateTimeField DateTimeField()
        => new(typeof(Sample).GetProperty(nameof(Sample.When))!, "When");

    // CR-L176: the missing-table seam recognizes MySQL's "Table 'x' doesn't exist" wording (plus the
    // inherited SQLite base match) so a reader over a missing table yields empty instead of faulting.
    //
    // TASK-211 narrowed it: the bare "doesn't exist" catch-all also matched a missing routine, and this
    // seam decides whether a reader answers an error with an empty result rather than a failure.
    [Theory]
    [InlineData("Table 'db.widgets' doesn't exist", true)]
    [InlineData("no such table: widgets", true)]
    [InlineData("some other error", false)]
    [InlineData("FUNCTION db.f doesn't exist", false)]
    public void IsMissingTableException_matches_mysql_and_base_wording(string message, bool expected)
    {
        NewConnector().IsMissingTableException(new Exception(message)).Should().Be(expected);
    }

    [Theory]
    [InlineData(DbType.DateTime, "DATETIME")]
    [InlineData(DbType.DateTime2, "DATETIME")]
    [InlineData(DbType.Date, "DATE")]
    [InlineData(DbType.Time, "TIME")]
    [InlineData(DbType.Single, "FLOAT")]
    [InlineData(DbType.Boolean, "TINYINT(1)")]
    [InlineData(DbType.Guid, "CHAR(36)")]
    [InlineData(DbType.Int32, "INT")]
    [InlineData(DbType.Int64, "BIGINT")]
    public void ConvertType_MapsTypes(DbType type, string expected)
    {
        NewConnector().ConvertType(type, DateTimeField()).Should().Be(expected);
    }

    [Fact]
    public void QuoteIdentifier_Backticks_And_EscapesBacktick()
    {
        var connector = NewConnector();
        connector.QuoteIdentifier("Widgets").Should().Be("`Widgets`");
        connector.QuoteIdentifier("weird`name").Should().Be("`weird``name`");
    }

    [Fact]
    public void GetConnectionString_ContainsServerAndCredentials()
    {
        var settings = new MySqlSettings("srv", "mydb", "u", "p", port: 3306, useSecure: true);
        var cs = settings.GetConnectionString();

        cs.Should().Contain("Server=srv");
        cs.Should().Contain("Port=3306");
        cs.Should().Contain("User ID=u");
        cs.Should().Contain("Password=p");
        cs.Should().Contain("Database=mydb");
        cs.Should().Contain("SslMode=Required");
    }

    // ------------------------------------------------------------------ TASK-245: index DDL

    private static Birko.Data.SQL.Tables.IndexDefinition Index(string name, bool unique, params string[] columns)
    {
        var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = name, Unique = unique };
        for (int i = 0; i < columns.Length; i++)
        {
            index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = columns[i], Order = i });
        }
        return index;
    }

    /// <summary>
    /// MySQL rejects <c>IF NOT EXISTS</c> on <c>CREATE INDEX</c> (measured: ERROR 1064), so the override must
    /// never emit it. This is the offline half of TASK-245 — the end-to-end proof is gated on a live server,
    /// so without this a CI run with no BIRKO_MYSQL_HOST would say nothing about the statement.
    /// </summary>
    [Fact]
    public void CreateIndexSql_never_emits_IF_NOT_EXISTS()
    {
        var sql = NewConnector().CreateIndexSql("IdxRows", Index("ux_docnum", true, "TenantGuid", "Number"));

        sql.Should().Be("CREATE UNIQUE INDEX `ux_docnum` ON `IdxRows` (TenantGuid, Number)");
        sql.Should().NotContain("IF NOT EXISTS");
    }

    /// <summary>
    /// And it stays absent when the caller asks for the non-conditional form: MySQL has no conditional
    /// spelling either way, so <c>conditional</c> changes only whether CreateIndexes tolerates the error.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateIndexSql_is_the_same_statement_conditional_or_not(bool conditional)
    {
        NewConnector().CreateIndexSql("IdxRows", Index("ix_status", false, "Status"), conditional)
            .Should().Be("CREATE INDEX `ix_status` ON `IdxRows` (Status)");
    }

    [Fact]
    public void CreateIndexSql_emits_desc_and_bare_columns()
    {
        var index = Index("ix_desc", false, "Seen");
        index.Columns[0].IsDescending = true;

        NewConnector().CreateIndexSql("Docs", index)
            .Should().Be("CREATE INDEX `ix_desc` ON `Docs` (Seen DESC)");
    }

    /// <summary>
    /// MySQL's DROP INDEX takes no <c>IF EXISTS</c> and requires <c>ON &lt;table&gt;</c> — the base emitted
    /// neither correctly, so no declared index could be dropped here either.
    /// </summary>
    [Fact]
    public void DropIndexSql_has_no_IF_EXISTS_and_names_the_table()
    {
        NewConnector().DropIndexSql("IdxRows", Index("ix_status", false, "Status"))
            .Should().Be("DROP INDEX `ix_status` ON `IdxRows`");
    }

    /// <summary>
    /// The predicate matches on the error <b>code</b>, so a message that merely says "Duplicate key name"
    /// must NOT match. That is TASK-245's "check the code, not the message" requirement made testable.
    /// </summary>
    /// <remarks>
    /// The <c>true</c> branch cannot be exercised offline: <c>MySqlConnector.MySqlException</c>'s
    /// constructors are internal, so no instance carrying 1061 can be fabricated here. It is pinned live by
    /// <c>DeclaredIndexLiveTests.A_second_schema_ensure_over_an_indexed_table_records_nothing_and_duplicates_nothing</c>
    /// (and reverting the predicate to <c>return true</c> fails 4 of that suite's 14 tests). An untestable
    /// positive branch silently claimed as covered is how a predicate ships inverted.
    /// </remarks>
    [Theory]
    [InlineData("Duplicate key name 'ix_status'")]
    [InlineData("1061")]
    [InlineData("")]
    public void IsIndexAlreadyExistsException_ignores_message_text(string message)
    {
        var connector = NewConnector();

        connector.IsIndexAlreadyExistsException(new Exception(message)).Should().BeFalse();
        connector.IsIndexAlreadyExistsException(new Exception("wrapped", new Exception(message)))
                 .Should().BeFalse("the chain is walked for a MySqlException, not for matching text");
    }
}
