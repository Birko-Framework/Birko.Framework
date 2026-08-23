using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-278 — limited and paged reads, end to end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists at all:</b> when TASK-278 was found there was <i>no</i> paging coverage in any
/// suite. That is why the SQL Server defect survived — every limited read emitted invalid T-SQL there
/// (Msg 153 / Msg 102), and no test asked for one. These are the same assertions the MSSql live suite makes,
/// on the provider whose behaviour the shared base path defines.
/// </para>
/// </remarks>
public class PagedReadEndToEndTests : IDisposable
{
    private readonly string _root;
    private static int _seq;

    public PagedReadEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-paging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("SqPageRows")]
    public class PageRow : AbstractLogModel
    {
        public string? Name { get; set; }
        public int Rank { get; set; }
    }

    private async Task<AsyncSQLiteStore<PageRow>> SeededStore()
    {
        var settings = new SqLiteSettings(_root, $"paging{Interlocked.Increment(ref _seq)}.db");
        var store = new AsyncSQLiteStore<PageRow>();
        store.SetSettings(settings);
        for (int i = 1; i <= 5; i++)
        {
            await store.CreateAsync(new PageRow { Guid = Guid.NewGuid(), Name = $"row{i}", Rank = i });
        }
        return store;
    }

    [Fact]
    public async Task ReadFirstAsync_returns_one_matching_row()
    {
        var store = await SeededStore();

        var row = await store.ReadFirstAsync(x => x.Name == "row3");

        row.Should().NotBeNull();
        row!.Rank.Should().Be(3);
    }

    [Fact]
    public async Task A_limited_read_without_a_sort_returns_that_many_rows()
    {
        var store = await SeededStore();

        (await store.ReadAsync(null, null, 2, null, default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_paged_read_without_a_sort_skips_and_takes()
    {
        var store = await SeededStore();

        (await store.ReadAsync(null, null, 2, 1, default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_sorted_paged_read_is_deterministic()
    {
        var store = await SeededStore();

        var page = (await store.ReadAsync(null, OrderBy<PageRow>.By(x => x.Rank), 2, 1, default)).ToList();

        page.Select(x => x.Rank).Should().Equal(new[] { 2, 3 },
            "skip 1 of a Rank-ascending sort, then take 2");
    }

    /// <summary>
    /// The capability's false side on this provider: SQLite takes a bare <c>LIMIT</c>, so no sort may be
    /// synthesised. Only SQL Server answers true.
    /// </summary>
    [Fact]
    public void RequiresOrderByForPaging_is_false_on_sqlite()
    {
        new SqLiteConnector(new SqLiteSettings(_root, "cap.db")).RequiresOrderByForPaging.Should().BeFalse();
    }
}
