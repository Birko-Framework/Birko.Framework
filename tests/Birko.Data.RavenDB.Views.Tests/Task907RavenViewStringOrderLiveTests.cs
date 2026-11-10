using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// TASK-907, live RavenDB: an aggregate view's string Min / Max is Ordinal (UTF-16 code units), as the store's
/// <c>Aggregate</c> is by default (the user's decision, 2026-11-10). Each group discriminates one way an order can differ
/// from Ordinal. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task907RavenViewStringOrderLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task907RavenViewStringOrderLiveTests(ITestOutputHelper output) => _output = output;

    public class T907Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public class Extremes
    {
        public string? Kind { get; set; }
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    private static readonly (string Kind, string? Name)[] Seed =
    {
        ("case", "apple"), ("case", "Banana"),
        ("accent", "éclair"), ("accent", "zebra"),
        ("word", "zebra"), ("word", "Zebra"),
        ("space", "a "), ("space", "a"),
        ("eszett", "ß"), ("eszett", "ss"),
        ("astral", "\U0001F600"), ("astral", "～"),
        ("nulls", null), ("nulls", "m"),
        ("allnull", null),
    };

    private static ViewDefinition Grouped() => new ViewDefinitionBuilder<Extremes>()
        .HasName("t907_extremes").HasQueryMode(ViewQueryMode.Persistent)
        .From<T907Row>()
        .GroupBy<T907Row, string?>(r => r.Kind)
        .Select<T907Row, string?>(r => r.Kind, v => v.Kind)
        .Min<T907Row, string?>(r => r.Name, v => v.Low)
        .Max<T907Row, string?>(r => r.Name, v => v.High)
        .Build();

    private async Task Run(Func<IDocumentStore, AsyncRavenDBStore<T907Row>, Task> body)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        using var rows = new AsyncRavenDBStore<T907Row>(Url!, "birko-task907-" + Guid.NewGuid().ToString("N"));
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

    private async Task Ensure(IDocumentStore store, ViewDefinition definition)
    {
        await new RavenViewManager(store).EnsureAsync(definition);
        for (var i = 0; i < 100 && (await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!))).IsStale; i++)
            await Task.Delay(100);
        foreach (var errors in await store.Maintenance.SendAsync(new GetIndexErrorsOperation(new[] { definition.Name! })))
            foreach (var error in errors.Errors.Take(3))
                _output.WriteLine($"index error ({error.Action}): {error.Error}");
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    [Fact]
    public Task A_view_string_min_and_max_are_Ordinal() => Run(async (store, rows) =>
    {
        foreach (var (kind, name) in Seed) await rows.CreateAsync(new T907Row { Kind = kind, Name = name });
        var definition = Grouped();
        await Ensure(store, definition);

        var expected = string.Join(", ", Seed.GroupBy(s => s.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var values = g.Select(s => s.Name).Where(n => n != null).ToList();
            return $"{g.Key}={Show(values.Count == 0 ? null : values.Min(StringComparer.Ordinal))}..{Show(values.Count == 0 ? null : values.Max(StringComparer.Ordinal))}";
        }));
        var got = (await new RavenViewStore<Extremes>(store, definition).QueryAsync()).ToList();
        var actual = string.Join(", ", got.OrderBy(r => r.Kind, StringComparer.Ordinal).Select(r => $"{r.Kind}={Show(r.Low)}..{Show(r.High)}"));

        _output.WriteLine($"MEASURE | RavenDB | {actual} | expected {expected}");
        actual.Should().Be(expected);
    });
}
