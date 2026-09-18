using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// CR-H089: pure-function coverage for the MSSql backend — identifier quoting, connection-string
/// assembly, and LoadFrom round-trip (no live SQL Server required).
/// </summary>
public class MSSqlSettingsAndQuotingTests
{
    [Fact]
    public void QuoteIdentifier_Brackets_And_EscapesClosingBracket()
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db"));

        connector.QuoteIdentifier("Widgets").Should().Be("[Widgets]");
        connector.QuoteIdentifier("weird]name").Should().Be("[weird]]name]");
    }

    // CR-L176: the missing-table seam recognizes SQL Server's "Invalid object name" wording (plus the
    // inherited SQLite base match) so a reader over a missing table yields empty instead of faulting.
    [Theory]
    [InlineData("Invalid object name 'Widgets'.", true)]
    [InlineData("no such table: Widgets", true)]
    [InlineData("some other error", false)]
    public void IsMissingTableException_matches_mssql_and_base_wording(string message, bool expected)
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db"));

        connector.IsMissingTableException(new System.Exception(message)).Should().Be(expected);
    }

    [Fact]
    public void GetConnectionString_IncludesServerCredentialsAndFlags()
    {
        var settings = new MSSqlSettings("srv", "mydb", "sa", "secret", port: 1433, useSecure: true)
        {
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
        };

        var cs = settings.GetConnectionString();

        cs.Should().Contain("Server=tcp:srv,1433");
        cs.Should().Contain("Initial Catalog=mydb");
        cs.Should().Contain("User ID=sa");
        cs.Should().Contain("Password=secret");
        cs.Should().Contain("MultipleActiveResultSets=True");
        cs.Should().Contain("Encrypt=True");
        cs.Should().Contain("TrustServerCertificate=True");
    }

    [Fact]
    public void CreateConnection_RemoteSettings_UsesMSSqlBuilderDefaults()
    {
        // CR-M136: a plain RemoteSettings (not MSSqlSettings) must build the SAME connection string as
        // the typed path — not a divergent inline form that hard-codes MARS=False and omits
        // TrustServerCertificate / Connection Timeout.
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db"));
        var remote = new Birko.Configuration.RemoteSettings("srv", "mydb", "sa", "secret", 1433, useSecure: true);

        var cs = connector.CreateConnection(remote).ConnectionString;

        cs.Should().Contain("Server=tcp:srv,1433");
        cs.Should().Contain("Initial Catalog=mydb");
        cs.Should().Contain("Encrypt=True");
        cs.Should().Contain("TrustServerCertificate=False");
        cs.Should().Contain("Connection Timeout=15");
        // Matches exactly what an equivalent MSSqlSettings would produce.
        var equivalent = new MSSqlSettings("srv", "mydb", "sa", "secret", 1433, useSecure: true).GetConnectionString();
        cs.Should().Be(equivalent);
    }

    [Fact]
    public void LoadFrom_CopiesProviderSpecificFlags()
    {
        var source = new MSSqlSettings("srv", "db", "u", "p")
        {
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
        };
        var target = new MSSqlSettings();

        target.LoadFrom(source);

        target.MultipleActiveResultSets.Should().BeTrue();
        target.TrustServerCertificate.Should().BeTrue();
        target.Location.Should().Be("srv");
        target.UserName.Should().Be("u");
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
    /// MSSql has no <c>IF NOT EXISTS</c> on <c>CREATE INDEX</c>, so it synthesises the conditional form with
    /// a <c>sys.indexes</c> guard. That is why the MySQL defect never showed on this provider.
    /// </summary>
    [Fact]
    public void CreateIndexSql_wraps_the_statement_in_a_sys_indexes_guard_by_default()
    {
        var sql = new MSSqlConnector(new MSSqlSettings("localhost", "db"))
            .CreateIndexSql("IdxRows", Index("ux_docnum", true, "TenantGuid", "Number"));

        sql.Should().StartWith("IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='ux_docnum' AND object_id=OBJECT_ID('IdxRows')) ");
        sql.Should().EndWith("CREATE UNIQUE INDEX [ux_docnum] ON [IdxRows] ([TenantGuid], [Number])");
    }

    /// <summary>
    /// …and drops the guard when the caller asks for the non-conditional form, so
    /// <c>CreateIndexes(..., throwIfExists: true)</c> genuinely raises here instead of being silently
    /// ignored — a flag honoured on one provider and no-op'd on three is the shape § Conventions ranks worst.
    /// </summary>
    [Fact]
    public void CreateIndexSql_drops_the_guard_when_not_conditional()
    {
        var sql = new MSSqlConnector(new MSSqlSettings("localhost", "db"))
            .CreateIndexSql("IdxRows", Index("ix_status", false, "Status"), conditional: false);

        sql.Should().Be("CREATE INDEX [ix_status] ON [IdxRows] ([Status])");
        sql.Should().NotContain("sys.indexes");
    }

    /// <summary>
    /// Column identifiers stay bracket-quoted on MSSql, deliberately unlike the base emitter (which emits
    /// them bare so PostgreSQL can resolve its case-folded columns). MSSql resolves either spelling under the
    /// default collation, so there is no defect to fix here and no live measurement backing a change —
    /// pinned so the divergence reads as deliberate rather than as an oversight.
    /// </summary>
    [Fact]
    public void CreateIndexSql_keeps_bracket_quoted_columns_on_mssql()
    {
        new MSSqlConnector(new MSSqlSettings("localhost", "db"))
            .CreateIndexSql("IdxRows", Index("ix_a", false, "Status"), conditional: false)
            .Should().Contain("([Status])");
    }

    /// <summary>
    /// The MySQL 1061 tolerance must never engage on MSSql: its guard means the condition never reaches the
    /// client. This asserts the "no behaviour change off MySQL" claim rather than arguing it.
    /// </summary>
    [Fact]
    public void IsIndexAlreadyExistsException_is_false_on_mssql()
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db"));

        connector.IsIndexAlreadyExistsException(new System.Exception("already exists")).Should().BeFalse();
    }
}
