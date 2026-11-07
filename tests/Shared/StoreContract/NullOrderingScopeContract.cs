using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// The shape every <see cref="NullOrderingScopeContract{T}"/> filter is written against. A provider's row type has the
/// same four properties by name; <see cref="NullOrderingScopeContract{T}.Retarget"/> rebinds each filter onto it.
/// </summary>
public sealed class NullOrderingShape
{
    public string? Code { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public int Amount { get; set; }
}

/// <summary>
/// TASK-865 — on the non-SQL stores, a destructive filter that means every row because an ORDERING against null sits
/// under a NOT is refused. In C# a lifted <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> with a null operand is false, so
/// <c>!(x.Score &gt; nul)</c> is true of every row. <c>==</c> / <c>!=</c> null are ordinary filters and stay allowed.
/// <para>
/// Before (measured on InMemory, JSON, XML, MongoDB, RavenDB, Cosmos DB and Elasticsearch): <c>PredicateScope</c> reduced
/// the empty-<c>Contains</c> group but not a null ordering, so Delete and both Updates wrote every row and threw nothing.
/// TASK-861's <c>AlwaysFalseGroupContract</c> is the SQL side.
/// </para>
/// <para>
/// Shared source, linked into each provider's test project, which subclasses it with fresh, empty stores of a row type
/// holding <c>string? Code</c>, <c>int A</c>, <c>int? Score</c> and <c>int Amount</c>.
/// </para>
/// </summary>
public abstract class NullOrderingScopeContract<T> where T : AbstractModel, new()
{
    protected virtual bool Ready() => true;

    /// <summary>Makes earlier writes visible to a read (Elasticsearch refreshes); nothing by default.</summary>
    protected virtual void Settle() { }

    /// <summary>
    /// A measured, unfixed translation gap: the backend does not run <paramref name="shape"/> as C# does (it refuses it or
    /// matches other rows). Returns the reason, or null. For a gap the constraining test is INVERTED: it asserts the backend
    /// still diverges, so the day the translation is fixed the test fails and the entry is removed.
    /// </summary>
    protected virtual string? KnownTranslationGap(string shape) => null;

    /// <summary>A fresh, empty async store.</summary>
    protected abstract IAsyncBulkStore<T> NewAsyncStore();

    /// <summary>A fresh, empty sync store.</summary>
    protected abstract IBulkStore<T> NewSyncStore();

    private static readonly int? Nul = null;

    /// <summary>Filters that mean every row: a destructive statement refuses them.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<NullOrderingShape, bool>>> EveryRow =
        new Dictionary<string, Expression<Func<NullOrderingShape, bool>>>
        {
            ["!(Score > nul)"] = x => !(x.Score > Nul),
            ["!(Score > nul && A > 3)"] = x => !(x.Score > Nul && x.A > 3),
            ["!(A > 0 && (Amount > 1 && Score >= nul))"] = x => !(x.A > 0 && (x.Amount > 1 && x.Score >= Nul)),
            ["A > 3 || !(Score < nul && Amount > 1)"] = x => x.A > 3 || !(x.Score < Nul && x.Amount > 1),
            ["!(nul < Score)"] = x => !(Nul < x.Score),
            ["!(Score <= (int?)null)"] = x => !(x.Score <= (int?)null),
            ["!(A > nul)"] = x => !(x.A > Nul),
            ["!(Score > nul || Score < nul)"] = x => !(x.Score > Nul || x.Score < Nul),
            ["!(!(!(Score > nul && A > 0)) && A > 1)"] = x => !(!(!(x.Score > Nul && x.A > 0)) && x.A > 1),
        };

    /// <summary>Filters that constrain: each operation runs and touches exactly the rows C# matches.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<NullOrderingShape, bool>>> Constraining =
        new Dictionary<string, Expression<Func<NullOrderingShape, bool>>>
        {
            ["Score != nul"] = x => x.Score != Nul,
            ["Score == nul"] = x => x.Score == Nul,
            ["!(Score > nul || A > 3)"] = x => !(x.Score > Nul || x.A > 3),
            ["Score > nul || A > 4"] = x => x.Score > Nul || x.A > 4,
            ["!(Score > nul) && A > 4"] = x => !(x.Score > Nul) && x.A > 4,
        };

    public static readonly string[] Operations =
    {
        "DeleteAsync", "UpdateAsync(PropertyUpdate)", "UpdateAsync(Action)",
        "Delete", "Update(PropertyUpdate)", "Update(Action)",
    };

    public static IEnumerable<object[]> EveryRowCases
        => from shape in EveryRow.Keys from op in Operations select new object[] { shape, op };

    public static IEnumerable<object[]> ConstrainingCases
        => from shape in Constraining.Keys from op in Operations select new object[] { shape, op };

    private static IEnumerable<NullOrderingShape> SeedShapes()
    {
        yield return new NullOrderingShape { Code = "r1", A = 1, Score = null, Amount = 1 };
        yield return new NullOrderingShape { Code = "r2", A = 2, Score = 5, Amount = 2 };
        yield return new NullOrderingShape { Code = "r3", A = 3, Score = null, Amount = 3 };
        yield return new NullOrderingShape { Code = "r4", A = 4, Score = 8, Amount = 4 };
        yield return new NullOrderingShape { Code = "r5", A = 5, Score = null, Amount = 5 };
        yield return new NullOrderingShape { Code = "r6", A = 6, Score = 2, Amount = 6 };
    }

    private static T ToRow(NullOrderingShape shape)
    {
        var row = new T();
        foreach (var source in typeof(NullOrderingShape).GetProperties())
            typeof(T).GetProperty(source.Name)!.SetValue(row, source.GetValue(shape));
        return row;
    }

