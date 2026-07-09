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
        var settings = new PostgreSqlSettings("srv", "mydb", "u", "p", port: 5432, useSecure: true);
        var cs = settings.GetConnectionString();

        cs.Should().Contain("Host=srv");
        cs.Should().Contain("Port=5432");
        cs.Should().Contain("Username=u");
        cs.Should().Contain("Password=p");
        cs.Should().Contain("Database=mydb");
        cs.Should().Contain("SSL Mode=Require");
    }
}
