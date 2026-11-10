using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// The shape every <see cref="AggregateEdgeContract{T}"/> row is written as. A provider's row type has the same five
/// properties by name (and may carry more, e.g. a TimescaleDB time column).
/// </summary>
public sealed class AggregateEdgeShape
{
    public string? Code { get; set; }
    public string? Kind { get; set; }
    public int N { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// TASK-886 — the edge answers of a store aggregate, one answer on every provider (the user's decisions, 2026-11-10):
/// <list type="bullet">
/// <item>an UNGROUPED aggregate over no rows answers ONE row: <c>Count</c> 0, <c>Sum</c> 0, <c>Avg</c> / <c>Min</c> /
/// <c>Max</c> null — what SQL answers, and what LINQ's <c>Count()</c> and <c>Sum()</c> answer over an empty sequence;</item>
/// <item>a <c>Sum</c> over only NULLs (or over no rows) is 0, as LINQ's <c>Sum</c> over <c>int?</c> / <c>decimal?</c> is,
/// in the type <see cref="AggregateMath.ResultType"/> names; <c>Avg</c> / <c>Min</c> / <c>Max</c> stay null;</item>
/// <item>a GROUPED aggregate over no rows answers no rows (there is no group);</item>
/// <item>a query with nothing to aggregate (no aggregate, no group-by field, no time bucket) is refused with an
/// <see cref="ArgumentException"/> before anything runs.</item>
/// </list>
/// <para>
/// Before (measured live, sync and async): LINQ (InMemory, JSON, XML, RavenDB), MongoDB and Elasticsearch answered no
/// rows for the empty ungrouped aggregate; every store but SQL views answered NULL for a SUM over only NULLs; an empty
/// query answered raw rows in memory and one empty row on Elasticsearch. See the task file for the full table.
/// </para>
/// <para>
/// Shared source, linked into each provider's test project, which subclasses it with fresh, empty stores of a row type
/// holding <c>string? Code</c>, <c>string? Kind</c>, <c>int N</c>, <c>int? Score</c> and <c>decimal? Price</c>.
/// </para>
/// </summary>
public abstract class AggregateEdgeContract<T> where T : AbstractModel, new()
{
    protected virtual bool Ready() => true;

    /// <summary>Makes earlier writes visible to a read (Elasticsearch refreshes); nothing by default.</summary>
    protected virtual void Settle() { }

    /// <summary>A fresh, empty async store.</summary>
    protected abstract IAsyncBulkStore<T> NewAsyncStore();

    /// <summary>A fresh, empty sync store.</summary>
    protected abstract IBulkStore<T> NewSyncStore();

    private static readonly AggregateField[] Metrics =
    {
        new(AggregateFunction.Count, "Code", "count"),
        new(AggregateFunction.Sum, "N", "sumN"),
        new(AggregateFunction.Sum, "Score", "sumScore"),
        new(AggregateFunction.Sum, "Price", "sumPrice"),
        new(AggregateFunction.Avg, "Score", "avgScore"),
        new(AggregateFunction.Avg, "Price", "avgPrice"),
        new(AggregateFunction.Min, "Score", "minScore"),
        new(AggregateFunction.Max, "Score", "maxScore"),
    };

    private static IEnumerable<AggregateEdgeShape> SeedShapes()
    {
        yield return new AggregateEdgeShape { Code = "r1", Kind = "a", N = 1, Score = null, Price = null };
        yield return new AggregateEdgeShape { Code = "r2", Kind = "a", N = 2, Score = null, Price = null };
        yield return new AggregateEdgeShape { Code = "r3", Kind = "b", N = 3, Score = 5, Price = 1.5m };
        yield return new AggregateEdgeShape { Code = "r4", Kind = "b", N = 4, Score = 7, Price = 2.25m };
        yield return new AggregateEdgeShape { Code = "r5", Kind = "b", N = 5, Score = null, Price = null };
    }

    /// <summary>A row of the expected answer: the group key (or none), then the eight metrics in <see cref="Metrics"/> order.</summary>
    private static Dictionary<string, object?> Row(string? kind, int count, int sumN, int sumScore, decimal sumPrice,
        double? avgScore, decimal? avgPrice, int? minScore, int? maxScore, bool grouped)
    {
        var row = new Dictionary<string, object?>();
        if (grouped) row["Kind"] = kind;
        row["count"] = count;
        row["sumN"] = sumN;
        row["sumScore"] = sumScore;
        row["sumPrice"] = sumPrice;
        row["avgScore"] = avgScore;
        row["avgPrice"] = avgPrice;
        row["minScore"] = minScore;
        row["maxScore"] = maxScore;
        return row;
    }

    private static readonly Dictionary<string, object?> EmptyUngrouped = Row(null, 0, 0, 0, 0m, null, null, null, null, grouped: false);

    private sealed record Case(
        bool Seeded,
        Expression<Func<AggregateEdgeShape, bool>>? Filter,
        bool Grouped,
        IReadOnlyList<Dictionary<string, object?>> Expected);

    private static readonly IReadOnlyDictionary<string, Case> Cases = new Dictionary<string, Case>
    {
        ["ungrouped, empty store"] = new(false, null, false, new[] { EmptyUngrouped }),
        ["ungrouped, no row matches"] = new(true, x => x.N > 100, false, new[] { EmptyUngrouped }),
        ["ungrouped, only NULLs"] = new(true, x => x.Score == null, false,
            new[] { Row(null, 3, 8, 0, 0m, null, null, null, null, grouped: false) }),
        ["grouped, only NULLs in one group"] = new(true, null, true, new[]
        {
            Row("a", 2, 3, 0, 0m, null, null, null, null, grouped: true),
            Row("b", 3, 12, 12, 3.75m, 6d, 1.875m, 5, 7, grouped: true),
        }),
        ["grouped, only NULLs in every group"] = new(true, x => x.Score == null, true, new[]
        {
            Row("a", 2, 3, 0, 0m, null, null, null, null, grouped: true),
            Row("b", 1, 5, 0, 0m, null, null, null, null, grouped: true),
        }),
        ["grouped, no row matches"] = new(true, x => x.N > 100, true, Array.Empty<Dictionary<string, object?>>()),
        ["grouped, empty store"] = new(false, null, true, Array.Empty<Dictionary<string, object?>>()),
    };

    public static IEnumerable<object[]> AnswerCases
        => from shape in Cases.Keys from mode in new[] { "sync", "async" } select new object[] { shape, mode };

    public static IEnumerable<object[]> Modes => new[] { new object[] { "sync" }, new object[] { "async" } };

    private static T ToRow(AggregateEdgeShape shape)
    {
        var row = new T();
        foreach (var source in typeof(AggregateEdgeShape).GetProperties())
            typeof(T).GetProperty(source.Name)!.SetValue(row, source.GetValue(shape));
        return row;
    }

    /// <summary>Rebinds a filter written on <see cref="AggregateEdgeShape"/> onto <typeparamref name="T"/>, member by name.</summary>
    private static Expression<Func<T, bool>> Retarget(Expression<Func<AggregateEdgeShape, bool>> filter)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var body = new Rebinder(filter.Parameters[0], parameter).Visit(filter.Body)!;
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class Rebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public Rebinder(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitMember(MemberExpression node)
            => node.Expression == _from ? Expression.Property(_to, node.Member.Name) : base.VisitMember(node);

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }

    /// <summary>One row as text: each key with its value's CLR type, keys in ordinal order; a decimal without trailing zeros.</summary>
    private static string Render(IReadOnlyDictionary<string, object?> row)
        => string.Join("; ", row.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={RenderValue(kv.Value)}"));

    private static string RenderValue(object? value) => value switch
    {
        null => "null",
        decimal d => "Decimal:" + d.ToString("0.############################", CultureInfo.InvariantCulture),
        double d => "Double:" + d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => value.GetType().Name + ":" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value,
    };

    private static List<string> RenderAll(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static AggregateQuery<T> Query(Case c) => new()
    {
        Filter = c.Filter == null ? null : Retarget(c.Filter),
        GroupByFields = c.Grouped ? new[] { "Kind" } : Array.Empty<string>(),
        Aggregates = Metrics,
    };

    private async Task<IReadOnlyList<AggregateResult>> Run(string mode, bool seeded, AggregateQuery<T> query)
    {
        if (mode == "async")
        {
            var store = NewAsyncStore();
            if (seeded)
            {
                foreach (var shape in SeedShapes()) await store.CreateAsync(ToRow(shape));
                Settle();
            }
            var aggregatable = store as IAsyncAggregatableStore<T>
                ?? throw new InvalidOperationException($"{store.GetType().Name} does not aggregate.");
            return await aggregatable.AggregateAsync(query);
        }

        var sync = NewSyncStore();
        if (seeded)
        {
            foreach (var shape in SeedShapes()) sync.Create(ToRow(shape));
            Settle();
        }
        var syncAggregatable = sync as IAggregatableStore<T>
            ?? throw new InvalidOperationException($"{sync.GetType().Name} does not aggregate.");
        return syncAggregatable.Aggregate(query);
    }

    [Theory]
    [MemberData(nameof(AnswerCases))]
    public async Task Contract_An_aggregate_edge_answers_as_decided(string shape, string mode)
    {
        if (!Ready()) return;
        var c = Cases[shape];

        Exception? thrown = null;
        List<string> got = new();
        try
        {
            var results = await Run(mode, c.Seeded, Query(c));
            got = RenderAll(results.Select(r => r.Values));
        }
        catch (Exception ex) { thrown = ex; }

        var expected = RenderAll(c.Expected);
        var because = $"{mode} aggregate '{shape}' (expected [{string.Join(" | ", expected)}]; got [{string.Join(" | ", got)}]; "
                    + $"thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeNull(because);
        got.Should().Equal(expected, because);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Contract_A_query_with_nothing_to_aggregate_is_refused(string mode)
    {
        if (!Ready()) return;

        Exception? thrown = null;
        List<string> got = new();
        try
        {
            var results = await Run(mode, true, new AggregateQuery<T> { Filter = Retarget(x => x.N > 1) });
            got = RenderAll(results.Select(r => r.Values));
        }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} aggregate with no aggregate and no group (got {got.Count} row(s) [{string.Join(" | ", got)}]; "
                    + $"thrown {thrown?.GetType().Name}: {thrown?.Message})";
        thrown.Should().BeAssignableTo<ArgumentException>(because);
        thrown!.Message.Should().Contain("nothing to aggregate", because);
    }
}