    private static string? CodeOf(T row) => (string?)typeof(T).GetProperty(nameof(NullOrderingShape.Code))!.GetValue(row);

    private static int AmountOf(T row) => (int)typeof(T).GetProperty(nameof(NullOrderingShape.Amount))!.GetValue(row)!;

    private static List<string?> Codes(IEnumerable<T> rows)
        => rows.Select(CodeOf).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static List<string?> SeedCodes() => SeedShapes().Select(x => x.Code).ToList();

    /// <summary>Rebinds a filter written on <see cref="NullOrderingShape"/> onto <typeparamref name="T"/>, member by name;
    /// captured values stay as written, so the tree is the one a caller writing it on <typeparamref name="T"/> produces.</summary>
    public static Expression<Func<T, bool>> Retarget(Expression<Func<NullOrderingShape, bool>> filter)
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

    private static readonly PropertyUpdate<T> SetMarker = new PropertyUpdate<T>().Set(AmountSelector(), 100);

    private static Expression<Func<T, int>> AmountSelector()
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, int>>(Expression.Property(parameter, nameof(NullOrderingShape.Amount)), parameter);
    }

    private static void MarkAction(T row) => typeof(T).GetProperty(nameof(NullOrderingShape.Amount))!.SetValue(row, 100);

    private static readonly Expression<Func<T, bool>> HasCode = Retarget(x => x.Code != null);

    /// <summary>Runs <paramref name="operation"/> with <paramref name="filter"/> on a freshly seeded store; returns the
    /// exception it threw (or null), the codes left and the codes marked by an update.</summary>
    private async Task<(Exception? Thrown, List<string?> Left, List<string?> Marked)> Run(
        string operation, Expression<Func<T, bool>> filter)
    {
        Exception? thrown = null;
        if (operation.Contains("Async"))
        {
            var store = NewAsyncStore();
            foreach (var shape in SeedShapes()) await store.CreateAsync(ToRow(shape));
            Settle();
            try
            {
                switch (operation)
                {
                    case "DeleteAsync": await store.DeleteAsync(filter); break;
                    case "UpdateAsync(PropertyUpdate)": await store.UpdateAsync(filter, SetMarker); break;
                    case "UpdateAsync(Action)": await store.UpdateAsync(filter, MarkAction); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
            }
            catch (Exception ex) { thrown = ex; }
            Settle();
            var rows = (await store.ReadAsync(HasCode, null, null, null)).ToList();
            return (thrown, Codes(rows), Codes(rows.Where(x => AmountOf(x) == 100)));
        }
        else
        {
            var store = NewSyncStore();
            foreach (var shape in SeedShapes()) store.Create(ToRow(shape));
            Settle();
            try
            {
                switch (operation)
                {
                    case "Delete": store.Delete(filter); break;
                    case "Update(PropertyUpdate)": store.Update(filter, SetMarker); break;
                    case "Update(Action)": store.Update(filter, MarkAction); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                }
            }
            catch (Exception ex) { thrown = ex; }
            Settle();
            var rows = store.Read(HasCode, null, null, null).ToList();
            return (thrown, Codes(rows), Codes(rows.Where(x => AmountOf(x) == 100)));
        }
    }

    [Theory]
    [MemberData(nameof(EveryRowCases))]
    public async Task Contract_A_destructive_filter_negating_a_null_ordering_is_refused(string shape, string operation)
    {
        if (!Ready()) return;
        var filter = EveryRow[shape];
        SeedShapes().All(filter.Compile()).Should().BeTrue($"'{shape}' must mean every row in C#");

        var (thrown, left, marked) = await Run(operation, Retarget(filter));

        var seed = SeedCodes();
        var because = $"{operation} with '{shape}' means every row (deleted {seed.Count - left.Count} of {seed.Count}, "
                    + $"updated {marked.Count} of {seed.Count}, thrown {thrown?.GetType().Name ?? "NONE"})";
        thrown.Should().BeOfType<WholeTableWriteException>(because);
        left.Should().Equal(seed, because);
        marked.Should().BeEmpty(because);
    }

    [Theory]
    [MemberData(nameof(ConstrainingCases))]
    public async Task Contract_A_filter_with_a_null_comparison_that_constrains_writes_only_what_CSharp_matches(string shape, string operation)
    {
        if (!Ready()) return;
        var filter = Constraining[shape];
        var seed = SeedCodes();
        var matched = SeedShapes().Where(filter.Compile()).Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
        matched.Should().NotBeEmpty().And.NotEqual(seed, $"'{shape}' must constrain in C#");

        var (thrown, left, marked) = await Run(operation, Retarget(filter));

        var expectedLeft = operation.StartsWith("Delete") ? seed.Except(matched).ToList() : seed;
        var expectedMarked = operation.StartsWith("Delete") ? new List<string?>() : matched;
        var gap = KnownTranslationGap(shape);
        if (gap != null)
        {
            (thrown is WholeTableWriteException).Should().BeFalse($"'{shape}' constrains; the scope guard must not refuse it");
            var diverges = thrown != null || !left.SequenceEqual(expectedLeft) || !marked.SequenceEqual(expectedMarked);
            diverges.Should().BeTrue($"{operation} with '{shape}' was a recorded gap ({gap}) and now matches C#: remove the entry");
            return;
        }

        thrown.Should().BeNull($"{operation} with '{shape}' constrains and must run");
        if (operation.StartsWith("Delete"))
        {
            left.Should().Equal(seed.Except(matched), $"{operation} with '{shape}' must delete only what C# matches");
        }
        else
        {
            left.Should().Equal(seed);
            marked.Should().Equal(matched, $"{operation} with '{shape}' must update only what C# matches");
        }
    }
}
