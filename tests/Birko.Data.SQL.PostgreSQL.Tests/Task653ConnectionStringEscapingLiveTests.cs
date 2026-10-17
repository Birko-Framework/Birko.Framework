using System;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-653 (SH-M279), live: a user name, password and database name containing <c>;</c>, <c>=</c>, <c>'</c> and
/// <c>"</c> reach the server unchanged, through <see cref="PostgreSqlSettings"/> and through the connector's plain
/// <see cref="RemoteSettings"/> path, and a keyword inside a credential is a value, not a keyword. Gated on
/// <c>BIRKO_PG_HOST</c>.
/// </summary>
public class Task653ConnectionStringEscapingLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task653ConnectionStringEscapingLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static void Admin(string sql)
    {
        using var connection = new NpgsqlConnection(new PostgreSqlSettings(Host!, Database, User, Password) { Port = Port }.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Ident(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    private static (string user, string db) WhoAmI(System.Data.Common.DbConnection connection)
    {
        using (connection)
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user, current_database()";
            using var reader = command.ExecuteReader();
            reader.Read();
            var result = (reader.GetString(0), reader.GetString(1));
            NpgsqlConnection.ClearPool((NpgsqlConnection)connection);
            return result;
        }
    }

    [Fact]
    public void Credentials_with_separators_and_quotes_round_trip_on_both_paths()
    {
        if (!Live()) return;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = $"t653u;=x'\"{suffix}";
        var password = $"p;=x'\"Encrypt=False;{suffix}";
        var db = $"t653d;=x'\"{suffix}";
        Admin($"CREATE ROLE {Ident(user)} LOGIN PASSWORD {Literal(password)}");
        try
        {
            Admin($"CREATE DATABASE {Ident(db)} OWNER {Ident(user)}");
            try
            {
                var settings = new PostgreSqlSettings(Host!, db, user, password) { Port = Port };
                var remote = new RemoteSettings(Host!, db, user, password, Port);

                WhoAmI(new NpgsqlConnection(settings.GetConnectionString())).Should().Be((user, db));
                WhoAmI(new PostgreSQLConnector(remote).CreateConnection(remote)).Should().Be((user, db), "the RemoteSettings fallback");
            }
            finally
            {
                Admin($"DROP DATABASE {Ident(db)} WITH (FORCE)");
            }
        }
        finally
        {
            Admin($"DROP ROLE {Ident(user)}");
        }
    }

    [Fact]
    public void A_keyword_inside_a_credential_is_a_value()
    {
        var settings = new PostgreSqlSettings("db.example", "app", "u;SSL Mode=Disable", "x;SSL Mode=Disable;Host=evil") { UseSecure = true };
        var remote = new RemoteSettings("db.example", "app", "u;SSL Mode=Disable", "x;SSL Mode=Disable;Host=evil", 5432) { UseSecure = true };

        foreach (var composed in new[] { settings.GetConnectionString(), new PostgreSQLConnector(remote).CreateConnection(remote).ConnectionString })
        {
            var read = new NpgsqlConnectionStringBuilder(composed);
            read.SslMode.Should().Be(SslMode.Require);
            read.Host.Should().Be("db.example");
            read.Password.Should().Be("x;SSL Mode=Disable;Host=evil");
            read.Username.Should().Be("u;SSL Mode=Disable");
        }
    }

    [Fact]
    public void Every_key_the_interpolated_string_set_survives()
    {
        var settings = new PostgreSqlSettings("db.example", "app", "u", "p") { Port = 5440, ConnectionTimeout = 42, CommandTimeout = 99 };

        var read = new NpgsqlConnectionStringBuilder(settings.GetConnectionString());

        read.Host.Should().Be("db.example");
        read.Port.Should().Be(5440);
        read.Database.Should().Be("app");
        read.Timeout.Should().Be(42);
        read.CommandTimeout.Should().Be(99);
        read.SslMode.Should().Be(SslMode.Prefer, "no SSL Mode key without UseSecure, as before (the driver default)");
    }
}
