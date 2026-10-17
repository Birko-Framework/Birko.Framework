using System;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-653 (SH-M280), live: a user name, password and database name containing <c>;</c>, <c>=</c>, <c>'</c> and
/// <c>"</c> reach MySQL unchanged, through <see cref="MySqlSettings"/> and through the connector's plain
/// <see cref="RemoteSettings"/> path, and a keyword inside a credential is a value. Gated on <c>BIRKO_MYSQL_HOST</c>.
/// </summary>
public class Task653ConnectionStringEscapingLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task653ConnectionStringEscapingLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static void Admin(string sql)
    {
        using var connection = new MySqlConnection(new MySqlSettings(Host!, Database, User, Password, Port).GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Ident(string name) => "`" + name.Replace("`", "``") + "`";
    private static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";

    private static (string user, string db) WhoAmI(System.Data.Common.DbConnection connection)
    {
        using (connection)
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SUBSTRING_INDEX(CURRENT_USER(), '@', 1), DATABASE()";
            using var reader = command.ExecuteReader();
            reader.Read();
            var result = (reader.GetString(0), reader.GetString(1));
            MySqlConnection.ClearPool((MySqlConnection)connection);
            return result;
        }
    }

    [Fact]
    public void Credentials_with_separators_and_quotes_round_trip_on_both_paths()
    {
        if (!Live()) return;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = $"t653u;=x'\"{suffix}";
        var password = $"p;=x'\"SslMode=None;{suffix}";
        var db = $"t653d;=x'\"{suffix}";
        Admin($"CREATE USER {Literal(user)}@'%' IDENTIFIED BY {Literal(password)}");
        try
        {
            Admin($"CREATE DATABASE {Ident(db)}");
            try
            {
                Admin($"GRANT ALL ON {Ident(db)}.* TO {Literal(user)}@'%'");
                var settings = new MySqlSettings(Host!, db, user, password, Port);
                var remote = new RemoteSettings(Host!, db, user, password, Port);

                WhoAmI(new MySqlConnection(settings.GetConnectionString())).Should().Be((user, db));
                WhoAmI(new MySQLConnector(remote).CreateConnection(remote)).Should().Be((user, db), "the RemoteSettings fallback");
            }
            finally
            {
                Admin($"DROP DATABASE {Ident(db)}");
            }
        }
        finally
        {
            Admin($"DROP USER {Literal(user)}@'%'");
        }
    }

    [Fact]
    public void A_keyword_inside_a_credential_is_a_value()
    {
        var settings = new MySqlSettings("db.example", "app", "u;SslMode=None", "x;SslMode=None;Server=evil", 3306, useSecure: true);
        var remote = new RemoteSettings("db.example", "app", "u;SslMode=None", "x;SslMode=None;Server=evil", 3306) { UseSecure = true };

        foreach (var composed in new[] { settings.GetConnectionString(), new MySQLConnector(remote).CreateConnection(remote).ConnectionString })
        {
            var read = new MySqlConnectionStringBuilder(composed);
            read.SslMode.Should().Be(MySqlSslMode.Required);
            read.Server.Should().Be("db.example");
            read.Password.Should().Be("x;SslMode=None;Server=evil");
            read.UserID.Should().Be("u;SslMode=None");
        }
    }

    [Fact]
    public void Every_key_the_interpolated_string_set_survives()
    {
        var settings = new MySqlSettings("db.example", "app", "u", "p", 3307) { ConnectionTimeout = 42 };

        var read = new MySqlConnectionStringBuilder(settings.GetConnectionString());

        read.Server.Should().Be("db.example");
        read.Port.Should().Be(3307u);
        read.Database.Should().Be("app");
        read.ConnectionTimeout.Should().Be(42u);
        read.SslMode.Should().Be(MySqlSslMode.Preferred, "no SslMode key without UseSecure, as before (the driver default)");
    }
}
