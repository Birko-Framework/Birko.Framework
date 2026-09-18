using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-257 — a predicate over an <b>unlengthed</b> string column against a real SQL Server, asserted on the
/// rows it returns rather than on the absence of an exception.
///
/// <para>
/// <b>The defect this pins.</b> Filed by consumer Symbio (TASK-472) after
/// <c>DeleteWhereAsync(x =&gt; x.Label == "a")</c> failed on MSSql 16.00.4265 with
/// <i>"The data types text and nvarchar are incompatible in the equal to operator"</i>. The cause was column
/// typing, not the boundary it was verifying: <c>ConvertType</c> mapped every string that is not a
/// <c>CharField</c> to the deprecated <c>TEXT</c>, and a parameter binds as <c>nvarchar</c>.
/// </para>
/// <para>
/// <b>Measured on 2022 (16.0.4265.3) before the fix</b>, against a <c>TEXT</c> column: <c>=</c>, <c>&lt;&gt;</c>
/// and <c>IN</c> raise Msg <b>402</b>; <c>LOWER(col)</c> raises <b>8116</b>; <c>ORDER BY</c> and
/// <c>GROUP BY</c> raise <b>306</b>; <c>DISTINCT</c> raises <b>421</b>. <c>LIKE</c> and <c>IS NULL</c> are
/// <b>legal</b> — so the <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c> tests below are contract
/// <i>pins</i> that passed before the fix, not provers of it. The provers are equality, <c>IN</c>,
/// <c>ToLower()</c> and the two sorts. A revert that also takes the LIKE tests down means the harness changed,
/// not the fix.
/// </para>
/// <para>
/// The probe entity carries <b>no length attribute and no ModelMap</b> — the shape consumers actually write,
/// and the one both pre-existing live fixtures had accidentally avoided.
/// </para>
/// <para>
/// Gated on <c>BIRKO_MSSQL_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>); set
/// <c>BIRKO_REQUIRE_LIVE</c> to make a skip a failure.
/// </para>
/// </summary>
public class StringPredicateLiveTests : IDisposable
{
    private const string TableName = "MsStrRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public StringPredicateLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <remarks>
    /// <c>AbstractDatabaseLogModel</c>, not <c>AbstractLogModel</c>: the latter leaves
    /// <c>CreatedAt</c>/<c>UpdatedAt</c> at 0001-01-01, below SQL Server's 1753 floor.
    /// <para>
    /// <b>No <c>[MaxLengthField]</c> anywhere, deliberately.</b> That is the whole point of this fixture.
    /// </para>
    /// </remarks>
    [Table(TableName)]
    public class MsStrRow : AbstractDatabaseLogModel
    {
        public string Label { get; set; } = null!;
        public string? Note { get; set; }
        public int Amount { get; set; }
    }

