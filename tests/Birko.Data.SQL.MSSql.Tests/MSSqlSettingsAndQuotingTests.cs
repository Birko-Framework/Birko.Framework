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
