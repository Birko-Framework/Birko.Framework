using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>The row TASK-870's contract reads: a text and a target for its fold, decimals to divide, integers to overflow, dates.</summary>
[Table("T870Rows")]
public class LeftoverRow : AbstractModel
{
    public string Code { get; set; } = string.Empty;

    public string? Folded { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Amount { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Divisor { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal? NAmount { get; set; }

    [PrecisionField(28)]
    [ScaleField(10)]
    public decimal Precise { get; set; }

    public int Hits { get; set; }

    public long Big { get; set; }

    public short Sh { get; set; }

    public DateTime At { get; set; }

    public DateTime Other { get; set; }
}

/// <summary>
/// TASK-870 — the SQL translations TASK-860 left: a case fold (<c>ToLower</c> / <c>ToUpper</c>) over non-ASCII text, a
/// decimal division in a filter, an update SET of a column's <c>.Date</c>, <c>Replace</c> in a SET, and integer arithmetic
/// in a SET that overflows. Each case is measured against what C# computes; a statement either does what C# does, or is
/// refused (or fails) and writes nothing.
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class TranslationLeftoversContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="LeftoverRow"/>.</summary>
    protected abstract IAsyncBulkStore<LeftoverRow> NewStore();

    private static AbstractConnector ConnectorOf(IAsyncBulkStore<LeftoverRow> store)
        => ((ISqlConnectedStore)store).Connector ?? throw new InvalidOperationException("The store has no connector.");

    private static readonly Expression<Func<LeftoverRow, bool>> EveryRow = x => x.Code != null;

    private static void Set<P>(IAsyncBulkStore<LeftoverRow> store, Expression<Func<LeftoverRow, P>> column,
        Expression<Func<LeftoverRow, P>> value, Expression<Func<LeftoverRow, bool>>? filter = null)
        => ConnectorOf(store).Update(typeof(LeftoverRow),
            new Dictionary<Expression<Func<LeftoverRow, P>>, Expression<Func<LeftoverRow, P>>> { [column] = value }, filter ?? EveryRow);

    private static string Show(string? text)
        => text == null ? "null" : string.Concat(text.Select(c => c < 128 && !char.IsControl(c) ? c.ToString() : $"\\u{(int)c:X4}"));

    // ---------------------------------------------------------------- case folds

    /// <summary>Texts whose case mapping differs between .NET and at least one SQL engine, plus ASCII controls.</summary>
    public static readonly string[] FoldTexts =
    {
        "MiXeD", "abc", "Čučoriedka", "ŽLTÝ KÔŇ", "Ärger", "Straße", "STRASSE", "İstanbul", "ıi", "ſ", "K", "Å",
        "Ω", "ǅǈǋ", "Ǆ", "ΣΊΣΥΦΟΣ", "ς", "ﬀ", "Ⅻ", "ⓐⒶ", "Ａｂｃ", "𐐀𐐨", "ẞ", "Ꭰ", "ꭰ", "µ", "ÿ", "ȿ", "Ꞵ", "ᲀ",
        "ǰ", "ΐ", "Ǳ", "ﬅ",
    };

    public sealed record Fold(Func<string, string> CSharp,
        Func<string, Expression<Func<LeftoverRow, bool>>> Equal,
        Func<string, Expression<Func<LeftoverRow, bool>>> OrdinalEqual,
        Func<string, Expression<Func<LeftoverRow, bool>>> Contains,
        Func<string, Expression<Func<LeftoverRow, bool>>> OrdinalContains,
        Expression<Func<LeftoverRow, string?>> SetValue);

    public static readonly IReadOnlyDictionary<string, Fold> Folds = new Dictionary<string, Fold>
    {
        ["ToLower"] = new(s => s.ToLower(),
            t => x => x.Code.ToLower() == t,
            t => x => string.Equals(x.Code.ToLower(), t, StringComparison.Ordinal),
            t => x => x.Code.ToLower().Contains(t),
            t => x => x.Code.ToLower().Contains(t, StringComparison.Ordinal),
            x => x.Code.ToLower()),
        ["ToUpper"] = new(s => s.ToUpper(),
            t => x => x.Code.ToUpper() == t,
            t => x => string.Equals(x.Code.ToUpper(), t, StringComparison.Ordinal),
            t => x => x.Code.ToUpper().Contains(t),
            t => x => x.Code.ToUpper().Contains(t, StringComparison.Ordinal),
            x => x.Code.ToUpper()),
        ["ToLowerInvariant"] = new(s => s.ToLowerInvariant(),
            t => x => x.Code.ToLowerInvariant() == t,
            t => x => string.Equals(x.Code.ToLowerInvariant(), t, StringComparison.Ordinal),
            t => x => x.Code.ToLowerInvariant().Contains(t),
            t => x => x.Code.ToLowerInvariant().Contains(t, StringComparison.Ordinal),
            x => x.Code.ToLowerInvariant()),
        ["ToUpperInvariant"] = new(s => s.ToUpperInvariant(),
            t => x => x.Code.ToUpperInvariant() == t,
            t => x => string.Equals(x.Code.ToUpperInvariant(), t, StringComparison.Ordinal),
            t => x => x.Code.ToUpperInvariant().Contains(t),
            t => x => x.Code.ToUpperInvariant().Contains(t, StringComparison.Ordinal),
            x => x.Code.ToUpperInvariant()),
    };

    public static IEnumerable<object[]> FoldNames => Folds.Keys.Select(k => new object[] { k });

    private async Task<(IAsyncBulkStore<LeftoverRow> Store, List<LeftoverRow> Seed)> SeedTexts()
    {
        var store = NewStore();
        var seed = new List<LeftoverRow>();
        foreach (var text in FoldTexts)
        {
            var row = new LeftoverRow { Guid = Guid.NewGuid(), Code = text, At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1) };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static List<string> Codes(IEnumerable<LeftoverRow> rows) => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>The first text element of <paramref name="text"/>: a term that never splits a surrogate pair.</summary>
    private static string FirstElement(string text) => StringInfo.GetNextTextElement(text, 0);

    /// <summary>
    /// <c>SET Folded = Code.ToLower()</c> (and the other folds) stores what C# computes for every text. This isolates the fold
    /// itself from how the provider compares text.
    /// </summary>
    [Theory]
    [MemberData(nameof(FoldNames))]
    public async Task Contract_A_case_fold_in_SET_stores_what_CSharp_computes(string fold)
    {
        if (!Ready()) return;
        var (store, seed) = await SeedTexts();
        var f = Folds[fold];

        Set(store, x => x.Folded, f.SetValue);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Guid!.Value);
        var off = seed.Where(r => !string.Equals(stored[r.Guid!.Value].Folded, f.CSharp(r.Code), StringComparison.Ordinal))
            .Select(r => $"{Show(r.Code)} -> {Show(stored[r.Guid!.Value].Folded)}, C# {Show(f.CSharp(r.Code))}").ToList();
        off.Count.Should().BeLessThanOrEqualTo(FoldMismatchesAllowed(FoldTest.Set, fold),
            $"SET {fold} must store C#'s fold: {off.Count} of {seed.Count} off:\n{string.Join("\n", off)}");
    }

    /// <summary>
    /// <c>string.Equals(x.Code.ToLower(), t, Ordinal)</c> and <c>x.Code.ToLower().Contains(t, Ordinal)</c>: the comparison is
    /// TASK-869's exact one, so only the fold can differ from C#.
    /// </summary>
    [Theory]
    [MemberData(nameof(FoldNames))]
    public async Task Contract_A_case_fold_compared_ordinally_matches_what_CSharp_matches(string fold)
    {
        if (!Ready()) return;
        var (store, seed) = await SeedTexts();
        var f = Folds[fold];
        var off = new List<string>();
        var cases = 0;
        foreach (var text in FoldTexts)
        {
            var term = f.CSharp(text);
            foreach (var (kind, filter) in new[] { ("Equals", f.OrdinalEqual(term)), ("Contains", f.OrdinalContains(FirstElement(term))) })
            {
                cases++;
                var expected = Codes(seed.Where(filter.Compile()));
                var actual = Codes(await store.ReadAsync(filter, null, null, null));
                if (!actual.SequenceEqual(expected))
                    off.Add($"{kind}({Show(kind == "Equals" ? term : FirstElement(term))}): got [{string.Join(", ", actual.Select(Show))}], C# [{string.Join(", ", expected.Select(Show))}]");
            }
        }
        off.Count.Should().BeLessThanOrEqualTo(FoldMismatchesAllowed(FoldTest.Ordinal, fold),
            $"{fold} compared ordinally must match C#: {off.Count} of {cases} off:\n{string.Join("\n", off)}");
    }

    /// <summary>
    /// The shape consumers write: <c>x.Code.ToLower() == t</c> and <c>x.Code.ToLower().Contains(t)</c>, without a comparison.
    /// The comparison follows the column's collation (TASK-869, option A), so this records, per provider, how many cases
    /// differ from C#; <see cref="FoldedComparisonMismatchesAllowed"/> is that provider's measured budget.
    /// </summary>
    [Theory]
    [MemberData(nameof(FoldNames))]
    public async Task Contract_A_case_fold_compared_by_collation_is_measured(string fold)
    {
        if (!Ready()) return;
        var (store, seed) = await SeedTexts();
        var f = Folds[fold];
        var off = new List<string>();
        var cases = 0;
        foreach (var text in FoldTexts)
        {
            var term = f.CSharp(text);
            foreach (var (kind, filter) in new[] { ("==", f.Equal(term)), ("Contains", f.Contains(FirstElement(term))) })
            {
                cases++;
                var expected = Codes(seed.Where(filter.Compile()));
                var actual = Codes(await store.ReadAsync(filter, null, null, null));
                if (!actual.SequenceEqual(expected))
                    off.Add($"{kind}({Show(kind == "==" ? term : FirstElement(term))}): got [{string.Join(", ", actual.Select(Show))}], C# [{string.Join(", ", expected.Select(Show))}]");
            }
        }
        off.Count.Should().BeLessThanOrEqualTo(FoldMismatchesAllowed(FoldTest.Collation, fold),
            $"{fold} compared by collation: {off.Count} of {cases} off:\n{string.Join("\n", off)}");
    }

    public enum FoldTest
    {
        /// <summary><c>SET Folded = Code.ToLower()</c>, of 34 texts.</summary>
        Set,
        /// <summary>The fold compared with <c>StringComparison.Ordinal</c>, of 68 cases.</summary>
        Ordinal,
        /// <summary>The fold compared by <c>==</c> / <c>Contains</c>, following the collation, of 68 cases.</summary>
        Collation,
    }

    /// <summary>
    /// How many cases of <paramref name="test"/> may differ from C# for <paramref name="fold"/> on this provider. Zero where the
    /// fold is C#'s own (SQLite, TASK-870). On a server <c>LOWER</c> / <c>UPPER</c> fold by the server's case tables, which
    /// differ from .NET's; that gap is measured, recorded as each provider's budget, and left for a decision (TASK-870), so a
    /// regression past it fails and a fix shows up as a budget to lower.
    /// </summary>
    protected virtual int FoldMismatchesAllowed(FoldTest test, string fold) => 0;

    // ---------------------------------------------------------------- decimal division in a filter and in SET

    /// <summary>
    /// The seed: ordinary amounts, and three rows chosen where a server's scale of the quotient shows. 15.0003 / 300007 is
    /// 0.0000499998…, 1.7e-10 below a 4-decimal midpoint, which MySQL's 8 decimals round onto; 0.0001 / 2.0001 likewise;
    /// 30000000000000.0001 / 10000000000000 is 3 + 1e-17, past PostgreSQL's ~16 significant digits.
    /// </summary>
    private static readonly (decimal Amount, decimal Divisor)[] DecimalRows =
    {
        (10m, 3m), (10.5m, 4m), (1m, 7m), (100m, 3m), (2.5m, 0.3m), (-7m, 3m), (0.0001m, 3m), (12345678.9m, 7m), (1m, 1m), (2m, 3m),
        (15.0003m, 300007m), (0.0001m, 2.0001m), (30000000000000.0001m, 10000000000000m),
    };

    private async Task<(IAsyncBulkStore<LeftoverRow> Store, List<LeftoverRow> Seed)> SeedDecimals()
    {
        var store = NewStore();
        var seed = new List<LeftoverRow>();
        for (var i = 0; i < DecimalRows.Length; i++)
        {
            var row = new LeftoverRow
            {
                Guid = Guid.NewGuid(), Code = "d" + i.ToString("00", CultureInfo.InvariantCulture), Amount = DecimalRows[i].Amount,
                NAmount = i % 3 == 0 ? null : DecimalRows[i].Amount, Divisor = DecimalRows[i].Divisor, Hits = i - 3,
                At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1),
            };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static readonly decimal[] HalfAmounts = { 5m, 5.25m, 0.5m, 0.00005m };

    /// <summary>
    /// A division by a constant whose reciprocal terminates (2^i·5^j·10^k): the quotient ends a few decimals past the
    /// dividend, C# computes it exactly, and every provider computes it as the product with the exact reciprocal.
    /// Translated everywhere; HEAD answered these exactly too (TASK-870's review).
    /// </summary>
    public static IEnumerable<(string Name, Expression<Func<LeftoverRow, bool>> Filter)> ExactDivisionShapes()
    {
        yield return ("Amount / 4m == 2.625m", x => x.Amount / 4m == 2.625m);
        yield return ("Amount / 2m == 5m", x => x.Amount / 2m == 5m);
        yield return ("(decimal)Hits / 4m == 0.75m", x => (decimal)x.Hits / 4m == 0.75m);
        yield return ("(decimal)Hits / 4m < 1m", x => (decimal)x.Hits / 4m < 1m);
        yield return ("Amount > Divisor / 2m", x => x.Amount > x.Divisor / 2m);
        yield return ("HalfAmounts.Contains(Amount / 2m)", x => HalfAmounts.Contains(x.Amount / 2m));
        yield return ("NAmount / 2m != 5m", x => x.NAmount / 2m != 5m);
        yield return ("!(Amount / 4m > 2m)", x => !(x.Amount / 4m > 2m));
        yield return ("(Hits > 0 ? Amount / 2m : Amount) > 3m", x => (x.Hits > 0 ? x.Amount / 2m : x.Amount) > 3m);
        yield return ("Amount / 2.5m >= 4m", x => x.Amount / 2.5m >= 4m);
        yield return ("Amount / 100m == 0.1m", x => x.Amount / 100m == 0.1m);
        yield return ("Amount / -0.5m < -10m", x => x.Amount / -0.5m < -10m);
        yield return ("Amount / 0.0016m > 10000m", x => x.Amount / 0.0016m > 10000m);
        yield return ("Amount / 1024m < 0.001m", x => x.Amount / 1024m < 0.001m);
        yield return ("Round(Amount / 8m, 2, ToEven) == 1.31m", x => Math.Round(x.Amount / 8m, 2, MidpointRounding.ToEven) == 1.31m);
        foreach (var (a, _) in DecimalRows)
        {
            var byFour = a / 4m;
            yield return ($"Amount / 4m == {byFour.ToString(CultureInfo.InvariantCulture)}", x => x.Amount / 4m == byFour);
        }
    }

    /// <summary>
    /// A division by a column or by a constant whose reciprocal does not terminate: each server keeps its own scale of the
    /// quotient. Refused there, translated on SQLite (C#'s decimal).
    /// </summary>
    public static IEnumerable<(string Name, Expression<Func<LeftoverRow, bool>> Filter)> InexactDivisionShapes()
    {
        yield return ("Amount / 3m > 3.3333333m", x => x.Amount / 3m > 3.3333333m);
        yield return ("Amount / 3m == 3.333333m", x => x.Amount / 3m == 3.333333m);
        yield return ("Amount / 3m < 3.3333333333333333m", x => x.Amount / 3m < 3.3333333333333333m);
        yield return ("Amount / 3m * 3m == Amount", x => x.Amount / 3m * 3m == x.Amount);
        yield return ("Amount / Divisor * Divisor == Amount", x => x.Amount / x.Divisor * x.Divisor == x.Amount);
        yield return ("Amount / Divisor > 3m", x => x.Amount / x.Divisor > 3m);
        yield return ("Amount / Divisor < 0.00005m", x => x.Amount / x.Divisor < 0.00005m);
        yield return ("Amount / 300007m < 0.00005m", x => x.Amount / 300007m < 0.00005m);
        yield return ("(decimal)Hits / 3m > 0.6666666m", x => (decimal)x.Hits / 3m > 0.6666666m);
        yield return ("Round(Amount / 3m, 2, AwayFromZero) == 3.33m", x => Math.Round(x.Amount / 3m, 2, MidpointRounding.AwayFromZero) == 3.33m);
        yield return ("Round(Amount / 3m, 7, AwayFromZero) == 0.6666667m", x => Math.Round(x.Amount / 3m, 7, MidpointRounding.AwayFromZero) == 0.6666667m);
        yield return ("Round(Amount / 300007m, 4, AwayFromZero) == 0m", x => Math.Round(x.Amount / 300007m, 4, MidpointRounding.AwayFromZero) == 0m);
        foreach (var (a, d) in DecimalRows)
        {
            var byThree = a / 3m;
            var byDivisor = a / d;
            yield return ($"Amount / 3m == {byThree.ToString(CultureInfo.InvariantCulture)}", x => x.Amount / 3m == byThree);
            yield return ($"Amount / Divisor == {byDivisor.ToString(CultureInfo.InvariantCulture)}", x => x.Amount / x.Divisor == byDivisor);
        }
    }

    /// <summary>A decimal modulo is exact on every provider (measured), and keeps translating.</summary>
    public static IEnumerable<(string Name, Expression<Func<LeftoverRow, bool>> Filter)> ModuloShapes()
    {
        yield return ("Amount % 3m == 1m", x => x.Amount % 3m == 1m);
        yield return ("Amount % Divisor == 0.1m", x => x.Amount % x.Divisor == 0.1m);
        yield return ("Amount % Divisor > 0.5m", x => x.Amount % x.Divisor > 0.5m);
    }

    /// <summary>
    /// True where the provider divides decimals as C# does (SQLite, in C#'s own decimal); elsewhere a division that is not
    /// an exact product is refused by name before anything runs.
    /// </summary>
    protected virtual bool DividesDecimalsExactly => false;

    private static string Row(List<string> codes) => "[" + string.Join(", ", codes) + "]";

    [Fact]
    public async Task Contract_A_decimal_division_in_a_filter_matches_what_CSharp_matches_or_is_refused_by_name()
    {
        if (!Ready()) return;
        var (store, seed) = await SeedDecimals();
        var off = new List<string>();
        foreach (var (name, filter) in ExactDivisionShapes().Concat(ModuloShapes()))
        {
            var expected = Codes(seed.Where(filter.Compile()));
            try
            {
                var actual = Codes(await store.ReadAsync(filter, null, null, null));
                if (!actual.SequenceEqual(expected)) off.Add($"exact {name}: got {Row(actual)}, C# {Row(expected)}");
                if (await store.CountAsync(filter) != expected.Count) off.Add($"exact {name}: count differs");
            }
            catch (NotSupportedException ex)
            {
                off.Add($"exact {name}: refused ({ex.Message})");
            }
        }
        foreach (var (name, filter) in InexactDivisionShapes())
        {
            var expected = Codes(seed.Where(filter.Compile()));
            try
            {
                var actual = Codes(await store.ReadAsync(filter, null, null, null));
                if (!DividesDecimalsExactly)
                    off.Add($"inexact {name}: not refused; {(actual.SequenceEqual(expected) ? "matched C# here" : $"got {Row(actual)}, C# {Row(expected)}")}");
                else if (!actual.SequenceEqual(expected))
                    off.Add($"inexact {name}: got {Row(actual)}, C# {Row(expected)}");
            }
            catch (NotSupportedException ex)
            {
                if (DividesDecimalsExactly || !ex.Message.Contains("division of a decimal", StringComparison.Ordinal))
                    off.Add($"inexact {name}: refused ({ex.Message})");
            }
        }
        off.Should().BeEmpty($"a decimal division must match C# or be refused by name:\n{string.Join("\n", off)}");

        var delete = () => store.DeleteAsync(x => x.Amount / x.Divisor > 3m);
        if (!DividesDecimalsExactly) await delete.Should().ThrowAsync<NotSupportedException>();
        else await delete();
        (await store.CountAsync(EveryRow)).Should().Be(seed.Count - (DividesDecimalsExactly ? seed.Count(r => r.Amount / r.Divisor > 3m) : 0),
            "a refused delete never ran");
    }

    /// <summary>
    /// <c>Precise</c> is DECIMAL(28,10): a column with more decimals than a server keeps of a quotient (SQL Server as few as
    /// six), where storing the server's quotient loses digits C# keeps. Into <c>Amount</c> (4 decimals) the same divisions
    /// happened to store what C# stores on every server, measured on this seed.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (bool Precise, Expression<Func<LeftoverRow, decimal>> Value, Func<LeftoverRow, decimal> CSharp, bool Exact)> DivisionSets =
        new Dictionary<string, (bool, Expression<Func<LeftoverRow, decimal>>, Func<LeftoverRow, decimal>, bool)>
        {
            ["Amount = Amount / 4m"] = (false, x => x.Amount / 4m, r => r.Amount / 4m, true),
            ["Amount = Amount / 2.5m"] = (false, x => x.Amount / 2.5m, r => r.Amount / 2.5m, true),
            ["Amount = Amount / 1.6m"] = (false, x => x.Amount / 1.6m, r => r.Amount / 1.6m, true),
            ["Precise = Amount / 1.6m"] = (true, x => x.Amount / 1.6m, r => r.Amount / 1.6m, true),
            ["Amount = Amount / 3m"] = (false, x => x.Amount / 3m, r => r.Amount / 3m, false),
            ["Amount = Amount / 300007m"] = (false, x => x.Amount / 300007m, r => r.Amount / 300007m, false),
            ["Amount = Amount / Divisor"] = (false, x => x.Amount / x.Divisor, r => r.Amount / r.Divisor, false),
            ["Precise = Amount / 3m"] = (true, x => x.Amount / 3m, r => r.Amount / 3m, false),
            ["Precise = Amount / Divisor"] = (true, x => x.Amount / x.Divisor, r => r.Amount / r.Divisor, false),
        };

    public static IEnumerable<object[]> DivisionSetNames => DivisionSets.Keys.Select(k => new object[] { k });

    /// <summary>
    /// A decimal division in an update SET value follows the filter's rule. What C# writes is the C# quotient saved through
    /// the store (which rounds it to the column's scale), so each expected value is read back from a row the store saved.
    /// On the servers the refused shapes report, when a run reaches them unrefused, whether they matched (measurement).
    /// </summary>
    [Theory]
    [MemberData(nameof(DivisionSetNames))]
    public async Task Contract_A_decimal_division_in_SET_writes_what_CSharp_writes_or_is_refused_by_name(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await SeedDecimals();
        var (precise, value, csharp, exact) = DivisionSets[shape];
        Func<LeftoverRow, decimal> read = precise ? r => r.Precise : r => r.Amount;
        foreach (var row in seed)
        {
            var oracle = new LeftoverRow { Guid = Guid.NewGuid(), Code = "o" + row.Code, Divisor = 1m, At = row.At, Other = row.Other };
            if (precise) oracle.Precise = csharp(row);
            else oracle.Amount = csharp(row);
            await store.CreateAsync(oracle);
        }
        var saved = (await store.ReadAsync(x => x.Code.StartsWith("o"), null, null, null)).ToDictionary(x => x.Code.Substring(1), read);
        Expression<Func<LeftoverRow, bool>> onlySeed = x => x.Code.StartsWith("d");

        Exception? refusal = null;
        try
        {
            if (precise) Set(store, x => x.Precise, value, onlySeed);
            else Set(store, x => x.Amount, value, onlySeed);
        }
        catch (NotSupportedException ex)
        {
            refusal = ex;
        }
        var after = (await store.ReadAsync(onlySeed, null, null, null)).ToDictionary(x => x.Code, read);
        var off = seed.Where(r => after[r.Code] != saved[r.Code])
            .Select(r => $"{r.Code} ({r.Amount} / {r.Divisor}): SET stored {after[r.Code]}, C# saved {saved[r.Code]}").ToList();

        if (!exact && !DividesDecimalsExactly)
        {
            refusal.Should().NotBeNull($"SET {shape} has no exact form here; unrefused it was {(off.Count == 0 ? "exact on this seed" : "off:\n" + string.Join("\n", off))}");
            refusal!.Message.Should().Contain("division of a decimal");
            after.Values.OrderBy(x => x).Should().Equal(seed.Select(read).OrderBy(x => x), "the refused update never ran");
            return;
        }
        refusal.Should().BeNull($"SET {shape} is exact here");
        off.Should().BeEmpty($"SET {shape} must store what C# stores:\n{string.Join("\n", off)}");
    }

    // ---------------------------------------------------------------- SET .Date

    private async Task<(IAsyncBulkStore<LeftoverRow> Store, List<LeftoverRow> Seed)> SeedDates()
    {
        var store = NewStore();
        var seed = new List<LeftoverRow>();
        var dates = new[]
        {
            (new DateTime(2026, 3, 9, 8, 30, 0), new DateTime(2026, 3, 9, 23, 0, 0)),
            (new DateTime(2026, 3, 9, 0, 0, 0), new DateTime(2026, 3, 8, 23, 59, 59)),
            (new DateTime(2025, 12, 31, 23, 59, 59, 999), new DateTime(2026, 1, 1, 0, 0, 0)),
            (new DateTime(2024, 2, 29, 12, 0, 0), new DateTime(2024, 2, 29, 12, 0, 0)),
        };
        for (var i = 0; i < dates.Length; i++)
        {
            var row = new LeftoverRow { Guid = Guid.NewGuid(), Code = "t" + i.ToString(CultureInfo.InvariantCulture), At = dates[i].Item1, Other = dates[i].Item2 };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    /// <summary>
    /// <c>SET At = At.Date</c> stores midnight of that day, and the stored value then compares as a DateTime: equal to the day,
    /// and inside a range.
    /// </summary>
    [Fact]
    public async Task Contract_A_SET_of_a_columns_Date_stores_what_CSharp_stores()
    {
        if (!Ready()) return;
        var (store, seed) = await SeedDates();

        Set(store, x => x.At, x => x.At.Date);
        foreach (var row in seed) row.At = row.At.Date;

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code);
        foreach (var row in seed)
            stored[row.Code].At.Should().Be(row.At, $"SET At = At.Date must store midnight for '{row.Code}'");

        foreach (var day in seed.Select(r => r.At).Distinct())
        {
            Codes(await store.ReadAsync(x => x.At == day, null, null, null)).Should()
                .Equal(Codes(seed.Where(r => r.At == day)), $"the stored date must equal {day:O}");
            var next = day.AddDays(1);
            Codes(await store.ReadAsync(x => x.At >= day && x.At < next, null, null, null)).Should()
                .Equal(Codes(seed.Where(r => r.At >= day && r.At < next)), $"the stored date must be in [{day:O}, {next:O})");
        }
    }

    private static readonly DateTime[] SomeDays = { new(2026, 3, 9), new(2024, 2, 29) };

    /// <summary>
    /// A filter over a column's <c>.Date</c> that is not compared with a value (which becomes a range on the raw column):
    /// another column's <c>.Date</c>, or a list of days. <c>DATE(col)</c> is no T-SQL function, and on SQLite it is a text no
    /// bound DateTime equals.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<LeftoverRow, bool>>> DateColumnShapes =
        new Dictionary<string, Expression<Func<LeftoverRow, bool>>>
        {
            ["At.Date == Other.Date"] = x => x.At.Date == x.Other.Date,
            ["At.Date != Other.Date"] = x => x.At.Date != x.Other.Date,
            ["At.Date < Other.Date"] = x => x.At.Date < x.Other.Date,
            ["At.Date >= Other"] = x => x.At.Date >= x.Other,
            ["SomeDays.Contains(At.Date)"] = x => SomeDays.Contains(x.At.Date),
            ["!SomeDays.Contains(At.Date)"] = x => !SomeDays.Contains(x.At.Date),
        };

    public static IEnumerable<object[]> DateColumnNames => DateColumnShapes.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(DateColumnNames))]
    public async Task Contract_A_filter_over_a_columns_Date_matches_what_CSharp_matches_or_is_refused_by_name(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await SeedDates();
        var filter = DateColumnShapes[shape];
        List<string> actual;
        try
        {
            actual = Codes(await store.ReadAsync(filter, null, null, null));
        }
        catch (NotSupportedException ex)
        {
            ex.Message.Should().Contain(".Date", $"'{shape}' is refused by name");
            return;
        }
        actual.Should().Equal(Codes(seed.Where(filter.Compile())), $"'{shape}' must match C#");
    }

    // ---------------------------------------------------------------- Replace in SET

    public static readonly string[] ReplaceTexts = { "Abc", "ABC", "aBc", "Café", "CAFÉ", "Café", "Straße", "STRASSE", "a b ", "AAaa", "İi", "ǅ" };

    public static readonly IReadOnlyDictionary<string, (Expression<Func<LeftoverRow, string?>> Value, Func<string, string> CSharp)> ReplaceShapes =
        new Dictionary<string, (Expression<Func<LeftoverRow, string?>>, Func<string, string>)>
        {
            ["Replace(\"b\", \"X\")"] = (x => x.Code.Replace("b", "X"), s => s.Replace("b", "X")),
            ["Replace(\"B\", \"X\")"] = (x => x.Code.Replace("B", "X"), s => s.Replace("B", "X")),
            ["Replace(\"é\", \"e\")"] = (x => x.Code.Replace("é", "e"), s => s.Replace("é", "e")),
            ["Replace(\"e\", \"_\")"] = (x => x.Code.Replace("e", "_"), s => s.Replace("e", "_")),
            ["Replace(\"ss\", \"_\")"] = (x => x.Code.Replace("ss", "_"), s => s.Replace("ss", "_")),
            ["Replace(\"ß\", \"_\")"] = (x => x.Code.Replace("ß", "_"), s => s.Replace("ß", "_")),
            ["Replace(\" \", \"\")"] = (x => x.Code.Replace(" ", ""), s => s.Replace(" ", "")),
            ["Replace(\"a\", \"aa\")"] = (x => x.Code.Replace("a", "aa"), s => s.Replace("a", "aa")),
            ["Replace(\"i\", \"-\")"] = (x => x.Code.Replace("i", "-"), s => s.Replace("i", "-")),
            ["Replace(\"Ǆ\", \"-\")"] = (x => x.Code.Replace("Ǆ", "-"), s => s.Replace("Ǆ", "-")),
            ["Replace(\"A\", null)"] = (x => x.Code.Replace("A", null), s => s.Replace("A", null)),
        };

    public static IEnumerable<object[]> ReplaceNames => ReplaceShapes.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(ReplaceNames))]
    public async Task Contract_Replace_in_SET_stores_what_CSharp_computes(string shape)
    {
        if (!Ready()) return;
        var store = NewStore();
        var seed = new List<LeftoverRow>();
        foreach (var text in ReplaceTexts)
        {
            var row = new LeftoverRow { Guid = Guid.NewGuid(), Code = text, At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1) };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        var (value, csharp) = ReplaceShapes[shape];

        Set(store, x => x.Folded, value);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Guid!.Value);
        var off = seed.Where(r => !string.Equals(stored[r.Guid!.Value].Folded, csharp(r.Code), StringComparison.Ordinal))
            .Select(r => $"{Show(r.Code)} -> {Show(stored[r.Guid!.Value].Folded)}, C# {Show(csharp(r.Code))}").ToList();
        off.Should().BeEmpty($"SET Folded = Code.{shape} must store what C# computes ({off.Count} of {seed.Count} off)");
    }

