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

/// <summary>The row TASK-859's contract reads: nullable int, string, Guid and decimal columns, and a non-nullable int.</summary>
[Table("T859NullRows")]
public class NullFilterRow : AbstractModel
{
    public string? Code { get; set; }

    public int? Score { get; set; }

    public int? Peer { get; set; }

    public string? Name { get; set; }

    public Guid? Ref { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal? Optional { get; set; }

    public int Amount { get; set; }
}

/// <summary>
/// TASK-859 — a SQL filter over a nullable column matches the rows the C# expression matches: <c>!=</c> and <c>!Contains</c>
/// include NULL, <c>Contains</c> with <c>null</c> in the list matches NULL, and a negation (<c>!(a == b)</c>) includes the
/// NULL rows the comparison excluded. Read, count, delete and update are each checked against <c>Compile()</c>.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): SQL's three-valued logic made <c>Score &lt;&gt; 5</c>,
/// <c>NOT (Score = 5)</c> and <c>Score NOT IN (…)</c> UNKNOWN on a NULL row, which a WHERE drops, and a list holding
/// <c>null</c> either lost it (no NULL row matched) or bound it (<c>NOT IN (5, NULL)</c> matched no row at all).
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class NullFilterContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="NullFilterRow"/>.</summary>
    protected abstract IAsyncBulkStore<NullFilterRow> NewStore();

    private static readonly Guid G1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid G2 = new("22222222-2222-2222-2222-222222222222");

    private static readonly int?[] FiveOrNull = { null, 5 };
    private static readonly int?[] Five = { 5 };
    private static readonly int?[] OnlyNull = { null };
    private static readonly int[] PlainFive = { 5 };
    private static readonly List<int?> SixOrNull = new() { 6, null };
    private static readonly string?[] AOrNull = { "a", null };
    private static readonly string[] A = { "a" };
    private static readonly Guid?[] G1OrNull = { G1, null };
    private static readonly Guid?[] G1Only = { G1 };
    private static readonly int? NoScore = null;

