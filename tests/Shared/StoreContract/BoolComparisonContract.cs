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

/// <summary>The row TASK-863's contract reads: a <c>bool</c> and a <c>bool?</c> column, nullable ints and a string.</summary>
[Table("T863BoolRows")]
public class BoolComparisonRow : AbstractModel
{
    public string? Code { get; set; }

    public bool Flag { get; set; }

    public bool? Opt { get; set; }

    public int? Score { get; set; }

    public int? Peer { get; set; }

    public string? Name { get; set; }

    public int Amount { get; set; }
}

/// <summary>
/// TASK-863 — a boolean compared with a constant (<c>e == false</c>, <c>e != true</c>, <c>true == e</c>) over
/// <c>HasValue</c>, a comparison, a group, a translated method call, a <c>bool</c> and a <c>bool?</c> column, and
/// <c>.Value</c> on a <c>bool?</c>, reads, counts, deletes and updates exactly the rows <c>Compile()</c> matches.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): <c>x.Score.HasValue == false</c> rendered
/// <c>Score IS NOT NULL</c> and <c>(x.Score == x.Peer) == false</c> the equality itself, both inverted, so a Delete
/// removed exactly the rows the caller meant to keep; <c>x.Opt.HasValue &amp;&amp; x.Opt.Value</c> rendered <c>Opt = NULL</c>
/// and matched nothing. See the task for the full table.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class BoolComparisonContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="BoolComparisonRow"/>.</summary>
    protected abstract IAsyncBulkStore<BoolComparisonRow> NewStore();

    private static readonly int?[] Five = { 5 };
    private static readonly bool Yes = true;
    private static readonly bool No = false;
    private static readonly bool? NoOpt = null;

    public static readonly IReadOnlyDictionary<string, Expression<Func<BoolComparisonRow, bool>>> Shapes =
        new Dictionary<string, Expression<Func<BoolComparisonRow, bool>>>
        {
            // HasValue against a constant, both sides
            ["Score.HasValue == true"] = x => x.Score.HasValue == true,
            ["Score.HasValue == false"] = x => x.Score.HasValue == false,
            ["Score.HasValue != true"] = x => x.Score.HasValue != true,
            ["Score.HasValue != false"] = x => x.Score.HasValue != false,
            ["true == Score.HasValue"] = x => true == x.Score.HasValue,
            ["false == Score.HasValue"] = x => false == x.Score.HasValue,
            ["false != Score.HasValue"] = x => false != x.Score.HasValue,
            ["Score.HasValue == no (closure)"] = x => x.Score.HasValue == No,
            ["!(Score.HasValue == false)"] = x => !(x.Score.HasValue == false),
            ["Score.HasValue == false && Amount > 1"] = x => x.Score.HasValue == false && x.Amount > 1,
            ["Score.HasValue == false || Amount > 5"] = x => x.Score.HasValue == false || x.Amount > 5,

            // a comparison against a constant
            ["(Score == Peer) == false"] = x => (x.Score == x.Peer) == false,
            ["(Score == Peer) == true"] = x => (x.Score == x.Peer) == true,
            ["(Score == Peer) != true"] = x => (x.Score == x.Peer) != true,
            ["(Score == Peer) != false"] = x => (x.Score == x.Peer) != false,
            ["false == (Score == Peer)"] = x => false == (x.Score == x.Peer),
            ["(Score == 5) == false"] = x => (x.Score == 5) == false,
            ["(Score == 5) != true"] = x => (x.Score == 5) != true,
            ["(Score == 5) == true"] = x => (x.Score == 5) == true,
            ["(Score != 5) == false"] = x => (x.Score != 5) == false,
            ["(Score > 5) == false"] = x => (x.Score > 5) == false,
            ["(Score < Peer) == false"] = x => (x.Score < x.Peer) == false,
            ["(Amount > 2) == false"] = x => (x.Amount > 2) == false,
            ["(Amount > 2) != false"] = x => (x.Amount > 2) != false,
            ["(Amount > 2) == yes (closure)"] = x => (x.Amount > 2) == Yes,
            ["(Score == null) == false"] = x => (x.Score == null) == false,

            // a group against a constant
            ["(Score == 5 && Amount > 1) == false"] = x => (x.Score == 5 && x.Amount > 1) == false,
            ["(Score == 5 || Amount > 4) == true"] = x => (x.Score == 5 || x.Amount > 4) == true,
            ["(Score == 5 || Amount > 4) != true"] = x => (x.Score == 5 || x.Amount > 4) != true,
            ["!((Score > 5) == false)"] = x => !((x.Score > 5) == false),

            // a translated method call against a constant
            ["Name.StartsWith(a) == false"] = x => x.Name!.StartsWith("a") == false,
            ["Name.StartsWith(a) == true"] = x => x.Name!.StartsWith("a") == true,
            ["Name.Contains(b) != true"] = x => x.Name!.Contains("b") != true,
            ["{5}.Contains(Score) == false"] = x => Five.Contains(x.Score) == false,
            ["{5}.Contains(Score) != false"] = x => Five.Contains(x.Score) != false,

            // a bool column
            ["Flag"] = x => x.Flag,
            ["!Flag"] = x => !x.Flag,
            ["Flag == true"] = x => x.Flag == true,
            ["Flag == false"] = x => x.Flag == false,
            ["Flag != true"] = x => x.Flag != true,
            ["Flag != false"] = x => x.Flag != false,
            ["true == Flag"] = x => true == x.Flag,
            ["false != Flag"] = x => false != x.Flag,
            ["Flag == no (closure)"] = x => x.Flag == No,
            ["!(Flag == true)"] = x => !(x.Flag == true),
            ["Flag && Amount > 2"] = x => x.Flag && x.Amount > 2,
            ["!Flag || Score == 5"] = x => !x.Flag || x.Score == 5,
            ["Flag == (Amount > 2)"] = x => x.Flag == (x.Amount > 2),

            // a bool? column
            ["Opt == true"] = x => x.Opt == true,
            ["Opt == false"] = x => x.Opt == false,
            ["Opt != true"] = x => x.Opt != true,
            ["Opt != false"] = x => x.Opt != false,
            ["true == Opt"] = x => true == x.Opt,
            ["false != Opt"] = x => false != x.Opt,
            ["Opt == null"] = x => x.Opt == null,
            ["Opt != null"] = x => x.Opt != null,
            ["Opt == noOpt (null closure)"] = x => x.Opt == NoOpt,
            ["!(Opt == true)"] = x => !(x.Opt == true),
            ["!(Opt != false)"] = x => !(x.Opt != false),
            ["Opt.HasValue == false"] = x => x.Opt.HasValue == false,
            ["Opt ?? false"] = x => x.Opt ?? false,
            ["Opt ?? true"] = x => x.Opt ?? true,
            ["!(Opt ?? false)"] = x => !(x.Opt ?? false),
            ["(Opt ?? false) == false"] = x => (x.Opt ?? false) == false,
            ["Opt == Flag"] = x => x.Opt == x.Flag,
            ["Opt != Flag"] = x => x.Opt != x.Flag,

            // .Value on a bool?, guarded so the C# oracle never reads a null
            ["Opt.HasValue && Opt.Value"] = x => x.Opt.HasValue && x.Opt.Value,
            ["Opt.HasValue && !Opt.Value"] = x => x.Opt.HasValue && !x.Opt.Value,
            ["Opt.HasValue && Opt.Value == false"] = x => x.Opt.HasValue && x.Opt.Value == false,
            ["Opt.HasValue && Opt.Value != true"] = x => x.Opt.HasValue && x.Opt.Value != true,
            ["Opt.HasValue && Opt.Value == true"] = x => x.Opt.HasValue && x.Opt.Value == true,
            ["!(Opt.HasValue && Opt.Value)"] = x => !(x.Opt.HasValue && x.Opt.Value),
            ["Opt.HasValue == true && Opt.Value"] = x => x.Opt.HasValue == true && x.Opt.Value,
            ["!Opt.HasValue || Opt.Value"] = x => !x.Opt.HasValue || x.Opt.Value,
            ["Opt != null && Opt.Value"] = x => x.Opt != null && x.Opt.Value,
            ["Opt.HasValue && Opt.Value != false"] = x => x.Opt.HasValue && x.Opt.Value != false,
            ["Opt.HasValue && !(Opt.Value || Amount > 5)"] = x => x.Opt.HasValue && !(x.Opt.Value || x.Amount > 5),
            ["Opt.HasValue && Opt.Value == Flag"] = x => x.Opt.HasValue && x.Opt.Value == x.Flag,

            // a predicate compared with a predicate
            ["(Score == 5) == (Peer == 5)"] = x => (x.Score == 5) == (x.Peer == 5),
            ["(Score == 5) != (Peer == 5)"] = x => (x.Score == 5) != (x.Peer == 5),
            ["Score.HasValue == Flag"] = x => x.Score.HasValue == x.Flag,
            ["Score.HasValue != Peer.HasValue"] = x => x.Score.HasValue != x.Peer.HasValue,
            ["Flag != (Amount > 2)"] = x => x.Flag != (x.Amount > 2),
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private static IEnumerable<BoolComparisonRow> SeedRows()
    {
        // Flag crossed with Opt's three states, Score NULL / 5 / 8 and Peer NULL / equal / different.
        yield return new BoolComparisonRow { Code = "r0", Flag = false, Opt = null, Score = null, Peer = null, Name = "c", Amount = 1 };
        yield return new BoolComparisonRow { Code = "r1", Flag = true, Opt = true, Score = 5, Peer = null, Name = "a", Amount = 2 };
        yield return new BoolComparisonRow { Code = "r2", Flag = false, Opt = false, Score = 8, Peer = 8, Name = "b", Amount = 3 };
        yield return new BoolComparisonRow { Code = "r3", Flag = true, Opt = null, Score = null, Peer = 5, Name = "ab", Amount = 4 };
        yield return new BoolComparisonRow { Code = "r4", Flag = false, Opt = true, Score = 5, Peer = 5, Name = "b", Amount = 5 };
        yield return new BoolComparisonRow { Code = "r5", Flag = true, Opt = false, Score = 8, Peer = 5, Name = "a", Amount = 6 };
    }

    private async Task<(IAsyncBulkStore<BoolComparisonRow> Store, List<BoolComparisonRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = new List<BoolComparisonRow>();
        foreach (var row in SeedRows())
        {
            row.Guid = Guid.NewGuid();
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static List<string?> Codes(IEnumerable<BoolComparisonRow> rows)
        => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task Contract_A_boolean_compared_with_a_constant_reads_and_counts_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Shapes[shape];
        var expected = Codes(seed.Where(filter.Compile()));

        Codes(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected, $"'{shape}' must read the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task Contract_A_boolean_compared_with_a_constant_deletes_and_updates_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var filter = Shapes[shape];

        var (store, seed) = await Seeded();
        var matched = Codes(seed.Where(filter.Compile()));
        var kept = Codes(seed.Where(x => !matched.Contains(x.Code)));
        await store.DeleteAsync(filter);
        Codes(await store.ReadAsync(x => x.Code != null, null, null, null)).Should().Equal(kept, $"'{shape}' must delete only the rows C# matches");

        (store, seed) = await Seeded();
        await store.UpdateAsync(filter, new PropertyUpdate<BoolComparisonRow>().Set(x => x.Amount, 100));
        Codes(await store.ReadAsync(x => x.Amount == 100, null, null, null)).Should().Equal(matched, $"'{shape}' must update only the rows C# matches");
    }
}
