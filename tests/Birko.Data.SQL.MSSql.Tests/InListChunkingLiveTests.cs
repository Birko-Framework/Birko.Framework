using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-833 (FIELD-051, Symbio), on SQL Server, the provider with the lowest parameter limit. A list of 5,000 ids is past
/// it, so before the task every read, count, delete and update with it failed in the driver; now the connector splits it
/// into chunks of 1,000. The ceiling the connector declares (2098) is measured here, not assumed (rule 52).
/// </summary>
/// <remarks>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class InListChunkingLiveTests : IDisposable
{
    private const string TableName = "MsInChunkRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public InListChunkingLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    [Table(TableName)]
    public class ChunkRow : AbstractDatabaseLogModel
    {
        public int Rank { get; set; }
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{TableName}]"); } catch { }
    }

    /// <summary>5,200 rows; the ids of the first 5,000, with the first 100 repeated.</summary>
    private static async Task<(AsyncMSSqlStore<ChunkRow> Store, List<Guid> Ids)> SeedAsync()
    {
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        var store = new AsyncMSSqlStore<ChunkRow>();
        store.SetSettings(Settings());
        var rows = Enumerable.Range(0, 5200).Select(i => new ChunkRow { Guid = Guid.NewGuid(), Rank = i }).ToList();
        await store.CreateAsync(rows, ct: CancellationToken.None);
        var ids = rows.Take(5000).Select(r => r.Guid!.Value).ToList();
        ids.AddRange(ids.Take(100));
        return (store, ids);
    }

    [Fact]
    public async Task A_list_of_5000_ids_reads_counts_updates_and_deletes_exactly_its_rows()
    {
        if (!RequireServer()) return;
        var (store, ids) = await SeedAsync();

        var read = (await store.ReadAsync(x => ids.Contains(x.Guid!.Value) && x.Rank >= 0, ct: CancellationToken.None)).ToList();
        read.Should().HaveCount(5000).And.OnlyHaveUniqueItems(r => r.Guid);

        (await store.CountAsync(x => ids.Contains(x.Guid!.Value), CancellationToken.None)).Should().Be(5000);

        await store.UpdateAsync(x => ids.Contains(x.Guid!.Value), new PropertyUpdate<ChunkRow>().Increment(x => x.Rank, 100000), CancellationToken.None);
        Scalar($"SELECT COUNT(*) FROM [{TableName}] WHERE Rank >= 100000").Should().Be(5000);
        Scalar($"SELECT COUNT(*) FROM [{TableName}] WHERE Rank >= 200000").Should().Be(0, "no row is in two chunks");

        await store.DeleteAsync(x => ids.Contains(x.Guid!.Value), CancellationToken.None);
        Scalar($"SELECT COUNT(*) FROM [{TableName}]").Should().Be(200);
    }

    [Fact]
    public async Task A_not_in_past_the_limit_is_refused_and_deletes_nothing()
    {
        if (!RequireServer()) return;
        var (store, ids) = await SeedAsync();

        var act = async () => await store.DeleteAsync(x => !ids.Contains(x.Guid!.Value), CancellationToken.None);

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("2098");
        Scalar($"SELECT COUNT(*) FROM [{TableName}]").Should().Be(5200);
    }

    /// <summary>
    /// Rule 52: the declared ceiling is a measurement. A parameterised SqlCommand is sent as <c>sp_executesql</c>, which
    /// takes two parameters of the request's 2100 for the statement and the parameter list.
    /// </summary>
    [Theory]
    [InlineData(2098, true)]
    [InlineData(2099, false)]
    public void SQL_Server_binds_2098_parameters_and_refuses_2099(int parameters, bool binds)
    {
        if (!RequireServer()) return;
        new MSSqlConnector(Settings()).MaxCommandParameters.Should().Be(2098);

        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM (VALUES (1)) AS t(x) WHERE x IN ("
            + string.Join(", ", Enumerable.Range(0, parameters).Select(i => "@p" + i)) + ")";
        for (var i = 0; i < parameters; i++)
        {
            command.Parameters.Add(new SqlParameter("@p" + i, i));
        }

        var act = () => command.ExecuteScalar();

        if (binds)
        {
            Convert.ToInt64(act()).Should().Be(1);
            return;
        }
        var error = act.Should().Throw<SqlException>().Which;
        _output.WriteLine($"{parameters} parameters: Msg {error.Number}: {error.Message}");
    }
}