    public static readonly IReadOnlyDictionary<string, Expression<Func<NullFilterRow, bool>>> Shapes =
        new Dictionary<string, Expression<Func<NullFilterRow, bool>>>
        {
            // != against a constant, and the negated forms of == and the orderings
            ["Score != 5"] = x => x.Score != 5,
            ["5 != Score"] = x => 5 != x.Score,
            ["Score == 5"] = x => x.Score == 5,
            ["!(Score == 5)"] = x => !(x.Score == 5),
            ["!(Score != 5)"] = x => !(x.Score != 5),
            ["!!(Score != 5)"] = x => !!(x.Score != 5),
            ["Score > 5"] = x => x.Score > 5,
            ["!(Score > 5)"] = x => !(x.Score > 5),
            ["!(Score <= 5)"] = x => !(x.Score <= 5),
            ["Score == null"] = x => x.Score == null,
            ["Score != null"] = x => x.Score != null,
            ["!(Score == null)"] = x => !(x.Score == null),
            ["Score != NoScore (null closure)"] = x => x.Score != NoScore,
            ["Name != a"] = x => x.Name != "a",
            ["!(Name == a)"] = x => !(x.Name == "a"),
            ["Name == a"] = x => x.Name == "a",
            ["Ref != G1"] = x => x.Ref != G1,
            ["!(Ref == G1)"] = x => !(x.Ref == G1),

            // a nullable column against a nullable column
            ["Score != Peer"] = x => x.Score != x.Peer,
            ["Score == Peer"] = x => x.Score == x.Peer,
            ["!(Score == Peer)"] = x => !(x.Score == x.Peer),
            ["!(Score != Peer)"] = x => !(x.Score != x.Peer),
            ["Score < Peer"] = x => x.Score < x.Peer,
            ["!(Score < Peer)"] = x => !(x.Score < x.Peer),

            // Contains, with and without null in the list
            ["{null,5}.Contains(Score)"] = x => FiveOrNull.Contains(x.Score),
            ["!{null,5}.Contains(Score)"] = x => !FiveOrNull.Contains(x.Score),
            ["{5}.Contains(Score)"] = x => Five.Contains(x.Score),
            ["!{5}.Contains(Score)"] = x => !Five.Contains(x.Score),
            ["{null}.Contains(Score)"] = x => OnlyNull.Contains(x.Score),
            ["!{null}.Contains(Score)"] = x => !OnlyNull.Contains(x.Score),
            ["literal {null,5}.Contains(Score)"] = x => new int?[] { null, 5 }.Contains(x.Score),
            ["!literal {null,5}.Contains(Score)"] = x => !new int?[] { null, 5 }.Contains(x.Score),
            ["List{6,null}.Contains(Score)"] = x => SixOrNull.Contains(x.Score),
            ["!List{6,null}.Contains(Score)"] = x => !SixOrNull.Contains(x.Score),
            ["{a,null}.Contains(Name)"] = x => AOrNull.Contains(x.Name),
            ["!{a,null}.Contains(Name)"] = x => !AOrNull.Contains(x.Name),
            ["!{a}.Contains(Name)"] = x => !A.Contains(x.Name),
            ["{G1,null}.Contains(Ref)"] = x => G1OrNull.Contains(x.Ref),
            ["!{G1}.Contains(Ref)"] = x => !G1Only.Contains(x.Ref),
            ["!(!{5}.Contains(Score))"] = x => !(!Five.Contains(x.Score)),

            // value fragments
            ["Optional + 1 != 8"] = x => x.Optional + 1m != 8m,
            ["Optional + 1 == 8"] = x => x.Optional + 1m == 8m,
            ["!(Optional + 1 == 8)"] = x => !(x.Optional + 1m == 8m),
            ["Score * 2 != 10"] = x => x.Score * 2 != 10,
            ["{null,6}.Contains(Score + 1)"] = x => SixOrNull.Contains(x.Score + 1),
            ["!{null,6}.Contains(Score + 1)"] = x => !SixOrNull.Contains(x.Score + 1),
            ["(Score ?? 0) != 5"] = x => (x.Score ?? 0) != 5,
            ["(Score != 5 ? 1 : 0) == 1"] = x => (x.Score != 5 ? 1 : 0) == 1,
            ["(!(Score == 5) ? 1 : 0) == 1"] = x => (!(x.Score == 5) ? 1 : 0) == 1,
            ["(Score == Peer ? 1 : 0) == 1"] = x => (x.Score == x.Peer ? 1 : 0) == 1,
            ["(Score > 5 ? 0 : 1) == 1"] = x => (x.Score > 5 ? 0 : 1) == 1,

            // nested and combined
            ["!(Score == 5 && Amount > 1)"] = x => !(x.Score == 5 && x.Amount > 1),
            ["!(Score != 5 || Amount > 5)"] = x => !(x.Score != 5 || x.Amount > 5),
            ["Amount > 2 && Score != 5"] = x => x.Amount > 2 && x.Score != 5,
            ["Amount > 4 || Score != 5"] = x => x.Amount > 4 || x.Score != 5,
            ["Score != 5 && Name != a"] = x => x.Score != 5 && x.Name != "a",
            ["!(Score != 5 && Name != a)"] = x => !(x.Score != 5 && x.Name != "a"),
            ["Score.HasValue && Score != 5"] = x => x.Score.HasValue && x.Score != 5,

            // non-nullable controls: unchanged SQL, unchanged answer
            ["Amount != 2"] = x => x.Amount != 2,
            ["!{5}.Contains(Amount)"] = x => !PlainFive.Contains(x.Amount),
            ["!(Amount == 2)"] = x => !(x.Amount == 2),
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private static IEnumerable<NullFilterRow> SeedRows()
    {
        // Every pairing of NULL / equal / different that the shapes read, Score and Peer crossed.
        yield return new NullFilterRow { Code = "r0", Score = null, Peer = null, Name = null, Ref = null, Optional = null, Amount = 1 };
        yield return new NullFilterRow { Code = "r1", Score = 5, Peer = null, Name = "a", Ref = G1, Optional = 7m, Amount = 2 };
        yield return new NullFilterRow { Code = "r2", Score = 8, Peer = 8, Name = "b", Ref = G2, Optional = 10.1m, Amount = 3 };
        yield return new NullFilterRow { Code = "r3", Score = null, Peer = 5, Name = "a", Ref = null, Optional = null, Amount = 4 };
        yield return new NullFilterRow { Code = "r4", Score = 5, Peer = 5, Name = null, Ref = G1, Optional = 7m, Amount = 5 };
        yield return new NullFilterRow { Code = "r5", Score = 8, Peer = 5, Name = "b", Ref = G2, Optional = 10.1m, Amount = 6 };
    }

    private async Task<(IAsyncBulkStore<NullFilterRow> Store, List<NullFilterRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = new List<NullFilterRow>();
        foreach (var row in SeedRows())
        {
            row.Guid = Guid.NewGuid();
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static List<string?> Codes(IEnumerable<NullFilterRow> rows)
        => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task Contract_A_filter_over_nullable_columns_reads_and_counts_what_CSharp_matches(string shape)
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
    public async Task Contract_A_filter_over_nullable_columns_deletes_and_updates_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var filter = Shapes[shape];

        var (store, seed) = await Seeded();
        var matched = Codes(seed.Where(filter.Compile()));
        var kept = Codes(seed.Where(x => !matched.Contains(x.Code)));
        // A filter that happens to match every seeded row still constrains: the whole-table guard must not refuse it.
        await store.DeleteAsync(filter);
        Codes(await store.ReadAsync(x => x.Code != null, null, null, null)).Should().Equal(kept, $"'{shape}' must delete only the rows C# matches");

        (store, seed) = await Seeded();
        await store.UpdateAsync(filter, new PropertyUpdate<NullFilterRow>().Set(x => x.Amount, 100));
        Codes(await store.ReadAsync(x => x.Amount == 100, null, null, null)).Should().Equal(matched, $"'{shape}' must update only the rows C# matches");
    }
}
