using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// TASK-886, live RavenDB: an UNGROUPED aggregate view over no rows, and a SUM over only NULLs. Gated on
/// <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task886RavenViewAggregateEdgeLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task886RavenViewAggregateEdgeLiveTests(ITestOutputHelper output) => _output = output;

    public class T886Row : AbstractModel { public string? Grp { get; set; } public int? Score { get; set; } }

    public class WholeTotals
    {
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
        public double? AvgScore { get; set; }
    }

    public class GroupTotals
    {
        public string? Grp { get; set; }
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
    }

    private static ViewDefinition Whole() => new ViewDefinitionBuilder<WholeTotals>()
        .HasName("t886_whole").HasQueryMode(ViewQueryMode.Persistent)
        .From<T886Row>()
        .Count<T886Row>(v => v.Cnt)
        .Sum<T886Row, int?>(r => r.Score, v => v.SumScore)
        .Min<T886Row, int?>(r => r.Score, v => v.MinScore)
        .Avg<T886Row, double?>(r => r.Score, v => v.AvgScore)
        .Build();

    private static ViewDefinition Grouped() => new ViewDefinitionBuilder<GroupTotals>()
        .HasName("t886_grouped").HasQueryMode(ViewQueryMode.Persistent)
        .From<T886Row>()
        .GroupBy<T886Row, string?>(r => r.Grp)
        .Select<T886Row, string?>(r => r.Grp, v => v.Grp)
        .Count<T886Row>(v => v.Cnt)
        .Sum<T886Row, int?>(r => r.Score, v => v.SumScore)
        .Min<T886Row, int?>(r => r.Score, v => v.MinScore)
        .Build();

    private async Task Run(Func<IDocumentStore, AsyncRavenDBStore<T886Row>, Task> body)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        using var rows = new AsyncRavenDBStore<T886Row>(Url!, "birko-task886-" + Guid.NewGuid().ToString("N"));
        try
        {
            await rows.InitAsync();
            await body(rows.DocumentStore!, rows);
        }
        finally
        {
            try { await rows.DestroyAsync(); } catch { }
        }
    }

    private static async Task Ensure(IDocumentStore store, ViewDefinition definition)
    {
        await new RavenViewManager(store).EnsureAsync(definition);
        for (var i = 0; i < 100 && (await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!))).IsStale; i++)
            await Task.Delay(100);
    }

    [Fact]
    public Task An_ungrouped_view_over_no_rows_answers_one_row() => Run(async (store, rows) =>
    {
        await Ensure(store, Whole());
        var view = new RavenViewStore<WholeTotals>(store, Whole());

        (await view.QueryAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 0, SumScore = 0 });
        (await view.CountAsync()).Should().Be(1);
        (await view.QueryAsync(v => v.Cnt > 0)).Should().BeEmpty("a view filter still applies to the row");

        await rows.CreateAsync(new T886Row { Grp = "a", Score = 3 });
        await rows.CreateAsync(new T886Row { Grp = "a", Score = null });
        await Ensure(store, Whole());
        // Recorded, not fixed here: the index's MIN of {3, null} is null and its AVG 1.5 (C#: 3 and 3), measured; own task.
        var row = (await view.QueryAsync(v => v.Cnt > 0)).Should().ContainSingle().Subject;
        row.Cnt.Should().Be(2);
        row.SumScore.Should().Be(3);
        (await view.QueryFirstAsync()).Should().NotBeNull();
        (await view.QueryAsync(offset: 1)).Should().BeEmpty();
    });

    [Fact]
    public Task A_sum_over_only_nulls_is_zero() => Run(async (store, rows) =>
    {
        await rows.CreateAsync(new T886Row { Grp = "a", Score = null });
        await rows.CreateAsync(new T886Row { Grp = "a", Score = null });
        await rows.CreateAsync(new T886Row { Grp = "b", Score = 4 });
        await Ensure(store, Grouped());

        (await new RavenViewStore<GroupTotals>(store, Grouped()).QueryAsync()).OrderBy(g => g.Grp).Should().BeEquivalentTo(new[]
        {
            new GroupTotals { Grp = "a", Cnt = 2, SumScore = 0 },
            new GroupTotals { Grp = "b", Cnt = 1, SumScore = 4, MinScore = 4 },
        }, o => o.WithStrictOrdering());
    });
}
