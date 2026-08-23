using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-278 — limited and paged reads on SQL Server, where the framework used to emit invalid T-SQL.
/// </summary>
/// <remarks>
/// <para>
/// <c>LimitOffsetDefinition</c> emitted <c>FETCH NEXT n ROWS ONLY</c> and prepended <c>OFFSET</c> only when
/// the caller supplied one. Measured on 16.0.4265.3: <c>FETCH</c> alone is <c>Msg 153</c>, and
/// <c>OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY</c> without a sort is <c>Msg 102</c>. So <b>every</b> limited
/// read failed here — including <c>ReadFirstAsync</c>, which
/// <c>Birko.Data.SQL/CLAUDE.md</c> § Conventions tells consumers to use for a single row.
/// </para>
/// <para>
/// Two changes fixed it: the offset now defaults to 0 (T-SQL has no standalone <c>FETCH</c>), and
/// <c>RequiresOrderByForPaging</c> makes <c>CreateSelectCommand</c> synthesise
/// <c>ORDER BY (SELECT NULL)</c> when the caller passed no sort. The rows are then arbitrary — which they
/// already are on the other three providers for a limited read with no sort; SQL Server just refuses to
/// pretend otherwise.
/// </para>
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class LimitOffsetLiveTests : IDisposable
{
    private const string TableName = "MsPageRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public LimitOffsetLiveTests(ITestOutputHelper output) => _output = output;

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
    public class PageRow : AbstractDatabaseLogModel
    {
        [MaxLengthField(64)]
        public string? Name { get; set; }

        public int Rank { get; set; }
    }

    private static AsyncMSSqlStore<PageRow> NewStore()
    {
        var store = new AsyncMSSqlStore<PageRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task<AsyncMSSqlStore<PageRow>> SeededStore()
    {
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        var store = NewStore();
        for (int i = 1; i <= 5; i++)
        {
            await store.CreateAsync(new PageRow { Guid = Guid.NewGuid(), Name = $"row{i}", Rank = i });
        }
        return store;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{TableName}]"); } catch { }
    }

    /// <summary>
    /// The headline case: the single-row read § Conventions recommends. It could not work at all here —
    /// <c>Msg 153</c> — because it emits a limit with no offset.
    /// </summary>
    [Fact]
    public async Task ReadFirstAsync_works_on_sql_server()
    {
        if (!RequireServer()) return;
        var store = await SeededStore();

        var row = await store.ReadFirstAsync(x => x.Name == "row3");

        row.Should().NotBeNull();
        row!.Name.Should().Be("row3");
        row.Rank.Should().Be(3);
    }

    /// <summary>A limit with no offset and no caller-supplied sort — the shape that raised Msg 153.</summary>
    [Fact]
    public async Task A_limited_read_without_a_sort_returns_that_many_rows()
    {
        if (!RequireServer()) return;
        var store = await SeededStore();

        var rows = await store.ReadAsync(null, null, 2, null, default);

        rows.Should().HaveCount(2, "the limit is honoured; WHICH two is arbitrary without a sort, exactly "
                                + "as it is on the other three providers");
    }

    /// <summary>
    /// A paged read with an offset and no sort — the shape that raised Msg 102 even once the offset was
    /// present, because T-SQL requires the ORDER BY.
    /// </summary>
    [Fact]
    public async Task A_paged_read_without_a_sort_skips_and_takes()
    {
        if (!RequireServer()) return;
        var store = await SeededStore();

        var rows = await store.ReadAsync(null, null, 2, 1, default);

        rows.Should().HaveCount(2);
    }

    /// <summary>
    /// With a caller-supplied sort the page is deterministic, and the synthesised placeholder must NOT be
    /// emitted alongside it — two ORDER BY clauses would be a syntax error.
    /// </summary>
    [Fact]
    public async Task A_sorted_paged_read_is_deterministic()
    {
        if (!RequireServer()) return;
        var store = await SeededStore();

        var page = (await store.ReadAsync(null, OrderBy<PageRow>.By(x => x.Rank), 2, 1, default)).ToList();

        page.Should().HaveCount(2);
        page.Select(x => x.Rank).Should().Equal(new[] { 2, 3 }, "skip 1 of a Rank-ascending sort, then take 2");
    }

    /// <summary>
    /// The capability's own two sides, on the provider that answers true. The other three assert false in
    /// their own suites — without both, the flag is indistinguishable from an unconditional synthesised
    /// sort.
    /// </summary>
    [Fact]
    public void RequiresOrderByForPaging_is_true_on_sql_server()
    {
        new MSSqlConnector(new MSSqlSettings("localhost", "db", "sa", "p")).RequiresOrderByForPaging
            .Should().BeTrue("OFFSET/FETCH is part of the ORDER BY clause in T-SQL");
    }

    /// <summary>
    /// The emitted tail, offline: the <c>OFFSET</c> is present even when the caller gave none, because
    /// T-SQL has no standalone <c>FETCH</c>.
    /// </summary>
    [Fact]
    public void The_emitted_tail_always_carries_an_offset()
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db", "sa", "p"));
        using var command = new SqlCommand();

        connector.LimitOffsetDefinition(command, 1, null)
            .Should().Be(" OFFSET @OFFSET ROWS FETCH NEXT @LIMIT ROWS ONLY");
        command.Parameters["@OFFSET"].Value.Should().Be(0, "defaulted, not omitted — omitting it is Msg 153");
        command.Parameters["@LIMIT"].Value.Should().Be(1);
    }

    [Fact]
    public void The_emitted_tail_is_empty_without_a_limit()
    {
        var connector = new MSSqlConnector(new MSSqlSettings("localhost", "db", "sa", "p"));
        using var command = new SqlCommand();

        connector.LimitOffsetDefinition(command, null, 5).Should().BeEmpty(
            "CreateSelectCommand only calls this when a limit is set, and an OFFSET with no FETCH is Msg 102 "
          + "anyway");
    }
}
