using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-529 — <c>MSSqlConnector.IsUniqueViolationException</c> against a real server: a duplicate key on a UNIQUE column is
/// answered (also wrapped, as <c>InitException</c> does), a NOT NULL violation is not. The SQL event store maps this
/// answer to <c>EventStoreConcurrencyException</c>, so a wrong code here turns a version conflict into an opaque
/// error. Expected code: 2627 (constraint) / 2601 (index).
/// </summary>
public class UniqueViolationClassifierLiveTests
{
    private const string Table = "birko_uqclass_ms";
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;
    public UniqueViolationClassifierLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    [Fact]
    public async Task A_duplicate_key_is_a_unique_violation_and_a_NOT_NULL_failure_is_not()
    {
        if (!RequireServer()) return;
        var settings = Settings();
        var connector = DataBase.GetConnector<MSSqlConnector>(settings);
        await using var connection = connector.CreateConnection(settings);
        await connection.OpenAsync();

        async Task Run(string sql)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        async Task<Exception> Fail(string sql)
        {
            try { await Run(sql); } catch (Exception ex) { return ex; }
            throw new InvalidOperationException("expected a failure: " + sql);
        }

        await Run("DROP TABLE IF EXISTS " + Table + "");
        try
        {
            await Run("CREATE TABLE " + Table + " (k INT NOT NULL, u INT NULL, CONSTRAINT birko_uqclass_ms_u UNIQUE (u))");
            await Run("INSERT INTO " + Table + " (k, u) VALUES (1, 1)");

            var duplicate = await Fail("INSERT INTO " + Table + " (k, u) VALUES (2, 1)");
            _output.WriteLine(duplicate.GetType().Name + ": " + duplicate.Message);
            connector.IsUniqueViolationException(duplicate).Should().BeTrue();
            connector.IsUniqueViolationException(new Exception("wrapped", duplicate)).Should().BeTrue();

            var notNull = await Fail("INSERT INTO " + Table + " (k, u) VALUES (NULL, 3)");
            connector.IsUniqueViolationException(notNull).Should().BeFalse();
        }
        finally
        {
            await Run("DROP TABLE IF EXISTS " + Table + "");
        }
    }
}
