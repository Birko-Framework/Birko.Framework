using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-886, live MongoDB: an UNGROUPED aggregate view over no rows answers one row (Count 0, Sum 0, Min / Avg null), as
/// the store aggregate and every other provider do (the user's decision, 2026-11-10); a SUM over only NULLs is 0.
/// Measured before: MongoDB's <c>$group</c> answers no document over no input, so the view had no row, on the fly and
/// as a persistent view. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task886MongoViewAggregateEdgeLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task886MongoViewAggregateEdgeLiveTests(ITestOutputHelper output) => _output = output;

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

    private static ViewDefinition Whole(ViewQueryMode mode) => new ViewDefinitionBuilder<WholeTotals>()
        .HasName("t886_whole").HasQueryMode(mode)
        .From<T886Row>()
        .Count<T886Row>(v => v.Cnt)
        .Sum<T886Row, int?>(r => r.Score, v => v.SumScore)
        .Min<T886Row, int?>(r => r.Score, v => v.MinScore)
        .Avg<T886Row, double?>(r => r.Score, v => v.AvgScore)
        .Build();

    private static ViewDefinition Grouped() => new ViewDefinitionBuilder<GroupTotals>()
        .HasQueryMode(ViewQueryMode.OnTheFly)
        .From<T886Row>()
        .GroupBy<T886Row, string?>(r => r.Grp)
        .Select<T886Row, string?>(r => r.Grp, v => v.Grp)
        .Count<T886Row>(v => v.Cnt)
        .Sum<T886Row, int?>(r => r.Score, v => v.SumScore)
        .Min<T886Row, int?>(r => r.Score, v => v.MinScore)
        .Build();

    private async Task Run(Func<MongoDBClient, AsyncMongoDBStore<T886Row>, Task> body)
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var settings = new Settings(host, "birko_task886_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            var store = new AsyncMongoDBStore<T886Row>();
            store.SetSettings(settings);
            await body(client, store);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    [Theory]
    [InlineData(ViewQueryMode.OnTheFly)]
    [InlineData(ViewQueryMode.Persistent)]
    public Task An_ungrouped_view_over_no_rows_answers_one_row(ViewQueryMode mode) => Run(async (client, store) =>
    {
        await store.CreateAsync(new T886Row { Grp = "x", Score = 1 });
        await store.DeleteAsync(r => r.Grp == "x");
        if (mode == ViewQueryMode.Persistent) await new MongoViewManager(client).EnsureAsync(Whole(mode));
        var view = new MongoViewStore<WholeTotals>(client, Whole(mode));

        (await view.QueryAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 0, SumScore = 0 });
        (await view.CountAsync()).Should().Be(1);
        (await view.QueryAsync(v => v.Cnt > 0)).Should().BeEmpty("a view filter still applies to the row");
    });

    [Fact]
    public Task A_sum_over_only_nulls_is_zero() => Run(async (client, store) =>
    {
        await store.CreateAsync(new T886Row { Grp = "a", Score = null });
        await store.CreateAsync(new T886Row { Grp = "a", Score = null });
        await store.CreateAsync(new T886Row { Grp = "b", Score = 4 });

        (await new MongoViewStore<GroupTotals>(client, Grouped()).QueryAsync()).OrderBy(g => g.Grp).Should().BeEquivalentTo(new[]
        {
            new GroupTotals { Grp = "a", Cnt = 2, SumScore = 0 },
            new GroupTotals { Grp = "b", Cnt = 1, SumScore = 4, MinScore = 4 },
        }, o => o.WithStrictOrdering());

        await store.DeleteAsync(r => r.Grp == "b");
        (await new MongoViewStore<WholeTotals>(client, Whole(ViewQueryMode.OnTheFly)).QueryAsync())
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 2, SumScore = 0 });
    });
}
