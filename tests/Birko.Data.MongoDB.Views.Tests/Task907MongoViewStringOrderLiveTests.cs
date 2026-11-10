using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-907, live MongoDB: a view's string Min / Max is Ordinal, as the store's <c>Aggregate</c> is by default (the user's
/// decision, 2026-11-10). MongoDB compares strings by their UTF-8 bytes, code-point order: Ordinal but for a character above
/// U+FFFF against one in U+E000–U+FFFF, a group holding both is refused by name. A view's Min / Max of an enum stored as text, which <c>$min</c>
/// takes by name, is refused by name. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task907MongoViewStringOrderLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task907MongoViewStringOrderLiveTests(ITestOutputHelper output) => _output = output;

    public enum T907Priority { Zero = 0, High = 30, Low = 10 }

    public class T907Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }

        [BsonRepresentation(BsonType.String)]
        public T907Priority Priority { get; set; }
    }

    public class Extremes
    {
        public string? Kind { get; set; }
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    public class PriorityExtremes
    {
        public T907Priority Low { get; set; }
    }

    private static readonly (string Kind, string? Name)[] Seed =
    {
        ("case", "apple"), ("case", "Banana"),
        ("accent", "éclair"), ("accent", "zebra"),
        ("word", "zebra"), ("word", "Zebra"),
        ("space", "a "), ("space", "a"),
        ("eszett", "ß"), ("eszett", "ss"),
        ("nulls", null), ("nulls", "m"),
        ("allnull", null),
    };

    private static ViewDefinition Grouped(ViewQueryMode mode) => new ViewDefinitionBuilder<Extremes>()
        .HasName("t907_extremes").HasQueryMode(mode)
        .From<T907Row>()
        .GroupBy<T907Row, string?>(r => r.Kind)
        .Select<T907Row, string?>(r => r.Kind, v => v.Kind)
        .Min<T907Row, string?>(r => r.Name, v => v.Low)
        .Max<T907Row, string?>(r => r.Name, v => v.High)
        .Build();

    private async Task Run(Func<MongoDBClient, AsyncMongoDBStore<T907Row>, Task> body)
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var settings = new Settings(host, "birko_task907_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            var store = new AsyncMongoDBStore<T907Row>();
            store.SetSettings(settings);
            await body(client, store);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    private static string Show(string? value)
        => value == null ? "null" : "\"" + string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    [Theory]
    [InlineData(ViewQueryMode.OnTheFly)]
    [InlineData(ViewQueryMode.Persistent)]
    public Task A_view_string_min_and_max_are_Ordinal(ViewQueryMode mode) => Run(async (client, store) =>
    {
        foreach (var (kind, name) in Seed) await store.CreateAsync(new T907Row { Kind = kind, Name = name });
        var definition = Grouped(mode);
        if (mode == ViewQueryMode.Persistent) await new MongoViewManager(client).EnsureAsync(definition);

        var expected = string.Join(", ", Seed.GroupBy(s => s.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var values = g.Select(s => s.Name).Where(n => n != null).ToList();
            return $"{g.Key}={Show(values.Count == 0 ? null : values.Min(StringComparer.Ordinal))}..{Show(values.Count == 0 ? null : values.Max(StringComparer.Ordinal))}";
        }));
        var rows = (await new MongoViewStore<Extremes>(client, definition).QueryAsync()).ToList();
        var actual = string.Join(", ", rows.OrderBy(r => r.Kind, StringComparer.Ordinal).Select(r => $"{r.Kind}={Show(r.Low)}..{Show(r.High)}"));

        _output.WriteLine($"MEASURE | MongoDB | {mode} | {actual} | expected {expected}");
        actual.Should().Be(expected);
    });

    /// <summary>
    /// Inverted (rule 56): a group holding both a character above U+FFFF and one in U+E000–U+FFFF, which MongoDB's
    /// code-point order answers otherwise than Ordinal, is refused by name; a group of astral characters only, or of
    /// U+E000–U+FFFF only, is answered exactly. Before: the mixed group answered U+FF5E as its Min (pinned).
    /// </summary>
    [Theory]
    [InlineData("mixed", ViewQueryMode.OnTheFly)]
    [InlineData("mixed", ViewQueryMode.Persistent)]
    [InlineData("astral only", ViewQueryMode.OnTheFly)]
    [InlineData("high BMP only", ViewQueryMode.Persistent)]
    public Task An_astral_group_is_answered_exactly_or_refused_when_mixed(string shape, ViewQueryMode mode) => Run(async (client, store) =>
    {
        var names = shape switch
        {
            "mixed" => new[] { "\U0001F600", "\uFF5E" },
            "astral only" => new[] { "\U0001F600", "\U0001F4A9" },
            _ => new[] { "\uFF5E", "\uE000" },
        };
        foreach (var name in names) await store.CreateAsync(new T907Row { Kind = "g", Name = name });
        await store.CreateAsync(new T907Row { Kind = "plain", Name = "p" });
        var definition = Grouped(mode);
        if (mode == ViewQueryMode.Persistent) await new MongoViewManager(client).EnsureAsync(definition);

        var act = async () => (await new MongoViewStore<Extremes>(client, definition).QueryAsync()).ToList();

        if (shape == "mixed")
        {
            (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("U+FFFF").And.Contain("T907Row.Name");
            return;
        }
        var row = (await act()).Single(r => r.Kind == "g");
        (row.Low, row.High).Should().Be((names.Min(StringComparer.Ordinal), names.Max(StringComparer.Ordinal)));
    });

    [Theory]
    [InlineData(ViewQueryMode.OnTheFly)]
    [InlineData(ViewQueryMode.Persistent)]
    public Task A_view_min_of_an_enum_stored_as_text_is_refused_by_name(ViewQueryMode mode) => Run(async (client, store) =>
    {
        await store.CreateAsync(new T907Row { Kind = "a", Priority = T907Priority.High });
        await store.CreateAsync(new T907Row { Kind = "a", Priority = T907Priority.Zero });
        var definition = new ViewDefinitionBuilder<PriorityExtremes>()
            .HasName("t907_priority").HasQueryMode(mode)
            .From<T907Row>()
            .Min<T907Row, T907Priority>(r => r.Priority, v => v.Low)
            .Build();

        var act = async () =>
        {
            if (mode == ViewQueryMode.Persistent) await new MongoViewManager(client).EnsureAsync(definition);
            await new MongoViewStore<PriorityExtremes>(client, definition).QueryAsync();
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("T907Row.Priority");
    });
}
