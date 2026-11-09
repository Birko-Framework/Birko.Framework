using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>The row TASK-866's contract writes: two non-nullable ints, a nullable int and an int the updates mark.</summary>
[Table("T866CollapsedRows")]
public class CollapsedGroupRow : AbstractModel
{
    public string? Code { get; set; }

    public int A { get; set; }

    public int? Score { get; set; }

    public int Amount { get; set; }

    public int Mark { get; set; }
}

/// <summary>
/// TASK-866 — when one operand of <c>&amp;&amp;</c> / <c>||</c> is a constant, the parser collapses the group onto the other
/// operand. When that operand is itself a group, it keeps its own separator.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the collapse wrote the collapsed operator's
/// <c>IsOr</c> over the survivor's, so an OR group joined its children with AND and an AND group with OR.
/// <c>!((x.A == 1 || x.A == 2) &amp;&amp; t)</c> rendered <c>NOT (A = 1 AND A = 2)</c>: the read returned every row and a Delete
/// removed every row, with no exception. Wrong in either direction, on reads, counts, deletes and both updates.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies fresh, empty stores.</para>
/// </summary>
public abstract class CollapsedGroupContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty async store of <see cref="CollapsedGroupRow"/>.</summary>
    protected abstract IAsyncBulkStore<CollapsedGroupRow> NewAsyncStore();

    /// <summary>A fresh, empty sync store of <see cref="CollapsedGroupRow"/>.</summary>
    protected abstract IBulkStore<CollapsedGroupRow> NewSyncStore();

    private static readonly bool T = true;
    private static readonly bool F = false;

    public static readonly IReadOnlyDictionary<string, Expression<Func<CollapsedGroupRow, bool>>> Shapes =
        new Dictionary<string, Expression<Func<CollapsedGroupRow, bool>>>
        {
            // An OR group survives an AND with true; an AND group survives an OR with false.
            ["!((A == 1 || A == 2) && t)"] = x => !((x.A == 1 || x.A == 2) && T),
            ["!(t && (A == 1 || A == 2))"] = x => !(T && (x.A == 1 || x.A == 2)),
            ["A == 6 || ((A == 1 || A == 2) && t)"] = x => x.A == 6 || ((x.A == 1 || x.A == 2) && T),
            ["A >= 1 && ((A <= 3 && Code != r2) || f)"] = x => x.A >= 1 && ((x.A <= 3 && x.Code != "r2") || F),
            ["A >= 1 && (f || (A <= 3 && Code != r2))"] = x => x.A >= 1 && (F || (x.A <= 3 && x.Code != "r2")),
            ["!((A <= 3 && Code != r2) || f)"] = x => !((x.A <= 3 && x.Code != "r2") || F),
            ["!(!(A == 1 || A == 2) && t)"] = x => !(!(x.A == 1 || x.A == 2) && T),
            // Controls: the collapse at the top, with no enclosing node, was always right.
            ["(A == 1 || A == 2) && t"] = x => (x.A == 1 || x.A == 2) && T,
            ["(A <= 3 && Code != r2) || f"] = x => (x.A <= 3 && x.Code != "r2") || F,
            // Nested two levels, and a collapse inside a collapse.
            ["A > 0 && (A < 6 && ((A == 1 || A == 2) && t))"] = x => x.A > 0 && (x.A < 6 && ((x.A == 1 || x.A == 2) && T)),
            ["!(A > 5 || ((A == 1 || A == 2) && t))"] = x => !(x.A > 5 || ((x.A == 1 || x.A == 2) && T)),
            ["A < 6 && !(t && !((A == 1 || A == 4) && t))"] = x => x.A < 6 && !(T && !((x.A == 1 || x.A == 4) && T)),
            // A nullable column: TASK-859's NULL legs are added after the collapse.
            ["!((Score == 5 || Score > 6) && t)"] = x => !((x.Score == 5 || x.Score > 6) && T),
            ["A > 0 && ((Score != 5 && Score != 2) || f)"] = x => x.A > 0 && ((x.Score != 5 && x.Score != 2) || F),
            // Value fragments and a column against a column.
            ["!(((Score ?? 0) + A > 9 || A * 2 == 4) && t)"] = x => !(((x.Score ?? 0) + x.A > 9 || x.A * 2 == 4) && T),
            ["A >= 1 && ((A == Amount && Score != null) || f)"] = x => x.A >= 1 && ((x.A == x.Amount && x.Score != null) || F),
        };

    public static readonly string[] Operations =
    {
        "DeleteAsync", "UpdateAsync(PropertyUpdate)", "UpdateAsync(Action)",
        "Delete", "Update(PropertyUpdate)", "Update(Action)",
    };

    public static IEnumerable<object[]> WriteCases
        => from shape in Shapes.Keys from op in Operations select new object[] { shape, op };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private static IEnumerable<CollapsedGroupRow> SeedRows()
    {
        yield return new CollapsedGroupRow { Code = "r1", A = 1, Score = null, Amount = 1 };
        yield return new CollapsedGroupRow { Code = "r2", A = 2, Score = 5, Amount = 2 };
        yield return new CollapsedGroupRow { Code = "r3", A = 3, Score = null, Amount = 3 };
        yield return new CollapsedGroupRow { Code = "r4", A = 4, Score = 8, Amount = 2 };
        yield return new CollapsedGroupRow { Code = "r5", A = 5, Score = null, Amount = 5 };
        yield return new CollapsedGroupRow { Code = "r6", A = 6, Score = 2, Amount = 1 };
    }

    private static List<CollapsedGroupRow> FreshSeed()
    {
        var seed = SeedRows().ToList();
        foreach (var row in seed) row.Guid = Guid.NewGuid();
        return seed;
    }

    private static List<string?> Codes(IEnumerable<CollapsedGroupRow> rows)
        => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static readonly PropertyUpdate<CollapsedGroupRow> SetMarker = new PropertyUpdate<CollapsedGroupRow>().Set(x => x.Mark, 100);

    private static void MarkAction(CollapsedGroupRow row) => row.Mark = 100;

    private async Task<(Exception? Thrown, List<string?> Left, List<string?> Marked, List<string?> Seed)> Run(
        string operation, Expression<Func<CollapsedGroupRow, bool>> filter)
    {
        var seed = FreshSeed();
        Exception? thrown = null;
        if (operation.Contains("Async"))
        {
            var store = NewAsyncStore();
            foreach (var row in seed) await store.CreateAsync(row);
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
            var rows = (await store.ReadAsync(x => x.Code != null, null, null, null)).ToList();
            return (thrown, Codes(rows), Codes(rows.Where(x => x.Mark == 100)), Codes(seed));
        }
        else
        {
            var store = NewSyncStore();
            foreach (var row in seed) store.Create(row);
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
            var rows = store.Read(x => x.Code != null, null, null, null).ToList();
            return (thrown, Codes(rows), Codes(rows.Where(x => x.Mark == 100)), Codes(seed));
        }
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task Contract_A_collapsed_group_reads_and_counts_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var seed = FreshSeed();
        var filter = Shapes[shape];
        var expected = Codes(seed.Where(filter.Compile()));
        expected.Should().NotBeEmpty().And.NotEqual(Codes(seed), $"'{shape}' must constrain in C#");

        var store = NewAsyncStore();
        foreach (var row in seed) await store.CreateAsync(row);
        Codes(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected, shape);
        (await store.CountAsync(filter)).Should().Be(expected.Count, shape);

        var sync = NewSyncStore();
        foreach (var row in FreshSeed()) sync.Create(row);
        Codes(sync.Read(filter, null, null, null)).Should().Equal(expected, shape);
        sync.Count(filter).Should().Be(expected.Count, shape);
    }

    [Theory]
    [MemberData(nameof(WriteCases))]
    public async Task Contract_A_collapsed_group_writes_only_what_CSharp_matches(string shape, string operation)
    {
        if (!Ready()) return;
        var filter = Shapes[shape];
        var (thrown, left, marked, seed) = await Run(operation, filter);
        var matched = Codes(SeedRows().Where(filter.Compile()));

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