    /// <summary>C# throws for an empty or null value to replace, so the update is refused by name and writes nothing.</summary>
    [Fact]
    public async Task Contract_Replace_of_an_empty_value_in_SET_is_refused_by_name()
    {
        if (!Ready()) return;
        var (store, seed) = await SeedTexts();
        var empty = string.Empty;

        var run = () => Set(store, x => x.Folded, x => x.Code.Replace(empty, "x"));
        run.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("Replace");
        (await store.ReadAsync(EveryRow, null, null, null)).Should().OnlyContain(x => x.Folded == null, "the refused update never ran");
    }

    // ---------------------------------------------------------------- integer overflow in SET

    public sealed record OverflowCase(Action<LeftoverRow> Prepare, Func<IAsyncBulkStore<LeftoverRow>, Expression<Func<LeftoverRow, bool>>, Task> Run,
        Func<LeftoverRow, object> Read);

    public static readonly IReadOnlyDictionary<string, OverflowCase> Overflows = new Dictionary<string, OverflowCase>
    {
        ["SET Hits = Hits + 1 at int.MaxValue"] = new(r => r.Hits = int.MaxValue,
            (s, f) => { Set(s, x => x.Hits, x => x.Hits + 1, f); return Task.CompletedTask; }, r => r.Hits),
        ["SET Hits = Hits - 1 at int.MinValue"] = new(r => r.Hits = int.MinValue,
            (s, f) => { Set(s, x => x.Hits, x => x.Hits - 1, f); return Task.CompletedTask; }, r => r.Hits),
        ["SET Hits = Hits * 2 at int.MaxValue / 2 + 1"] = new(r => r.Hits = int.MaxValue / 2 + 1,
            (s, f) => { Set(s, x => x.Hits, x => x.Hits * 2, f); return Task.CompletedTask; }, r => r.Hits),
        ["SET Hits = -Hits at int.MinValue"] = new(r => r.Hits = int.MinValue,
            (s, f) => { Set(s, x => x.Hits, x => -x.Hits, f); return Task.CompletedTask; }, r => r.Hits),
        ["SET Hits = Hits / -1 at int.MinValue"] = new(r => r.Hits = int.MinValue,
            (s, f) => { Set(s, x => x.Hits, x => x.Hits / -1, f); return Task.CompletedTask; }, r => r.Hits),
        ["SET Big = Big + 1 at long.MaxValue"] = new(r => r.Big = long.MaxValue,
            (s, f) => { Set(s, x => x.Big, x => x.Big + 1, f); return Task.CompletedTask; }, r => r.Big),
        ["SET Big = Big * 3 at long.MaxValue / 2"] = new(r => r.Big = long.MaxValue / 2,
            (s, f) => { Set(s, x => x.Big, x => x.Big * 3, f); return Task.CompletedTask; }, r => r.Big),
        ["SET Big = Hits + Big at long.MaxValue"] = new(r => { r.Big = long.MaxValue; r.Hits = 1; },
            (s, f) => { Set(s, x => x.Big, x => x.Hits + x.Big, f); return Task.CompletedTask; }, r => r.Big),
        ["Increment(Hits, 1) at int.MaxValue"] = new(r => r.Hits = int.MaxValue,
            (s, f) => s.UpdateAsync(f, new PropertyUpdate<LeftoverRow>().Increment(x => x.Hits, 1)), r => r.Hits),
        ["Increment(Hits, -1) at int.MinValue"] = new(r => r.Hits = int.MinValue,
            (s, f) => s.UpdateAsync(f, new PropertyUpdate<LeftoverRow>().Increment(x => x.Hits, -1)), r => r.Hits),
        ["Increment(Sh, 1) at short.MaxValue"] = new(r => r.Sh = short.MaxValue,
            (s, f) => s.UpdateAsync(f, new PropertyUpdate<LeftoverRow>().Increment(x => x.Sh, (short)1)), r => r.Sh),
        ["Increment(Big, 1) at long.MaxValue"] = new(r => r.Big = long.MaxValue,
            (s, f) => s.UpdateAsync(f, new PropertyUpdate<LeftoverRow>().Increment(x => x.Big, 1L)), r => r.Big),
    };

