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
/// TASK-868, live RavenDB: <see cref="RavenViewStore{TView}"/> runs its filter through <c>RavenFilterRewriter</c> (since
/// TASK-867), so the portable set-membership spelling <c>codes.Contains(v.Code)</c> and the all-rows synonym
/// <c>v =&gt; true</c> work on a view. The control shows the rewrite is what makes them work: handed to the driver
/// unrewritten, each is refused. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task868RavenViewContainsLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task868RavenViewContainsLiveTests(ITestOutputHelper output) => _output = output;

    public class T868ViewRow : AbstractModel { public string? Code { get; set; } public int A { get; set; } }
    public class CodeView { public string? Code { get; set; } public int A { get; set; } }

    private static readonly string[] Codes = { "r2", "r5", "missing" };
    private static readonly List<string> CodeList = new() { "r1", "r6" };

    public static readonly IReadOnlyDictionary<string, Expression<Func<CodeView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<CodeView, bool>>>
        {
            ["array.Contains(Code)"] = v => Codes.Contains(v.Code),
            ["list.Contains(Code)"] = v => CodeList.Contains(v.Code!),
            ["!array.Contains(Code) && A > 2"] = v => !Codes.Contains(v.Code) && v.A > 2,
            ["array.Contains(Code) || A == 1"] = v => Codes.Contains(v.Code) || v.A == 1,
            ["v => true"] = v => true,
        };

    [Fact]
    public async Task A_view_filter_with_Contains_or_true_returns_what_CSharp_matches()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }

        var db = "birko-task868-" + Guid.NewGuid().ToString("N");
        using var rows = new AsyncRavenDBStore<T868ViewRow>(Url!, db);
        try
        {
            var oracle = new List<CodeView>();
            for (var i = 1; i <= 6; i++)
            {
                await rows.CreateAsync(new T868ViewRow { Code = "r" + i, A = i });
                oracle.Add(new CodeView { Code = "r" + i, A = i });
            }

            var definition = new ViewDefinitionBuilder<CodeView>()
                .HasName("t868_codes").HasQueryMode(ViewQueryMode.Persistent)
                .From<T868ViewRow>()
                .Select<T868ViewRow, string?>(r => r.Code, v => v.Code)
                .Select<T868ViewRow, int>(r => r.A, v => v.A)
                .Build();
            IDocumentStore store = rows.DocumentStore!;
            await new RavenViewManager(store).EnsureAsync(definition);
            for (var i = 0; i < 100 && (await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!))).IsStale; i++)
                await Task.Delay(100);

            var view = new RavenViewStore<CodeView>(store, definition);
            var failures = new List<string>();
            foreach (var (shape, filter) in Shapes)
            {
                var expected = oracle.Where(filter.Compile()).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                try
                {
                    var read = (await view.QueryAsync(filter)).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var count = await view.CountAsync(filter);
                    var first = await view.QueryFirstAsync(filter);
                    if (!read.SequenceEqual(expected) || count != expected.Count || (first == null) != (expected.Count == 0))
                        failures.Add($"{shape}: C# {string.Join(",", expected)}, read {string.Join(",", read)}, count {count}, first {first?.Code ?? "null"}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }

                // Control: the same filter, unrewritten, straight to the driver over the same index.
                using var session = store.OpenAsyncSession();
                Func<Task> raw = () => session.Query<CodeView>(definition.Name).Where(filter).ToListAsync();
                await raw.Should().ThrowAsync<Exception>($"'{shape}' must need the rewrite, or this test proves nothing");
            }
            failures.Should().BeEmpty();
        }
        finally
        {
            try { await rows.DestroyAsync(); } catch { }
        }
    }
}
