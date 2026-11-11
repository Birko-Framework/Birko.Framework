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

    // ---------------------------------------------------------------------------------------------------------------
    // TASK-909 — the order of the result ROWS (the user's decision, 2026-11-11): AggregateQuery.StringComparison, Ordinal by
    // default, also orders the rows by a string group key or a string Min / Max, before the page (Offset / Limit) is cut.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the provider orders the result rows in memory (after reading every group), so it orders them in every
    /// comparison as the oracle does. A provider that orders on the server answers Ordinal and refuses another comparison by
    /// name. The in-memory stores by default; MongoDB, Elasticsearch and Cosmos DB order their rows in memory too.
    /// </summary>
    protected virtual bool OrdersRowsInEveryComparison => AnswersEveryComparison;

    /// <summary>
    /// The values a row order is measured over, distinct in Ordinal, each pair discriminating one way an order can differ
    /// from it: case (<c>Banana</c> below <c>apple</c>, <c>Zebra</c> below <c>zebra</c>), accents (<c>éclair</c> above
    /// <c>zebra</c>), a trailing space and a tab (<c>"a"</c> &lt; <c>"a\t"</c> &lt; <c>"a "</c>, which space padding ties or
    /// reverses), <c>ß</c> against <c>ss</c> and <c>st</c>, the empty string, a character above U+FFFF against U+E000 and
    /// U+FF5E (UTF-16 puts its surrogate pair below them, code-point order above), and null (below every value, as the
    /// oracle orders it).
    /// The rows ordered by the group key use every value as a key: TASK-909 left out the keys a GROUP BY merged (SQL Server
    /// grouped <c>Zebra</c> with <c>zebra</c>, <c>"a "</c> with <c>"a"</c>, <c>ß</c> with <c>ss</c>, U+E000 and U+1F600 with
    /// <c>""</c>; MySQL <c>Zebra</c> with <c>zebra</c>, <c>ß</c> with <c>ss</c>); TASK-911 groups exactly, so they are back.
    /// </summary>
    private static readonly string?[] RowOrderValues =
    {
        "apple", "Banana", "éclair", "zebra", "Zebra", "a", "a ", "a\t", "ß", "ss", "st", "", "\U0001F600", "\uFF5E", "\uE000", null,
    };

    /// <summary>The pages each row order is read in: all, the first four, a page from the middle, the last four.</summary>
    private static readonly (int? Offset, int? Limit)[] RowOrderWindows = { (null, null), (null, 4), (3, 5), (-4, null) };

    public static IEnumerable<object[]> RowOrderCases
        => from key in new[] { "min", "Kind" }
           from desc in new[] { false, true }
           from window in Enumerable.Range(0, RowOrderWindows.Length)
           from mode in new[] { "sync", "async" }
           select new object[] { key, desc, window, mode };

    /// <summary>Null below every string, strings in <paramref name="comparison"/>'s order: the order the oracle takes rows in.</summary>
    private static int CompareRowKeys(string? x, string? y, StringComparison comparison)
        => x == null ? (y == null ? 0 : -1) : y == null ? 1 : string.Compare(x, y, comparison);

    private async Task<IReadOnlyList<AggregateResult>> RunRows(string mode, IEnumerable<(string Kind, string? Name, int N)> rows, AggregateQuery<T> query)
    {
        if (mode == "async")
        {
            var store = NewAsyncStore();
            foreach (var row in rows) await store.CreateAsync(ToRow(row));
            Settle();
            return await ((IAsyncAggregatableStore<T>)store).AggregateAsync(query);
        }

        var sync = NewSyncStore();
        foreach (var row in rows) sync.Create(ToRow(row));
        Settle();
        return ((IAggregatableStore<T>)sync).Aggregate(query);
    }

    /// <summary>
    /// The rows ordered by a string, Ordinal by default, and paged after the order: by a string <c>Min</c> (one value per
    /// group, so every discriminating value is its own row) and by the string group key itself. Ascending puts null first
    /// and descending last, as the oracle orders it.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowOrderCases))]
    public async Task Contract_Rows_ordered_by_a_string_are_in_Ordinal_order_before_the_page(string key, bool descending, int window, string mode)
    {
        if (!Ready()) return;
        // By Min: a group per value (Kind g00..g15), its Min the value. By the key: a group per value, the key the value
        // (a null Kind is a group of its own, as in LINQ).
        var byKey = key == "Kind";
        var rows = byKey
            ? RowOrderValues.Select((v, i) => (Kind: v!, Name: (string?)"x", N: i)).ToList()
            : RowOrderValues.Select((v, i) => (Kind: $"g{i:00}", Name: v, N: i)).ToList();
        var (offset, limit) = RowOrderWindows[window];
        if (offset < 0) offset = rows.Count + offset;
        var query = new AggregateQuery<T>
        {
            GroupByFields = new[] { "Kind" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "N", "count"), new AggregateField(AggregateFunction.Min, "Name", "min") },
            OrderBy = new Dictionary<string, bool> { [key] = descending },
            Offset = offset,
            Limit = limit,
        };

        var ordered = rows.Select(r => (Kind: (string?)r.Kind, Value: byKey ? (string?)r.Kind : r.Name)).ToList();
        ordered.Sort((a, b) => CompareRowKeys(a.Value, b.Value, StringComparison.Ordinal) * (descending ? -1 : 1));
        var expected = ordered.Skip(offset ?? 0).Take(limit ?? int.MaxValue).Select(r => RenderValue(r.Kind)).ToList();

        Exception? thrown = null;
        var got = new List<string>();
        try { got = (await RunRows(mode, rows, query)).Select(r => RenderValue(r.GetValue<object>("Kind"))).ToList(); }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} rows by {key} {(descending ? "desc" : "asc")}, offset {offset?.ToString() ?? "-"} limit {limit?.ToString() ?? "-"} "
                      + $"(expected [{string.Join(" ", expected)}]; got [{string.Join(" ", got)}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }

    /// <summary>
    /// Rows that tie on the first key are ordered by the next, each in Ordinal order, so a page cut across a tie is the
    /// same page on every provider. (A tie no later key breaks keeps the provider's own order, as on any key: the oracle's
    /// is the order the groups first occur in.)
    /// </summary>
    [Theory]
    [MemberData(nameof(GroupingModes))]
    public async Task Contract_Rows_tied_on_a_string_are_ordered_by_the_next_key_in_Ordinal_order(bool descending, string mode)
    {
        if (!Ready()) return;
        var rows = new (string Kind, string? Name, int N)[]
        {
            ("c", "same", 1), ("B", "same", 2), ("a", "same", 3), ("d", "Zebra", 4), ("e", "zebra", 5), ("f", null, 6), ("g", null, 7),
        };
        var query = new AggregateQuery<T>
        {
            GroupByFields = new[] { "Kind" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Min, "Name", "min") },
            OrderBy = new Dictionary<string, bool> { ["min"] = descending, ["Kind"] = false },
            Offset = 1,
            Limit = 5,
        };
        var ordered = rows.ToList();
        ordered.Sort((a, b) =>
        {
            var first = CompareRowKeys(a.Name, b.Name, StringComparison.Ordinal) * (descending ? -1 : 1);
            return first != 0 ? first : CompareRowKeys(a.Kind, b.Kind, StringComparison.Ordinal);
        });
        var expected = ordered.Skip(1).Take(5).Select(r => RenderValue(r.Kind)).ToList();

        Exception? thrown = null;
        var got = new List<string>();
        try { got = (await RunRows(mode, rows, query)).Select(r => RenderValue(r.GetValue<object>("Kind"))).ToList(); }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} rows by min {(descending ? "desc" : "asc")} then Kind (expected [{string.Join(" ", expected)}]; "
                      + $"got [{string.Join(" ", got)}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }

    public static IEnumerable<object[]> RowOrderComparisons
        => from comparison in new[]
           {
               StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCulture, StringComparison.CurrentCultureIgnoreCase,
               StringComparison.InvariantCulture, StringComparison.InvariantCultureIgnoreCase,
           }
           from desc in new[] { false, true }
           from mode in new[] { "sync", "async" }
           select new object[] { comparison, desc, mode };

    /// <summary>
    /// Rows ordered by a string group key in another comparison (no string Min / Max in the query): a provider that orders
    /// its rows in memory orders them in that comparison (ties, such as <c>zebra</c> and <c>Zebra</c> ignoring case, in
    /// either order); a provider that orders on the server refuses by name, before any data, never answering in the
    /// database's own order.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowOrderComparisons))]
    public async Task Contract_Rows_ordered_by_a_string_in_another_comparison_are_ordered_in_memory_or_refused_by_name(
        StringComparison comparison, bool descending, string mode)
    {
        if (!Ready()) return;
        var rows = RowOrderValues.Select((v, i) => (Kind: v!, Name: (string?)"x", N: i)).ToList();
        var query = new AggregateQuery<T>
        {
            GroupByFields = new[] { "Kind" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "N", "count") },
            OrderBy = new Dictionary<string, bool> { ["Kind"] = descending },
            StringComparison = comparison,
        };

        Exception? thrown = null;
        var got = new List<string?>();
        try
        {
            // A provider that refuses is asked over no rows: the refusal does not depend on the data.
            var seeded = OrdersRowsInEveryComparison ? rows : rows.Take(0).ToList();
            got = (await RunRows(mode, seeded, query)).Select(r => r.GetValue<object>("Kind") as string).ToList();
        }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} rows by Kind {(descending ? "desc" : "asc")} in {comparison} order "
                      + $"(got [{string.Join(" ", got.Select(RenderValue))}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        if (!OrdersRowsInEveryComparison)
        {
            thrown.Should().BeOfType<NotSupportedException>(because);
            thrown!.Message.Should().Contain(comparison.ToString(), because).And.Contain("TASK-909", because);
            return;
        }

        thrown.Should().BeNull(because);
        got.Should().BeEquivalentTo(RowOrderValues, because);
        for (var i = 1; i < got.Count; i++)
        {
            (CompareRowKeys(got[i - 1], got[i], comparison) * (descending ? -1 : 1)).Should().BeLessThanOrEqualTo(0,
                $"{RenderValue(got[i - 1])} is before {RenderValue(got[i])}: {because}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // TASK-911 — the GROUPS: a query grouped by a string makes exactly the groups C# makes (Ordinal: object.Equals on the
    // key tuple), each answering its own key text, whatever the database's collation would merge.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Group keys that are distinct in C# and that some collation merges: case (<c>apple</c> / <c>APPLE</c>, <c>Zebra</c> /
    /// <c>zebra</c>), a trailing space and a tab (<c>"a"</c>, <c>"a "</c>, <c>"a\t"</c>), the empty string against a space, a
    /// no-break space against a space, <c>ß</c> against <c>ss</c>, a precomposed against a decomposed <c>é</c>, the Kelvin sign
    /// against <c>K</c>, U+E000 and U+1F600 (which SQL Server's default collation weighs as nothing, so as <c>""</c>), U+FF5E,
    /// and null. Measured before (TASK-909): SQL Server grouped 16 such keys into 12, MySQL into 14.
    /// </summary>
    private static readonly string?[] GroupKeyValues =
    {
        "apple", "APPLE", "Zebra", "zebra", "a", "a ", "a\t", "", " ", " ", "ß", "ss", "café", "café", "K", "K",
        "", "\U0001F600", "～", null,
    };

    /// <summary>
    /// The rows grouped: key <c>i</c> holds <c>i % 3 + 1</c> rows; each row's <c>Name</c> is a case twin of the others
    /// (<c>x</c>, <c>X</c>, <c>x</c>), so grouping by <c>Name</c> too splits a group by case; <c>N</c> is 1 or 2, so grouping
    /// by <c>N</c> too splits it by a number.
    /// </summary>
    private static List<(string Kind, string? Name, int N)> GroupRows()
    {
        var rows = new List<(string Kind, string? Name, int N)>();
        for (var i = 0; i < GroupKeyValues.Length; i++)
        {
            for (var j = 0; j <= i % 3; j++) rows.Add((GroupKeyValues[i]!, j % 2 == 0 ? "x" : "X", j % 2 + 1));
        }
        return rows;
    }

    public static IEnumerable<object[]> GroupCases
        => from shape in new[] { "Kind", "Kind+Name", "Name+Kind", "Kind+N", "N+Kind", "Kind ordered paged", "Kind desc paged" }
           from mode in new[] { "sync", "async" }
           select new object[] { shape, mode };

    private static AggregateQuery<T> GroupQuery(string shape) => shape switch
    {
        "Kind+Name" => GroupQuery(new[] { "Kind", "Name" }),
        "Name+Kind" => GroupQuery(new[] { "Name", "Kind" }),
        "Kind+N" => GroupQuery(new[] { "Kind", "N" }),
        "N+Kind" => GroupQuery(new[] { "N", "Kind" }),
        "Kind ordered paged" => GroupQuery(new[] { "Kind" }, new Dictionary<string, bool> { ["Kind"] = false }, 3, 9),
        "Kind desc paged" => GroupQuery(new[] { "Kind" }, new Dictionary<string, bool> { ["Kind"] = true }, 2, 7),
        _ => GroupQuery(new[] { "Kind" }),
    };

    private static AggregateQuery<T> GroupQuery(string[] keys, Dictionary<string, bool>? orderBy = null, int? offset = null, int? limit = null)
        => new()
        {
            GroupByFields = keys,
            Aggregates = new[]
            {
                new AggregateField(AggregateFunction.Count, "N", "count"),
                new AggregateField(AggregateFunction.Sum, "N", "sum"),
                new AggregateField(AggregateFunction.Min, "Name", "min"),
                new AggregateField(AggregateFunction.Max, "Name", "max"),
            },
            OrderBy = orderBy,
            Offset = offset,
            Limit = limit,
        };

    /// <summary>One group as text: every key (escaped, so a twin is visible), then count, sum, min and max with their types.</summary>
    private static string RenderGroup(AggregateResult row, IReadOnlyList<string> keys)
        => string.Join("; ", keys.Select(k => $"{k}={RenderValue(row.GetValue<object>(k))}"))
           + $"; count={RenderValue(row.GetValue<object>("count"))}; sum={RenderValue(row.GetValue<object>("sum"))}"
           + $"; min={RenderValue(row.GetValue<object>("min"))}; max={RenderValue(row.GetValue<object>("max"))}";

    /// <summary>
    /// Grouped by a string (alone, with a second string, with a number, in either position, and ordered and paged by the
    /// key): exactly the oracle's groups, each with its own key text, count, sum, Min and Max. An unordered query is compared
    /// as a set; an ordered one in order (TASK-909: Ordinal, null first ascending).
    /// </summary>
    [Theory]
    [MemberData(nameof(GroupCases))]
    public async Task Contract_Grouping_by_a_string_makes_exactly_the_groups_CSharp_makes(string shape, string mode)
    {
        if (!Ready()) return;
        var rows = GroupRows();
        var query = GroupQuery(shape);
        var ordered = query.OrderBy != null;
        var keys = query.GroupByFields;

        var expected = AggregateHelper.LinqAggregate(rows.Select(ToRow).ToList(), query).Select(r => RenderGroup(r, keys)).ToList();
        if (!ordered) expected.Sort(StringComparer.Ordinal);

        Exception? thrown = null;
        var got = new List<string>();
        try
        {
            got = (await RunRows(mode, rows, query)).Select(r => RenderGroup(r, keys)).ToList();
            if (!ordered) got.Sort(StringComparer.Ordinal);
        }
        catch (Exception ex) { thrown = ex; }

        var missing = expected.Except(got).ToList();
        var extra = got.Except(expected).ToList();
        var because = $"{mode} grouped by {shape}: {got.Count} groups where C# makes {expected.Count}; missing [{string.Join(" | ", missing)}]; "
                      + $"extra [{string.Join(" | ", extra)}]; thrown {thrown?.GetType().Name}: {thrown?.Message}";
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }
}
