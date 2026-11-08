using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
/// TASK-867, live RavenDB: a view filter with an ordering against null returns what C# matches. RQL ranks null below
/// every value, so <c>Points &gt; null</c> matched every non-null row where C# matches none. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task867RavenViewNullOrderingLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task867RavenViewNullOrderingLiveTests(ITestOutputHelper output) => _output = output;

    public class T867Row : AbstractModel { public string? Code { get; set; } public int A { get; set; } public int? Score { get; set; } }
    public class ScoreView { public string? Code { get; set; } public int A { get; set; } public int? Points { get; set; } }

    private static readonly int? Nul = null;

    public static readonly IReadOnlyDictionary<string, Expression<Func<ScoreView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<ScoreView, bool>>>
        {
            ["Points > nul"] = v => v.Points > Nul,
            ["!(Points > nul)"] = v => !(v.Points > Nul),
            ["nul <= Points"] = v => Nul <= v.Points,
            ["Points > nul || A > 4"] = v => v.Points > Nul || v.A > 4,
            ["!(Points > nul || A > 3)"] = v => !(v.Points > Nul || v.A > 3),
            ["!(Points > nul) && A > 4"] = v => !(v.Points > Nul) && v.A > 4,
            ["!(Points < nul && A > 1)"] = v => !(v.Points < Nul && v.A > 1),
            ["Points != nul"] = v => v.Points != Nul,
            ["Points == nul"] = v => v.Points == Nul,
        };

    [Fact]
    public async Task A_view_filter_with_a_null_ordering_returns_what_CSharp_matches()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }

        var db = "birko-task867-" + Guid.NewGuid().ToString("N");
        using var rows = new AsyncRavenDBStore<T867Row>(Url!, db);
        try
        {
            int?[] scores = { null, 5, null, 8, null, 2 };
            var oracle = new List<ScoreView>();
            for (var i = 0; i < scores.Length; i++)
            {
                await rows.CreateAsync(new T867Row { Code = "r" + (i + 1), A = i + 1, Score = scores[i] });
                oracle.Add(new ScoreView { Code = "r" + (i + 1), A = i + 1, Points = scores[i] });
            }

            var definition = new ViewDefinitionBuilder<ScoreView>()
                .HasName("t867_scores").HasQueryMode(ViewQueryMode.Persistent)
                .From<T867Row>()
                .Select<T867Row, string?>(r => r.Code, v => v.Code)
                .Select<T867Row, int>(r => r.A, v => v.A)
                .Select<T867Row, int?>(r => r.Score, v => v.Points)
                .Build();
            IDocumentStore store = rows.DocumentStore!;
            await new RavenViewManager(store).EnsureAsync(definition);
            for (var i = 0; i < 100 && (await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!))).IsStale; i++)
                await Task.Delay(100);

            var view = new RavenViewStore<ScoreView>(store, definition);
            var failures = new List<string>();
            foreach (var (shape, filter) in Shapes)
            {
                var expected = oracle.Where(filter.Compile()).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                try
                {
                    var read = (await view.QueryAsync(filter)).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var count = await view.CountAsync(filter);
                    if (!read.SequenceEqual(expected) || count != expected.Count)
                        failures.Add($"{shape}: C# {string.Join(",", expected)}, read {string.Join(",", read)}, count {count}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
            }
            failures.Should().BeEmpty();
        }
        finally
        {
            try { await rows.DestroyAsync(); } catch { }
        }
    }
}
