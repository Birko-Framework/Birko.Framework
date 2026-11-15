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
    public void The_probe_is_the_MySQL_dialects_catalogue_query_for_sync_and_async_alike()
    {
        // CR-L186 asserted an async override on MySQLConnector, so async callers got the information_schema query
        // rather than the base fallback. TASK-917 moved the query to the connector's dialect, which both forms run
        // (one producer) and which needs no View package; the override was removed. Pin what the dialect asks.
        var dialect = (Birko.Data.SQL.Conditions.SqlValueFragmentDialect)typeof(MySQLConnector)
            .GetProperty("FragmentDialect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(NewConnector())!;

        dialect.ViewExistsSql.Should().Be(
            "SELECT 1 FROM information_schema.VIEWS WHERE TABLE_NAME = @viewName AND TABLE_SCHEMA = DATABASE()");
        typeof(MySQLConnector).GetMethod(nameof(MySQLConnector.ViewExistsAsync),
                new[] { typeof(string), typeof(System.Threading.CancellationToken) })!
            .DeclaringType.Should().NotBe(typeof(MySQLConnector), "the View package no longer keeps its own copy");
    }
}
