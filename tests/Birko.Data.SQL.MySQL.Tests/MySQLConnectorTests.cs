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
    [Theory]
    [InlineData("Table 'db.widgets' doesn't exist", true)]
    [InlineData("no such table: widgets", true)]
    [InlineData("some other error", false)]
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
}
