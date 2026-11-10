using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// The shape every <see cref="AggregateStringOrderContract{T}"/> row is written as. A provider's row type has the same
/// three properties by name.
/// </summary>
public sealed class AggregateStringOrderShape
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>
/// TASK-907 — the order of a string <c>Min</c> / <c>Max</c> (the user's decision, 2026-11-10): a parameter on the query,
/// <see cref="AggregateQuery{T}.StringComparison"/>, <see cref="StringComparison.Ordinal"/> by default. Every provider
/// answers Ordinal exactly; another comparison is answered in memory (the oracle, <see cref="AggregateHelper"/>) and
/// refused by name (<see cref="NotSupportedException"/> naming the comparison) where a provider cannot order that way —
/// never answered in the database's own order. A comparison never refuses a query whose Min / Max is not of a string.
/// <para>
/// Each group discriminates one way an order can differ from Ordinal (UTF-16 code units): case (<c>Banana</c> below
/// <c>apple</c>), accents (<c>éclair</c> above <c>zebra</c>), case of one word, a trailing space (<c>"a"</c> below
/// <c>"a "</c>, which a PAD SPACE collation ties), <c>ß</c> against <c>ss</c>, a null beside a value, only nulls, the empty
/// string, and a character below the space after a shorter value (<c>"a"</c> below <c>"a\t"</c>, which space padding
/// reverses).
/// </para>
/// <para>
/// Shared source, linked into each provider's test project, which subclasses it with fresh, empty stores of a row type
/// holding <c>string? Kind</c>, <c>string? Name</c> and <c>int N</c>.
/// </para>
/// </summary>
public abstract class AggregateStringOrderContract<T> where T : AbstractModel, new()
{
    protected virtual bool Ready() => true;

    /// <summary>Makes earlier writes visible to a read (Elasticsearch refreshes); nothing by default.</summary>
    protected virtual void Settle() { }

    /// <summary>Whether the provider aggregates in memory, so it answers every comparison as the oracle does.</summary>
    protected virtual bool AnswersEveryComparison => false;

    /// <summary>A fresh, empty async store.</summary>
    protected abstract IAsyncBulkStore<T> NewAsyncStore();

    /// <summary>A fresh, empty sync store.</summary>
    protected abstract IBulkStore<T> NewSyncStore();

    private static readonly AggregateField[] Metrics =
    {
        new(AggregateFunction.Count, "Name", "count"),
        new(AggregateFunction.Min, "Name", "min"),
        new(AggregateFunction.Max, "Name", "max"),
    };

    private static readonly (string Kind, string? Name, int N)[] Seed =
    {
        ("case", "apple", 1), ("case", "Banana", 2),
        ("accent", "éclair", 3), ("accent", "zebra", 4),
        ("word", "zebra", 5), ("word", "Zebra", 6),
        ("space", "a ", 7), ("space", "a", 8),
        ("eszett", "ß", 9), ("eszett", "ss", 10),
        ("nulls", null, 11), ("nulls", "m", 12),
        ("allnull", null, 13),
        ("empty", "", 14), ("empty", "b", 15),
        ("tab", "a", 16), ("tab", "a\t", 17),
    };

    /// <summary>The Ordinal answer, by group: count, Min, Max.</summary>
    private static readonly Dictionary<string, (int Count, string? Min, string? Max)> OrdinalGroups = new()
    {
        ["case"] = (2, "Banana", "apple"),
        ["accent"] = (2, "zebra", "éclair"),
        ["word"] = (2, "Zebra", "zebra"),
        ["space"] = (2, "a", "a "),
        ["eszett"] = (2, "ss", "ß"),
        ["nulls"] = (2, "m", "m"),
        ["allnull"] = (1, null, null),
        ["empty"] = (2, "", "b"),
        ["tab"] = (2, "a", "a\t"),
    };

    private static readonly (int Count, string? Min, string? Max) OrdinalAll = (17, "", "éclair");

