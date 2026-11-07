using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// TASK-843: a view that projects a nested member (<c>r.Sub.X</c>) was recorded as source field <c>X</c> by the
/// shared builder, so the RavenDB index map read <c>entity.X</c>, the document's top-level field (measured on RavenDB 6,
/// 2026-11-07: every nested view property read its same-named top-level field). The map now reads the whole path,
/// <c>entity.Sub.X</c>. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task843RavenViewNestedProjectionLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task843RavenViewNestedProjectionLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
        return false;
    }

    public class T843Deeper { public int Level { get; set; } }

    public class T843Sub
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public T843Deeper? Deep { get; set; }
    }

    /// <summary>Top-level members named like the nested ones, with different values, so a flattened read shows.</summary>
    public class T843Row : AbstractModel
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public int Level { get; set; }
        public T843Sub Sub { get; set; } = new();
    }

    public class FlatView
    {
        public int TopX { get; set; }
        public int SubX { get; set; }
        public string? SubName { get; set; }
        public int? Level { get; set; }
    }

    private static ViewDefinition Nested() => new ViewDefinitionBuilder<FlatView>()
        .HasName("t843_nested").HasQueryMode(ViewQueryMode.Persistent)
        .From<T843Row>()
        .Select<T843Row, int>(r => r.X, v => v.TopX)
        .Select<T843Row, int>(r => r.Sub.X, v => v.SubX)
        .Select<T843Row, string?>(r => r.Sub.Name, v => v.SubName)
        .Select<T843Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
        .Build();

    private static IEnumerable<(int, int, string?, int?)> Tuples(IEnumerable<FlatView> rows)
        => rows.Select(v => (v.TopX, v.SubX, v.SubName, v.Level));

    private async Task Run(Func<IDocumentStore, List<FlatView>, Task> body)
    {
        if (!RequireServer()) return;
        var db = "birko-task843-" + Guid.NewGuid().ToString("N");
        using var rows = new AsyncRavenDBStore<T843Row>(Url!, db);
        try
        {
            var oracle = new List<FlatView>();
            for (var i = 0; i < 6; i++)
            {
                var row = new T843Row
                {
                    X = 10 + i, Name = "top" + i, Level = 100 + i,
                    Sub = new T843Sub { X = 5 - i, Name = "n" + i, Deep = i == 3 ? null : new T843Deeper { Level = i % 3 } },
                };
                await rows.CreateAsync(row);
                oracle.Add(new FlatView { TopX = row.X, SubX = row.Sub.X, SubName = row.Sub.Name, Level = row.Sub.Deep?.Level });
            }

            var store = rows.DocumentStore!;
            await new RavenViewManager(store).EnsureAsync(Nested());
            for (var i = 0; i < 100; i++)
            {
                if (!(await store.Maintenance.SendAsync(new GetIndexStatisticsOperation("t843_nested"))).IsStale) break;
                await Task.Delay(100);
            }
            await body(store, oracle);
        }
        finally
        {
            try { await rows.DestroyAsync(); } catch { }
        }
    }

    [Fact]
    public Task A_nested_projection_reads_the_nested_values() => Run(async (store, oracle) =>
    {
        var read = (await new RavenViewStore<FlatView>(store, Nested()).QueryAsync()).ToList();
        Tuples(read).Should().BeEquivalentTo(Tuples(oracle));
    });

    [Fact]
    public Task A_filter_a_count_and_an_order_on_a_nested_projection_use_the_nested_value() => Run(async (store, oracle) =>
    {
        var views = new RavenViewStore<FlatView>(store, Nested());
        (await views.QueryAsync(v => v.SubX >= 3)).Select(v => v.SubX).Should().BeEquivalentTo(new[] { 5, 4, 3 });
        (await views.CountAsync(v => v.Level == 1)).Should().Be(oracle.Count(v => v.Level == 1));
        (await views.QueryAsync(null, OrderBy<FlatView>.ByDescending(v => v.SubX))).Select(v => v.SubX)
            .Should().Equal(oracle.Select(v => v.SubX).OrderByDescending(x => x));
    });
}
