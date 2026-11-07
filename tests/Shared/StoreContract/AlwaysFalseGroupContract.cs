using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>The row TASK-861's contract writes: a non-nullable int, a nullable int and a second non-nullable int.</summary>
[Table("T861FalseRows")]
public class AlwaysFalseRow : AbstractModel
{
    public string? Code { get; set; }

    public int A { get; set; }

    public int? Score { get; set; }

    public int Amount { get; set; }
}

/// <summary>
/// TASK-861 — a filter whose meaning is "every row" because an always-false term sits under a NOT is refused by a
/// destructive statement, however deep the term is; a read returns every row. A filter that only <i>contains</i> such a
/// term but still constrains reads, deletes and updates exactly the rows C# matches.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): <c>!(empty.Contains(x.A) &amp;&amp; x.A &gt; 0)</c> rendered
/// <c>NOT (1 = 0 AND …)</c> and <c>!(x.Score &gt; nul &amp;&amp; …)</c> folded to the same, so Delete and Update wrote every row and
/// threw nothing. The whole-table guard reduced always-true terms but never an always-false one.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies fresh, empty stores.</para>
/// </summary>
public abstract class AlwaysFalseGroupContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty async store of <see cref="AlwaysFalseRow"/>.</summary>
    protected abstract IAsyncBulkStore<AlwaysFalseRow> NewAsyncStore();

    /// <summary>A fresh, empty sync store of <see cref="AlwaysFalseRow"/>.</summary>
    protected abstract IBulkStore<AlwaysFalseRow> NewSyncStore();

    private static readonly int[] Empty = Array.Empty<int>();
    private static readonly int? Nul = null;
    private static readonly bool Off = false;

    /// <summary>Filters that mean every row: a destructive statement refuses them, a read returns every row.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<AlwaysFalseRow, bool>>> EveryRow =
        new Dictionary<string, Expression<Func<AlwaysFalseRow, bool>>>
        {
            ["!(empty.Contains(A) && A > 0)"] = x => !(Empty.Contains(x.A) && x.A > 0),
            ["!(Score > nul && Amount > 3)"] = x => !(x.Score > Nul && x.Amount > 3),
            ["!(A > 0 && empty.Contains(A))"] = x => !(x.A > 0 && Empty.Contains(x.A)),
            ["!(empty.Contains(A))"] = x => !Empty.Contains(x.A),
            ["!(Score > nul)"] = x => !(x.Score > Nul),
            ["!(A > 0 && (Amount > 1 && empty.Contains(A)))"] = x => !(x.A > 0 && (x.Amount > 1 && Empty.Contains(x.A))),
            ["!(A > 0 && (Amount > 1 && Score > nul))"] = x => !(x.A > 0 && (x.Amount > 1 && x.Score > Nul)),
            ["A > 3 || !(empty.Contains(A) && Amount > 1)"] = x => x.A > 3 || !(Empty.Contains(x.A) && x.Amount > 1),
            ["A > 3 || !(Score < nul && Amount > 1)"] = x => x.A > 3 || !(x.Score < Nul && x.Amount > 1),
            ["!(empty.Contains(A) || Score > nul)"] = x => !(Empty.Contains(x.A) || x.Score > Nul),
            ["!(empty.Contains(A) && A > 0) && !(Score > nul && A > 1)"] = x => !(Empty.Contains(x.A) && x.A > 0) && !(x.Score > Nul && x.A > 1),
            ["!(A > 0 && off)"] = x => !(x.A > 0 && Off),
            ["!(!(!(empty.Contains(A) && A > 0)) && A > 1)"] = x => !(!(!(Empty.Contains(x.A) && x.A > 0)) && x.A > 1),
        };

    /// <summary>Filters that hold an always-false term but still constrain: every operation affects what C# matches.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<AlwaysFalseRow, bool>>> Constraining =
        new Dictionary<string, Expression<Func<AlwaysFalseRow, bool>>>
        {
            ["!(A > 3 || empty.Contains(A))"] = x => !(x.A > 3 || Empty.Contains(x.A)),
            ["!(A > 3 || Score > nul)"] = x => !(x.A > 3 || x.Score > Nul),
            ["empty.Contains(A) || A > 4"] = x => Empty.Contains(x.A) || x.A > 4,
            ["Score > nul || A > 4"] = x => x.Score > Nul || x.A > 4,
            ["!(empty.Contains(A) && A > 0) && A > 4"] = x => !(Empty.Contains(x.A) && x.A > 0) && x.A > 4,
            ["!(A > 3 && !empty.Contains(A))"] = x => !(x.A > 3 && !Empty.Contains(x.A)),
            ["A > 4 || (empty.Contains(A) && A > 0)"] = x => x.A > 4 || (Empty.Contains(x.A) && x.A > 0),
            ["!(A > 2 && (Amount > 1 || empty.Contains(A)))"] = x => !(x.A > 2 && (x.Amount > 1 || Empty.Contains(x.A))),
            ["!(empty.Contains(A) && A > 0) && (A < 2 || Score > nul)"] = x => !(Empty.Contains(x.A) && x.A > 0) && (x.A < 2 || x.Score > Nul),
            ["!(!(empty.Contains(A) && A > 0)) || A < 3"] = x => !(!(Empty.Contains(x.A) && x.A > 0)) || x.A < 3,
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

    public static IEnumerable<object[]> EveryRowNames => EveryRow.Keys.Select(k => new object[] { k });

    public static IEnumerable<object[]> ConstrainingNames => Constraining.Keys.Select(k => new object[] { k });

    private static IEnumerable<AlwaysFalseRow> SeedRows()
    {
        yield return new AlwaysFalseRow { Code = "r1", A = 1, Score = null, Amount = 1 };
        yield return new AlwaysFalseRow { Code = "r2", A = 2, Score = 5, Amount = 2 };
        yield return new AlwaysFalseRow { Code = "r3", A = 3, Score = null, Amount = 3 };
        yield return new AlwaysFalseRow { Code = "r4", A = 4, Score = 8, Amount = 4 };
        yield return new AlwaysFalseRow { Code = "r5", A = 5, Score = null, Amount = 5 };
        yield return new AlwaysFalseRow { Code = "r6", A = 6, Score = 2, Amount = 6 };
    }

    private static List<AlwaysFalseRow> FreshSeed()
    {
        var seed = SeedRows().ToList();
        foreach (var row in seed) row.Guid = Guid.NewGuid();
        return seed;
    }

    private static List<string?> Codes(IEnumerable<AlwaysFalseRow> rows)
        => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static readonly PropertyUpdate<AlwaysFalseRow> SetMarker = new PropertyUpdate<AlwaysFalseRow>().Set(x => x.Amount, 100);

    private static void MarkAction(AlwaysFalseRow row) => row.Amount = 100;

    /// <summary>Runs <paramref name="operation"/> with <paramref name="filter"/> on a freshly seeded store; returns the
    /// exception it threw (or null), the codes left and the codes marked by an update.</summary>
    private async Task<(Exception? Thrown, List<string?> Left, List<string?> Marked, List<string?> Seed)> Run(
        string operation, Expression<Func<AlwaysFalseRow, bool>> filter)
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
            return (thrown, Codes(rows), Codes(rows.Where(x => x.Amount == 100)), Codes(seed));
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
            return (thrown, Codes(rows), Codes(rows.Where(x => x.Amount == 100)), Codes(seed));
        }
    }

    [Theory]
    [MemberData(nameof(EveryRowCases))]
    public async Task Contract_A_destructive_statement_whose_filter_means_every_row_is_refused(string shape, string operation)
    {
        if (!Ready()) return;
        var (thrown, left, marked, seed) = await Run(operation, EveryRow[shape]);

        thrown.Should().BeOfType<WholeTableWriteException>($"{operation} with '{shape}' means every row and must be refused");
        left.Should().Equal(seed, $"{operation} with '{shape}' must delete nothing");
        marked.Should().BeEmpty($"{operation} with '{shape}' must update nothing");
    }

    [Theory]
    [MemberData(nameof(EveryRowNames))]
    public async Task Contract_A_read_whose_filter_means_every_row_returns_every_row(string shape)
    {
        if (!Ready()) return;
        var seed = FreshSeed();
        var filter = EveryRow[shape];
        Codes(seed.Where(filter.Compile())).Should().Equal(Codes(seed), $"'{shape}' must mean every row in C#");

        var store = NewAsyncStore();
        foreach (var row in seed) await store.CreateAsync(row);
        Codes(await store.ReadAsync(filter, null, null, null)).Should().Equal(Codes(seed));
        (await store.CountAsync(filter)).Should().Be(seed.Count);

        var sync = NewSyncStore();
        foreach (var row in FreshSeed()) sync.Create(row);
        Codes(sync.Read(filter, null, null, null)).Should().Equal(Codes(seed));
        sync.Count(filter).Should().Be(seed.Count);
    }

    [Theory]
    [MemberData(nameof(ConstrainingCases))]
    public async Task Contract_A_filter_that_still_constrains_writes_only_what_CSharp_matches(string shape, string operation)
    {
        if (!Ready()) return;
        var filter = Constraining[shape];
        var (thrown, left, marked, seed) = await Run(operation, filter);
        var matched = Codes(SeedRows().Where(filter.Compile()));
        matched.Should().NotBeEmpty().And.NotEqual(seed, $"'{shape}' must constrain in C#");

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

    [Theory]
    [MemberData(nameof(ConstrainingNames))]
    public async Task Contract_A_filter_that_still_constrains_reads_and_counts_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var seed = FreshSeed();
        var filter = Constraining[shape];
        var expected = Codes(seed.Where(filter.Compile()));

        var store = NewAsyncStore();
        foreach (var row in seed) await store.CreateAsync(row);
        Codes(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected);
        (await store.CountAsync(filter)).Should().Be(expected.Count);

        var sync = NewSyncStore();
        foreach (var row in FreshSeed()) sync.Create(row);
        Codes(sync.Read(filter, null, null, null)).Should().Equal(expected);
        sync.Count(filter).Should().Be(expected.Count);
    }
}
