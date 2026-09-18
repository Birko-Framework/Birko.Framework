using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.TimescaleDB.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// CR-H109: TimescaleDBSettings.GetConnectionString() (with Command/Connection timeouts + SSL) was
/// never invoked — the inherited PostgreSQLConnector.CreateConnection only honored a PostgreSqlSettings
/// (a sibling type), so a TimescaleDBSettings fell through to a bare connection string that dropped
/// the timeouts. TimescaleDBConnector now overrides CreateConnection to use GetConnectionString().
/// </summary>
public class TimescaleDBConnectorConnectionStringTests
{
    [Fact]
    public void CreateConnection_HonorsGetConnectionString_Timeouts()
    {
        var settings = new TimescaleDBSettings("localhost", "metrics", "user", "pass", 5432)
        {
            ConnectionTimeout = 77,
            CommandTimeout = 1234,
        };
        var connector = new TimescaleDBConnector(settings);

        using var conn = (NpgsqlConnection)connector.CreateConnection(settings);
        var builder = new NpgsqlConnectionStringBuilder(conn.ConnectionString);

        builder.Timeout.Should().Be(77, "ConnectionTimeout must reach the real connection");
        builder.CommandTimeout.Should().Be(1234, "CommandTimeout must reach the real connection");
        builder.Host.Should().Be("localhost");
        builder.Database.Should().Be("metrics");
    }

    [Fact]
    public void CreateConnection_AppliesSslWhenSecure()
    {
        var settings = new TimescaleDBSettings("localhost", "metrics", "user", "pass", 5432)
        {
            UseSecure = true,
        };
        var connector = new TimescaleDBConnector(settings);

        using var conn = (NpgsqlConnection)connector.CreateConnection(settings);
        var builder = new NpgsqlConnectionStringBuilder(conn.ConnectionString);

        builder.SslMode.Should().Be(SslMode.Require);
    }

    [Fact]
    public void Constructor_FromRemoteSettings_RoutesSettingsThroughTimescaleBuilder()
    {
        // CR-M176: the RemoteSettings ctor now chains to the typed ctor, so base _settings (exposed via
        // Settings, used by CreateConnection / bulk ops) is a TimescaleDBSettings — not the raw
        // RemoteSettings that never reached TimescaleDBSettings.GetConnectionString().
        var remote = new Birko.Configuration.RemoteSettings("localhost", "metrics", "user", "pass", 5432, useSecure: true);
        var connector = new TimescaleDBConnector(remote);

        connector.Settings.Should().BeOfType<TimescaleDBSettings>();

        using var conn = (NpgsqlConnection)connector.CreateConnection(connector.Settings);
        var builder = new NpgsqlConnectionStringBuilder(conn.ConnectionString);
        builder.Host.Should().Be("localhost");
        builder.Database.Should().Be("metrics");
        builder.SslMode.Should().Be(SslMode.Require);
    }
}