    private static AsyncMSSqlStore<MsStrRow> Store()
    {
        var store = new AsyncMSSqlStore<MsStrRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Counted on a connection of this test's own, never through the store under test.</summary>
    private static int RowCount()
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{TableName}]";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>The declared column type as SQL Server itself reports it.</summary>
    private static string ColumnType(string column)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT t.name, c.max_length
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c";
        cmd.Parameters.AddWithValue("@t", TableName);
        cmd.Parameters.AddWithValue("@c", column);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return "(missing)";
        return $"{reader.GetString(0)}({reader.GetInt16(1)})";
    }

    private async Task<AsyncMSSqlStore<MsStrRow>> SeededAsync()
    {
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        var store = Store();
        await store.CreateAsync(new[]
        {
            new MsStrRow { Guid = Guid.NewGuid(), Label = "alpha", Note = "first",  Amount = 1 },
            new MsStrRow { Guid = Guid.NewGuid(), Label = "beta",  Note = null,     Amount = 2 },
            new MsStrRow { Guid = Guid.NewGuid(), Label = "gamma", Note = "third",  Amount = 3 },
            new MsStrRow { Guid = Guid.NewGuid(), Label = "delta", Note = "fourth", Amount = 4 },
        }, null, CancellationToken.None);
        return store;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{TableName}]"); } catch { }
    }

    // ---------------------------------------------------------------- the column itself

    [Fact]
    public async Task An_unlengthed_string_column_is_nvarchar_max_on_the_server()
    {
        if (!RequireServer()) return;
        await SeededAsync();

        // max_length -1 is how SQL Server reports a MAX type.
        ColumnType(nameof(MsStrRow.Label)).Should().Be("nvarchar(-1)",
            "the declared type is what every failure below came from; assert it against the catalogue "
          + "rather than trusting ConvertType's return value");
    }

    // ---------------------------------------------------------------- the provers

    [Fact]
    public async Task Equality_returns_the_right_row()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var rows = (await store.ReadAsync(x => x.Label == "beta", null, null, null, CancellationToken.None)).ToList();

        rows.Should().HaveCount(1, "Msg 402 before the fix — the consumer's original error");
        rows[0].Label.Should().Be("beta");
        rows[0].Amount.Should().Be(2);
    }

    [Fact]
    public async Task Inequality_returns_the_complement()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var rows = (await store.ReadAsync(x => x.Label != "beta", null, null, null, CancellationToken.None)).ToList();

        rows.Select(r => r.Label).Should().BeEquivalentTo(new[] { "alpha", "gamma", "delta" });
    }

    [Fact]
    public async Task Collection_contains_becomes_IN_and_returns_both_rows()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();
        var wanted = new[] { "alpha", "gamma" };

        var rows = (await store.ReadAsync(x => wanted.Contains(x.Label), null, null, null, CancellationToken.None)).ToList();

        // The canonical batch-fetch shape, and it is not on the task's own criteria list — it fails with the
        // same Msg 402, because a collection Contains renders IN rather than LIKE.
        rows.Select(r => r.Label).Should().BeEquivalentTo(wanted);
    }

    [Fact]
    public async Task ToLower_returns_the_right_row()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var rows = (await store.ReadAsync(x => x.Label.ToLower() == "beta", null, null, null, CancellationToken.None)).ToList();

        // Rendered as LOWER(col) = @p; on TEXT that is Msg 8116, "Argument data type text is invalid for
        // argument 1 of lower function".
        rows.Should().HaveCount(1);
        rows[0].Label.Should().Be("beta");
    }

    [Fact]
    public async Task SortBy_returns_the_right_sequence()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var ascending = (await store.ReadAsync(null, OrderBy<MsStrRow>.By(x => x.Label), null, null, CancellationToken.None))
            .Select(r => r.Label).ToList();
        var descending = (await store.ReadAsync(null, OrderBy<MsStrRow>.ByDescending(x => x.Label), null, null, CancellationToken.None))
            .Select(r => r.Label).ToList();

        // Msg 306 before the fix. Asserted as a SEQUENCE — a set comparison would pass on an unsorted result.
        ascending.Should().Equal(new[] { "alpha", "beta", "delta", "gamma" });
        descending.Should().Equal(new[] { "gamma", "delta", "beta", "alpha" });
    }

    [Fact]
    public async Task Delete_by_string_predicate_removes_only_the_matching_row()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        // The consumer's original failing call.
        await store.DeleteAsync(x => x.Label == "beta", CancellationToken.None);

        RowCount().Should().Be(3, "counted on a separate connection, not through the store under test");
        var survivors = (await store.ReadAsync(CancellationToken.None)).Select(r => r.Label).ToList();
        survivors.Should().BeEquivalentTo(new[] { "alpha", "gamma", "delta" });
    }

    // ---------------------------------------------------------------- the pins (green before the fix)

    [Fact]
    public async Task Contains_startswith_endswith_return_the_right_rows()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var contains = (await store.ReadAsync(x => x.Label.Contains("elt"), null, null, null, CancellationToken.None)).ToList();
        var starts = (await store.ReadAsync(x => x.Label.StartsWith("ga"), null, null, null, CancellationToken.None)).ToList();
        var ends = (await store.ReadAsync(x => x.Label.EndsWith("ta"), null, null, null, CancellationToken.None)).ToList();

        // A string Contains renders LIKE, which is legal on TEXT — measured. These are contract pins: they
        // passed before the fix and must keep passing after it.
        contains.Select(r => r.Label).Should().BeEquivalentTo(new[] { "delta" });
        starts.Select(r => r.Label).Should().BeEquivalentTo(new[] { "gamma" });
        ends.Select(r => r.Label).Should().BeEquivalentTo(new[] { "beta", "delta" });
    }

    [Fact]
    public async Task A_nullable_string_column_handles_null_and_equality()
    {
        if (!RequireServer()) return;
        var store = await SeededAsync();

        var nulls = (await store.ReadAsync(x => x.Note == null, null, null, null, CancellationToken.None)).ToList();
        var third = (await store.ReadAsync(x => x.Note == "third", null, null, null, CancellationToken.None)).ToList();

        // IS NULL was legal on TEXT; the equality beside it was not. Both must hold now.
        nulls.Select(r => r.Label).Should().BeEquivalentTo(new[] { "beta" });
        third.Select(r => r.Label).Should().BeEquivalentTo(new[] { "gamma" });
    }
}
