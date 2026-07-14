using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.View.Tests;

/// <summary>
/// CR-L187: the MySQL view override had no test sibling. CR-L186 added an async ViewExistsAsync override
/// for parity with the sync ViewExists. The catalog query itself needs a live MySQL instance, but the
/// argument-validation guards on both overrides are unit-testable offline.
/// </summary>
public class MySQLViewExistsGuardTests
{
    private static MySQLConnector NewConnector()
        => new(new MySqlSettings("localhost", "db", "user", "pass"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ViewExists_NullOrWhitespace_ThrowsArgumentException(string? viewName)
    {
        NewConnector().Invoking(c => c.ViewExists(viewName!))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ViewExistsAsync_NullOrWhitespace_ThrowsArgumentException(string? viewName)
    {
        await NewConnector().Invoking(c => c.ViewExistsAsync(viewName!))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Async_override_is_declared_on_the_connector()
    {
        // CR-L186: assert the async override exists (declared on MySQLConnector, not just inherited),
        // so async callers get the information_schema query rather than the base SELECT/catch fallback.
        var method = typeof(MySQLConnector).GetMethod(
            nameof(MySQLConnector.ViewExistsAsync),
            new[] { typeof(string), typeof(System.Threading.CancellationToken) });

        method.Should().NotBeNull();
        method!.DeclaringType.Should().Be(typeof(MySQLConnector));
    }
}