    public static IEnumerable<object[]> Modes => new[] { new object[] { "sync" }, new object[] { "async" } };

    public static IEnumerable<object[]> GroupingModes
        => from grouped in new[] { false, true } from mode in new[] { "sync", "async" } select new object[] { grouped, mode };

    public static IEnumerable<object[]> OtherComparisons
        => from comparison in new[]
           {
               StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCulture, StringComparison.CurrentCultureIgnoreCase,
               StringComparison.InvariantCulture, StringComparison.InvariantCultureIgnoreCase,
           }
           from grouped in new[] { false, true }
           from mode in new[] { "sync", "async" }
           select new object[] { comparison, grouped, mode };

    private static T ToRow((string Kind, string? Name, int N) seed)
    {
        var row = new T();
        typeof(T).GetProperty(nameof(AggregateStringOrderShape.Kind))!.SetValue(row, seed.Kind);
        typeof(T).GetProperty(nameof(AggregateStringOrderShape.Name))!.SetValue(row, seed.Name);
        typeof(T).GetProperty(nameof(AggregateStringOrderShape.N))!.SetValue(row, seed.N);
        return row;
    }

    private async Task<IReadOnlyList<AggregateResult>> Run(string mode, AggregateQuery<T> query)
    {
        if (mode == "async")
        {
            var store = NewAsyncStore();
            foreach (var seed in Seed) await store.CreateAsync(ToRow(seed));
            Settle();
            var aggregatable = store as IAsyncAggregatableStore<T>
                ?? throw new InvalidOperationException($"{store.GetType().Name} does not aggregate.");
            return await aggregatable.AggregateAsync(query);
        }

        var sync = NewSyncStore();
        foreach (var seed in Seed) sync.Create(ToRow(seed));
        Settle();
        var syncAggregatable = sync as IAggregatableStore<T>
            ?? throw new InvalidOperationException($"{sync.GetType().Name} does not aggregate.");
        return syncAggregatable.Aggregate(query);
    }

    private static AggregateQuery<T> Query(bool grouped, StringComparison? comparison, IReadOnlyList<AggregateField>? metrics = null)
    {
        var query = new AggregateQuery<T>
        {
            GroupByFields = grouped ? new[] { "Kind" } : Array.Empty<string>(),
            Aggregates = metrics ?? Metrics,
        };
        return comparison == null ? query : new AggregateQuery<T>
        {
            GroupByFields = query.GroupByFields,
            Aggregates = query.Aggregates,
            StringComparison = comparison.Value,
        };
    }

    /// <summary>One row as text, its strings escaped so a trailing space or a non-ASCII letter is visible.</summary>
    private static string Render(AggregateResult row, bool grouped)
    {
        var kind = grouped ? row.GetValue<object>("Kind") as string ?? "(null)" : "*";
        return $"{kind}: count={RenderValue(row.GetValue<object>("count"))}; min={RenderValue(row.GetValue<object>("min"))}; "
               + $"max={RenderValue(row.GetValue<object>("max"))}";
    }

