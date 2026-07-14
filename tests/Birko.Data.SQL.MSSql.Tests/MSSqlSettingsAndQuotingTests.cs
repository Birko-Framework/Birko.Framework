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
}
