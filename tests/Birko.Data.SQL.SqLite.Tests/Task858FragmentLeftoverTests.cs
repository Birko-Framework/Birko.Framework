using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-858 — three leftovers TASK-856 filed, each re-measured first (rule 54). None reproduced, so each is pinned here:
/// <list type="bullet">
/// <item><c>ReturnSingleSubCondition</c> never copied <c>IsField</c>, but no filter can collapse onto a column-vs-column
/// leaf. The parser nests every comparison it builds under the accumulator it was given, so the surviving node of a
/// collapse (<c>yes &amp;&amp; x.A == x.B</c>) is always a group, and its <c>IsField</c> leaf moves with its
/// <c>SubConditions</c>.</item>
/// <item>The diagnostic text is <c>GetGeneratedQuery</c> of the command the connector executes, which already holds the
/// provider's rendering of a fragment.</item>
/// <item>A constant inside a fragment is evaluated once: <c>ExpressionNormalizer</c> folds every parameter-free subtree
/// to a constant before the parser renders anything, so the provider's re-render reads a constant.</item>
/// </list>
/// </summary>
public sealed class Task858FragmentLeftoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t858-{Guid.NewGuid():N}");

    public Task858FragmentLeftoverTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestTempDirectory.Delete(_root);

    [Table("T858Ledger")]
    public class Ledger : AbstractModel
    {
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
        public int Hits { get; set; }
    }

    private async Task<(AsyncSQLiteStore<Ledger> Store, List<Ledger> Seed)> Seeded(AsyncSQLiteStore<Ledger>? store = null)
    {
        store ??= new AsyncSQLiteStore<Ledger>();
        store.SetSettings(new SqLiteSettings(_root, "t858.db"));
        var seed = new List<Ledger>();
        // Optional is NULL on 7 and differs from Amount elsewhere, so a column-vs-column test that compared a value would show.
        foreach (var amount in new[] { 10.10m, 10.1m, 5.25m, 0.30m, 7m, 10.14m, 0m, 9.5m })
        {
            var row = new Ledger
            {
                Guid = Guid.NewGuid(),
                Name = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Amount = amount,
                Optional = amount == 7m ? null : amount == 0m ? 0m : amount * 2m - 10m,
                Hits = (int)amount,
            };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static List<Guid> Ids(IEnumerable<Ledger> rows) => rows.Select(x => x.Guid!.Value).OrderBy(x => x).ToList();

    private static IEnumerable<Condition> Flatten(IEnumerable<Condition> conditions)
        => conditions.SelectMany(c => new[] { c }.Concat(Flatten(c.SubConditions ?? Enumerable.Empty<Condition>())));

    private static readonly bool Yes = true;
    private static readonly bool No = false;

    private static readonly IReadOnlyDictionary<string, Expression<Func<Ledger, bool>>> Collapses =
        new Dictionary<string, Expression<Func<Ledger, bool>>>
        {
            ["yes && Amount == Optional"] = x => Yes && x.Amount == x.Optional,
            ["Amount == Optional || no"] = x => x.Amount == x.Optional || No,
            ["yes && Amount < Hits"] = x => Yes && x.Amount < x.Hits,
            ["Amount + 0 < Optional && yes"] = x => x.Amount + 0m < x.Optional && Yes,
            ["!(Amount + 0 == Optional && yes)"] = x => !(x.Amount + 0m == x.Optional && Yes),
            ["no || (yes && Amount > Hits)"] = x => No || (Yes && x.Amount > x.Hits),
        };

    public static IEnumerable<object[]> CollapseNames => Collapses.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(CollapseNames))]
    public async Task A_collapsed_column_versus_column_comparison_still_compares_two_columns(string shape)
    {
        var filter = Collapses[shape];

        var fields = Flatten(DataBase.ParseConditionExpression(filter)).Where(c => c.IsField).ToList();
        fields.Should().NotBeEmpty("the comparison reads two columns");
        fields.Should().OnlyContain(c => c.Values!.Cast<object>().Single() is string,
            "the right side of a column comparison is a column, rendered verbatim");
        fields.Select(c => (string)c.Values!.Cast<object>().Single()).Should()
            .OnlyContain(v => v.StartsWith("T858Ledger."), "a collapse keeps IsField on the leaf it moves");

        var (store, seed) = await Seeded();
        Ids(await store.ReadAsync(filter)).Should().Equal(Ids(seed.Where(filter.Compile())), $"'{shape}' must match the rows C# matches");
    }

    [Fact]
    public async Task The_diagnostic_text_is_the_statement_SQLite_ran_with_its_decimal_rendering()
    {
        var (store, seed) = await Seeded();
        var executed = new List<string>();
        OnExecute record = executed.Add;
        store.Connector!.OnExecute += record;
        try
        {
            (await store.CountAsync(x => x.Amount + 0m == 10.10m)).Should().Be(2);
            await store.DeleteAsync(x => x.Amount + 0m == 10.10m);
        }
        finally
        {
            store.Connector!.OnExecute -= record;
        }

        executed.Should().HaveCount(2);
        executed.Should().OnlyContain(text => text.Contains("birko_decimal_add(") && text.Contains("COLLATE BIRKO_DECIMAL"),
            "the diagnostic is the SQL SQLite ran, not the portable (Amount + 0)");
        executed.Should().OnlyContain(text => text.Contains("= 10.10") && !text.Contains("@WHERE"), "the values are substituted");
        executed.Should().NotContain(text => text.Contains("Amount + 0)"));
        (await store.CountAsync(x => x.Name != null)).Should().Be(seed.Count - 2);
    }

    private static int _evaluations;

    private static decimal Zero()
    {
        _evaluations++;
        return _evaluations == 1 ? 0m : 100m;
    }

    /// <summary>A connector that splits an IN past 10 values, so the fragment is rendered once per chunk.</summary>
    private sealed class ChunkingConnector : SqLiteConnector
    {
        public ChunkingConnector(Birko.Configuration.PasswordSettings settings) : base(settings) { }
        public override int MaxCommandParameters => 40;
        public override int PreferredInListChunkSize => 10;
    }

    private sealed class ChunkingStore : AsyncSQLiteStore<Ledger>
    {
        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = new ChunkingConnector(settings);
        }
    }

    [Fact]
    public async Task A_side_effecting_call_inside_a_fragment_runs_once_across_the_chunks_of_a_split_list()
    {
        await Seeded();
        var store = new ChunkingStore();
        store.SetSettings(new SqLiteSettings(_root, "t858.db"));
        var executed = new List<string>();
        store.Connector!.OnExecute += executed.Add;
        var many = Enumerable.Range(1, 60).Select(i => i + 0.5m).Concat(new[] { 10.10m, 0.30m }).ToArray();

        _evaluations = 0;
        var read = (await store.ReadAsync(x => many.Contains(x.Amount + Zero()))).ToList();

        _evaluations.Should().Be(1, "the call is evaluated once, at parse time; a second evaluation would answer 100");
        executed.Should().HaveCountGreaterThan(1, "the list is split");
        read.Select(x => x.Amount).Should().BeEquivalentTo(new[] { 10.10m, 10.1m, 0.30m, 9.5m });
    }
}