    private static string RenderValue(object? value) => value switch
    {
        null => "null",
        string s => "\"" + string.Concat(s.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"",
        IFormattable f => value.GetType().Name + ":" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value,
    };

    private static List<string> Expected(bool grouped)
    {
        if (!grouped) return new List<string> { Render(OrdinalAll, "*") };
        return OrdinalGroups.Select(g => Render(g.Value, g.Key)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private static string Render((int Count, string? Min, string? Max) row, string kind)
        => $"{kind}: count={RenderValue(row.Count)}; min={RenderValue(row.Min)}; max={RenderValue(row.Max)}";

    private async Task AssertOrdinal(bool grouped, string mode, StringComparison? comparison)
    {
        Exception? thrown = null;
        var got = new List<string>();
        try
        {
            var results = await Run(mode, Query(grouped, comparison));
            got = results.Select(r => Render(r, grouped)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) { thrown = ex; }

        var expected = Expected(grouped);
        var because = $"{mode} {(grouped ? "grouped" : "ungrouped")} string Min/Max, {comparison?.ToString() ?? "default"} order "
                      + $"(expected [{string.Join(" | ", expected)}]; got [{string.Join(" | ", got)}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }

    [Theory]
    [MemberData(nameof(GroupingModes))]
    public Task Contract_A_string_Min_Max_is_Ordinal_by_default(bool grouped, string mode)
        => Ready() ? AssertOrdinal(grouped, mode, null) : Task.CompletedTask;

    [Theory]
    [MemberData(nameof(GroupingModes))]
    public Task Contract_A_string_Min_Max_asked_for_Ordinal_is_Ordinal(bool grouped, string mode)
        => Ready() ? AssertOrdinal(grouped, mode, StringComparison.Ordinal) : Task.CompletedTask;

    /// <summary>
    /// Another comparison: the oracle's answer where the provider aggregates in memory (a Min / Max equal to the oracle's
    /// under that comparison, since tied values may be either), otherwise a refusal naming the comparison.
    /// </summary>
    [Theory]
    [MemberData(nameof(OtherComparisons))]
    public async Task Contract_A_string_Min_Max_in_another_order_is_answered_in_memory_or_refused_by_name(
        StringComparison comparison, bool grouped, string mode)
    {
        if (!Ready()) return;

        Exception? thrown = null;
        IReadOnlyList<AggregateResult> results = Array.Empty<AggregateResult>();
        try { results = await Run(mode, Query(grouped, comparison)); }
        catch (Exception ex) { thrown = ex; }

        var describe = $"{mode} {(grouped ? "grouped" : "ungrouped")} string Min/Max in {comparison} order "
                       + $"(got [{string.Join(" | ", results.Select(r => Render(r, grouped)))}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        if (!AnswersEveryComparison)
        {
            thrown.Should().BeOfType<NotSupportedException>(describe);
            thrown!.Message.Should().Contain(comparison.ToString(), describe);
            return;
        }

        thrown.Should().BeNull(describe);
        var oracle = AggregateHelper.LinqAggregate(Seed.Select(ToRow), Query(grouped, comparison));
        var comparer = StringComparer.FromComparison(comparison);
        results.Should().HaveCount(oracle.Count, describe);
        foreach (var expected in oracle)
        {
            var kind = grouped ? expected.GetValue<string>("Kind") : null;
            var row = results.Single(r => !grouped || r.GetValue<string>("Kind") == kind);
            row.GetValue<object>("count").Should().Be(expected.GetValue<object>("count"), describe);
            foreach (var metric in new[] { "min", "max" })
            {
                var want = expected.GetValue<string>(metric);
                var have = row.GetValue<object>(metric) as string;
                if (want == null) have.Should().BeNull(describe);
                else comparer.Equals(have, want).Should().BeTrue($"{metric} of {kind ?? "*"} is {RenderValue(have)} where the oracle answers {RenderValue(want)}: {describe}");
            }
        }
    }

    /// <summary>
    /// Whether the provider orders strings by code point, which is Ordinal (UTF-16 code units) except where a character
    /// above U+FFFF meets one in U+E000–U+FFFF: UTF-16 puts the surrogate pair (U+D800–U+DFFF) below it, code points above.
    /// Such a provider refuses a group that holds both by name, and answers every other group exactly.
    /// </summary>
    protected virtual bool OrdersByCodePoint => false;

    public static IEnumerable<object[]> AstralCases
        => from shape in new[] { "mixed", "astral only", "high BMP only" }
           from grouped in new[] { false, true }
           from mode in new[] { "sync", "async" }
           select new object[] { shape, grouped, mode };

    [Theory]
    [MemberData(nameof(AstralCases))]
    public async Task Contract_An_astral_character_is_ordered_by_UTF16_code_units_or_a_mixed_group_is_refused(string shape, bool grouped, string mode)
    {
        if (!Ready()) return;
        // U+1F600 is D83D DE00 in UTF-16, below U+FF5E; as a code point it is above. A second group of plain text is
        // answered in every shape, so a refusal is of the mixed group only.
        var group = shape switch
        {
            "mixed" => new[] { "\U0001F600", "\uFF5E", "x" },
            "astral only" => new[] { "\U0001F600", "\U0001F4A9", "x" },
            _ => new[] { "\uFF5E", "\uE000", "x" },
        };
        var rows = group.Select((n, i) => ("g", (string?)n, i)).Concat(new[] { ("plain", (string?)"p", 9), ("plain", (string?)"q", 10) }).ToArray();

        Exception? thrown = null;
        var got = new List<string>();
        try
        {
            IReadOnlyList<AggregateResult> results;
            var query = Query(grouped, null);
            if (mode == "async")
            {
                var store = NewAsyncStore();
                foreach (var row in rows) await store.CreateAsync(ToRow(row));
                Settle();
                results = await ((IAsyncAggregatableStore<T>)store).AggregateAsync(query);
            }
            else
            {
                var store = NewSyncStore();
                foreach (var row in rows) store.Create(ToRow(row));
                Settle();
                results = ((IAggregatableStore<T>)store).Aggregate(query);
            }
            got = results.Select(r => Render(r, grouped)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) { thrown = ex; }

        var expected = AggregateHelper.LinqAggregate(rows.Select(ToRow).ToList(), Query(grouped, null))
            .Select(r => Render(r, grouped)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var because = $"{mode} {(grouped ? "grouped" : "ungrouped")} Min/Max, {shape} (expected [{string.Join(" | ", expected)}]; "
                      + $"got [{string.Join(" | ", got)}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        if (shape == "mixed" && OrdersByCodePoint)
        {
            thrown.Should().BeOfType<NotSupportedException>(because);
            thrown!.Message.Should().Contain("U+FFFF").And.Contain("TASK-907");
            return;
        }
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }

    /// <summary>
    /// The refusal of another order does not depend on the data: on an empty store (no collection, no index, no table yet)
    /// it is refused as it is over rows. Measured before on MongoDB: an empty ungrouped store answered in memory.
    /// </summary>
    [Theory]
    [MemberData(nameof(GroupingModes))]
    public async Task Contract_Another_order_is_refused_before_any_data(bool grouped, string mode)
    {
        if (!Ready() || AnswersEveryComparison) return;
        var query = Query(grouped, StringComparison.CurrentCulture);
        Exception? thrown = null;
        try
        {
            if (mode == "async") await ((IAsyncAggregatableStore<T>)NewAsyncStore()).AggregateAsync(query);
            else ((IAggregatableStore<T>)NewSyncStore()).Aggregate(query);
        }
        catch (Exception ex) { thrown = ex; }

        thrown.Should().BeOfType<NotSupportedException>($"{mode} {(grouped ? "grouped" : "ungrouped")} over an empty store: {thrown?.Message}");
        thrown!.Message.Should().Contain("CurrentCulture");
    }

    /// <summary>The comparison orders only strings: a Min / Max of an int with another comparison is answered, never refused.</summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Contract_A_comparison_does_not_refuse_a_Min_Max_of_a_number(string mode)
    {
        if (!Ready()) return;
        var metrics = new[]
        {
            new AggregateField(AggregateFunction.Count, "Name", "count"),
            new AggregateField(AggregateFunction.Min, "N", "min"),
            new AggregateField(AggregateFunction.Max, "N", "max"),
        };

        Exception? thrown = null;
        var got = new List<string>();
        try
        {
            var results = await Run(mode, Query(false, StringComparison.CurrentCulture, metrics));
            got = results.Select(r => Render(r, false)).ToList();
        }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} Min/Max of an int with CurrentCulture (got [{string.Join(" | ", got)}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeNull(because);
        got.Should().Equal(new[] { "*: count=Int32:17; min=Int32:1; max=Int32:17" }, because);
    }
}
