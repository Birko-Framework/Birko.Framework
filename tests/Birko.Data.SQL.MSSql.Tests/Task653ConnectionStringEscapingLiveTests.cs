using System;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-653 (SH-M280), live: a login, password and database name containing <c>;</c>, <c>=</c>, <c>'</c> and
/// <c>"</c> reach SQL Server unchanged, through <see cref="MSSqlSettings"/> and through the connector's plain
/// <see cref="RemoteSettings"/> path, and a keyword inside a credential is a value. Gated on <c>BIRKO_MSSQL_HOST</c>.
/// </summary>
public class Task653ConnectionStringEscapingLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task653ConnectionStringEscapingLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static void Admin(string sql, string? database = null)
    {
        using var connection = new SqlConnection(new MSSqlSettings(Host!, database ?? Database, User, Password, Port) { TrustServerCertificate = true }.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Ident(string name) => "[" + name.Replace("]", "]]") + "]";
    private static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";

    private static (string user, string db) WhoAmI(System.Data.Common.DbConnection connection)
    {
        using (connection)
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SUSER_SNAME(), DB_NAME()";
            using var reader = command.ExecuteReader();
            reader.Read();
            var result = (reader.GetString(0), reader.GetString(1));
            SqlConnection.ClearPool((SqlConnection)connection);
            return result;
        }
    }

    [Fact]
    public void Credentials_with_separators_and_quotes_round_trip_on_both_paths()
    {
        if (!Live()) return;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = $"t653u;=x'\"{suffix}";
        var password = $"P;=x'\"Encrypt=True;9aA{suffix}";
        var db = $"t653d;=x'\"{suffix}";
        Admin($"CREATE LOGIN {Ident(user)} WITH PASSWORD = {Literal(password)}, CHECK_POLICY = OFF");
        try
        {
            Admin($"CREATE DATABASE {Ident(db)}");
            try
            {
                Admin($"CREATE USER {Ident(user)} FOR LOGIN {Ident(user)}", db);
                // The local container's certificate is self-signed: the typed path trusts it, the RemoteSettings path
                // (which has no TrustServerCertificate) connects unencrypted.
                var settings = new MSSqlSettings(Host!, db, user, password, Port) { TrustServerCertificate = true };
                var remote = new RemoteSettings(Host!, db, user, password, Port) { UseSecure = false };

                WhoAmI(new SqlConnection(settings.GetConnectionString())).Should().Be((user, db));
                WhoAmI(new MSSqlConnector(remote).CreateConnection(remote)).Should().Be((user, db), "the RemoteSettings fallback");
            }
            finally
            {
                Admin($"ALTER DATABASE {Ident(db)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Ident(db)}");
            }
        }
        finally
        {
            Admin($"DROP LOGIN {Ident(user)}");
        }
    }

    [Fact]
    public void A_keyword_inside_a_credential_is_a_value()
    {
        var settings = new MSSqlSettings("db.example", "app", "u;Encrypt=False", "x;Encrypt=False;TrustServerCertificate=True;Server=evil", 1433, useSecure: true);
        var remote = new RemoteSettings("db.example", "app", "u;Encrypt=False", "x;Encrypt=False;TrustServerCertificate=True;Server=evil", 1433) { UseSecure = true };

        foreach (var composed in new[] { settings.GetConnectionString(), new MSSqlConnector(remote).CreateConnection(remote).ConnectionString })
        {
            var read = new SqlConnectionStringBuilder(composed);
            read.Encrypt.Should().Be(SqlConnectionEncryptOption.Mandatory);
            read.TrustServerCertificate.Should().BeFalse();
            read.DataSource.Should().Be("tcp:db.example,1433");
            read.Password.Should().Be("x;Encrypt=False;TrustServerCertificate=True;Server=evil");
            read.UserID.Should().Be("u;Encrypt=False");
        }
    }

    [Fact]
    public void Every_key_the_interpolated_string_set_survives()
    {
        var settings = new MSSqlSettings("db.example", "app", "u", "p", 1444, useSecure: false)
        {
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
            ConnectionTimeout = 42,
        };

        var read = new SqlConnectionStringBuilder(settings.GetConnectionString());

        read.DataSource.Should().Be("tcp:db.example,1444");
        read.InitialCatalog.Should().Be("app");
        read.PersistSecurityInfo.Should().BeFalse();
        read.MultipleActiveResultSets.Should().BeTrue();
        read.Encrypt.Should().Be(SqlConnectionEncryptOption.Optional);
        read.TrustServerCertificate.Should().BeTrue();
        read.ConnectTimeout.Should().Be(42);
        settings.GetId().Should().Be(new MSSqlSettings("db.example", "app", "u", "other", 1444).GetId(), "the connector cache key is unchanged");
    }
}