    public static IEnumerable<object[]> OverflowNames => Overflows.Keys.Select(k => new object[] { k });

    /// <summary>
    /// An integer SET that overflows its type: C# wraps (or throws, for <c>Increment</c>'s fallback), and the servers fail the
    /// statement. Either way nothing may be stored that C# never computes, and every row must still read.
    /// </summary>
    [Theory]
    [MemberData(nameof(OverflowNames))]
    public async Task Contract_An_integer_SET_that_overflows_fails_and_writes_nothing(string shape)
    {
        if (!Ready()) return;
        var store = NewStore();
        var overflow = Overflows[shape];
        var edge = new LeftoverRow { Guid = Guid.NewGuid(), Code = "edge", At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1) };
        overflow.Prepare(edge);
        var plain = new LeftoverRow { Guid = Guid.NewGuid(), Code = "plain", Hits = 5, Big = 5, Sh = 5, At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1) };
        await store.CreateAsync(edge);
        await store.CreateAsync(plain);
        var before = overflow.Read(edge);

        var run = () => overflow.Run(store, EveryRow);
        await run.Should().ThrowAsync<Exception>("C# wraps the overflow, so SQL's only exact answer is to fail");

        var rows = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code);
        overflow.Read(rows["edge"]).Should().Be(before, "a failed statement writes nothing");
        overflow.Read(rows["plain"]).Should().Be(overflow.Read(plain), "a failed statement writes nothing, on any row");
    }

    /// <summary>The same increments, in range, still write what C# writes.</summary>
    [Fact]
    public async Task Contract_An_integer_SET_in_range_writes_what_CSharp_writes()
    {
        if (!Ready()) return;
        var store = NewStore();
        var row = new LeftoverRow
        {
            Guid = Guid.NewGuid(), Code = "r", Hits = int.MaxValue - 1, Big = long.MaxValue - 1, Sh = short.MaxValue - 1,
            At = new DateTime(2026, 1, 1), Other = new DateTime(2026, 1, 1),
        };
        await store.CreateAsync(row);

        Set(store, x => x.Hits, x => x.Hits + 1);
        Set(store, x => x.Big, x => x.Big + 1);
        await store.UpdateAsync(EveryRow, new PropertyUpdate<LeftoverRow>().Increment(x => x.Sh, (short)1));
        Set(store, x => x.Hits, x => x.Hits - 1 - 1);
        await store.UpdateAsync(EveryRow, new PropertyUpdate<LeftoverRow>().Increment(x => x.Hits, 2));
        Set(store, x => x.Hits, x => -x.Hits);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).Single();
        stored.Hits.Should().Be(-int.MaxValue);
        stored.Big.Should().Be(long.MaxValue);
        stored.Sh.Should().Be(short.MaxValue);
    }
}
