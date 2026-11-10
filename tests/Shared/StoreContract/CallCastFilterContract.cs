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

public enum CallCastStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2,
}

/// <summary>The row TASK-860's contract reads: strings, every numeric width the parser converts between, a date, an enum.</summary>
[Table("T860CallRows")]
public class CallCastRow : AbstractModel
{
    public string Code { get; set; } = string.Empty;

    public string? Name { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Amount { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal? Optional { get; set; }

    public int Hits { get; set; }

    public int? Score { get; set; }

    public long Big { get; set; }

    public double Ratio { get; set; }

    public DateTime At { get; set; }

    public CallCastStatus Status { get; set; }

    public bool Flag { get; set; }

    public bool? OptFlag { get; set; }

    public char Letter { get; set; }

    public float F { get; set; }

    public short Sh { get; set; }
}

/// <summary>
/// TASK-860 — a SQL filter (and an update's SET value) containing a method call, a conversion, a member or an operator the
/// parser does not translate is refused by name before the statement runs; the ones it translates match what C# matches.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the parser's default for an unknown call or conversion
/// was to keep its operand and lose the operation. <c>x.Code.Substring(1) == "bc"</c> compared the bare column,
/// <c>Convert.ToInt32(x.Amount) == 10</c> compared the decimal, <c>(decimal)x.Hits / 4m</c> divided two integers on
/// PostgreSQL and SQL Server, <c>x.Score.GetValueOrDefault() != 5</c> missed the NULL rows, and
/// <c>string.Compare(x.Code, "b") &gt; 0</c> lost C#'s culture ordering — wrong rows, no error.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class CallCastFilterContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="CallCastRow"/>.</summary>
    protected abstract IAsyncBulkStore<CallCastRow> NewStore();

    private static readonly Guid FirstGuid = new("8600a1b2-0000-4000-8000-000000000001");
    private static readonly DateTime Day = new(2026, 3, 9);
    private static readonly int[] OpenOrClosed = { 1, 2 };
    private static readonly Func<CallCastRow, bool> Predicate = r => r.Hits > 2;
    private static readonly double SomeDouble = 2.5;

    public static readonly IReadOnlyDictionary<string, Expression<Func<CallCastRow, bool>>> Translated =
        new Dictionary<string, Expression<Func<CallCastRow, bool>>>
        {
            // Conversions: a widening is exact, and inside arithmetic it is made explicit so no integer division happens.
            ["(decimal)Hits / 4m == 0.75m"] = x => (decimal)x.Hits / 4m == 0.75m,
            ["(decimal)Hits / 4m < 1m"] = x => (decimal)x.Hits / 4m < 1m,
            ["Hits / 4.0 == 0.75"] = x => x.Hits / 4.0 == 0.75,
            ["(double)Hits / 4 > 1.2"] = x => (double)x.Hits / 4 > 1.2,
            ["(decimal)Hits + Amount > 10m"] = x => (decimal)x.Hits + x.Amount > 10m,
            ["(decimal)Hits == 3m"] = x => (decimal)x.Hits == 3m,
            ["(double)Hits > 2.5"] = x => (double)x.Hits > 2.5,
            ["(long)Hits * 3 == 9"] = x => (long)x.Hits * 3 == 9,
            ["Big > Hits"] = x => x.Big > x.Hits,
            ["Amount > Hits"] = x => x.Amount > x.Hits,
            ["(int)Status == 1"] = x => (int)x.Status == 1,
            ["Status == Open"] = x => x.Status == CallCastStatus.Open,
            ["(CallCastStatus)Hits == Closed"] = x => (CallCastStatus)x.Hits == CallCastStatus.Closed,
            ["OpenOrClosed.Contains((int)Status)"] = x => OpenOrClosed.Contains((int)x.Status),
            ["(int?)Hits == Score"] = x => (int?)x.Hits == x.Score,
            // Integer arithmetic is C#'s integer arithmetic.
            ["Hits / 4 == 0"] = x => x.Hits / 4 == 0,
            ["Hits / 2 == -1"] = x => x.Hits / 2 == -1,
            ["Hits % 3 == 1"] = x => x.Hits % 3 == 1,
            // GetValueOrDefault is a COALESCE.
            ["Score.GetValueOrDefault() != 5"] = x => x.Score.GetValueOrDefault() != 5,
            ["Score.GetValueOrDefault() == 0"] = x => x.Score.GetValueOrDefault() == 0,
            ["Score.GetValueOrDefault(7) == 7"] = x => x.Score.GetValueOrDefault(7) == 7,
            ["Optional.GetValueOrDefault() > 2m"] = x => x.Optional.GetValueOrDefault() > 2m,
            ["Score.GetValueOrDefault() < Hits"] = x => x.Score.GetValueOrDefault() < x.Hits,
            ["OptFlag.GetValueOrDefault()"] = x => x.OptFlag.GetValueOrDefault(),
            ["!OptFlag.GetValueOrDefault()"] = x => !x.OptFlag.GetValueOrDefault(),
            // Equals(T) is ==.
            ["Hits.Equals(3)"] = x => x.Hits.Equals(3),
            ["!Hits.Equals(3)"] = x => !x.Hits.Equals(3),
            ["Code.Equals(\"xyz\")"] = x => x.Code.Equals("xyz"),
            ["string.Equals(Code, \"xyz\")"] = x => string.Equals(x.Code, "xyz"),
            ["Guid.Equals(first)"] = x => x.Guid.Equals(FirstGuid),
            ["Amount.Equals(7m)"] = x => x.Amount.Equals(7m),
            ["Status.Equals(Open) (Enum.Equals(object))"] = x => x.Status.Equals(CallCastStatus.Open),
            ["!Status.Equals(Closed)"] = x => !x.Status.Equals(CallCastStatus.Closed),
            ["Score.Equals(5) (Nullable.Equals(object))"] = x => x.Score.Equals(5),
            ["!Score.Equals(5)"] = x => !x.Score.Equals(5),
            ["Hits.Equals(Score.Value) (guarded)"] = x => x.Score != null && x.Hits.Equals(x.Score.Value),
            // A boolean XOR is a boolean inequality.
            ["Flag ^ (Hits > 2)"] = x => x.Flag ^ (x.Hits > 2),
            ["!(Flag ^ (Hits > 2))"] = x => !(x.Flag ^ (x.Hits > 2)),
            // Shapes that already translated, kept translating.
            ["Score.HasValue && Score.Value > 3"] = x => x.Score.HasValue && x.Score.Value > 3,
            ["(Score ?? 0) > 3"] = x => (x.Score ?? 0) > 3,
            ["string.IsNullOrEmpty(Name)"] = x => string.IsNullOrEmpty(x.Name),
            ["!string.IsNullOrEmpty(Name)"] = x => !string.IsNullOrEmpty(x.Name),
            ["At.Date == Day"] = x => x.At.Date == Day,
            ["-Hits < -3"] = x => -x.Hits < -3,
            ["Code.ToLower() == \"abc\""] = x => x.Code.ToLower() == "abc",
            ["Code.ToUpperInvariant() == \"XYZ\""] = x => x.Code.ToUpperInvariant() == "XYZ",
            // Implicit integer-to-fraction conversions the compiler inserts (review of TASK-860: refused by the first cut).
            ["Big > 2.5"] = x => x.Big > 2.5,
            ["(double)Big > 2"] = x => (double)x.Big > 2,
            ["Big > SomeDouble"] = x => x.Big > SomeDouble,
            ["Ratio > Big"] = x => x.Ratio > x.Big,
            ["Ratio * Big > 3"] = x => x.Ratio * x.Big > 3,
            ["Big / 2.0 > 1.5"] = x => x.Big / 2.0 > 1.5,
            ["F > Hits"] = x => x.F > x.Hits,
            ["F * Hits > 2"] = x => x.F * x.Hits > 2,
            ["Hits > 2.5f"] = x => x.Hits > 2.5f,
            ["Sh == 2"] = x => x.Sh == 2,
            ["Sh > Hits"] = x => x.Sh > x.Hits,
            // A char pattern is the one-character string pattern (no letter here has a case twin in the seed).
            ["Code.Contains('y')"] = x => x.Code.Contains('y'),
            ["Code.StartsWith('x')"] = x => x.Code.StartsWith('x'),
            ["Code.EndsWith('1')"] = x => x.Code.EndsWith('1'),
            ["!Code.Contains('y')"] = x => !x.Code.Contains('y'),
        };

    public static IEnumerable<object[]> TranslatedNames => Translated.Keys.Select(k => new object[] { k });

    /// <summary>Shapes with no exact SQL form on every provider, each with the name its refusal must carry.</summary>
    public static readonly IReadOnlyDictionary<string, (Expression<Func<CallCastRow, bool>> Filter, string Named)> Refused =
        new Dictionary<string, (Expression<Func<CallCastRow, bool>>, string)>
        {
            // String calls and members.
            ["Code.Substring(1) == \"bc\""] = (x => x.Code.Substring(1) == "bc", "Substring"),
            ["Code.Substring(0, 1) == \"a\""] = (x => x.Code.Substring(0, 1) == "a", "Substring"),
            ["Code.Length > 3"] = (x => x.Code.Length > 3, "Length"),
            ["Code.Trim() == \"pad\""] = (x => x.Code.Trim() == "pad", "Trim"),
            ["Code.TrimStart() == \"pad \""] = (x => x.Code.TrimStart() == "pad ", "TrimStart"),
            ["Code.TrimEnd() == \" pad\""] = (x => x.Code.TrimEnd() == " pad", "TrimEnd"),
            ["Code.IndexOf(\"b\") == 1"] = (x => x.Code.IndexOf("b") == 1, "IndexOf"),
            ["Code.Replace(\"b\", \"c\") == \"Acc\""] = (x => x.Code.Replace("b", "c") == "Acc", "Replace"),
            ["Code.Trim().StartsWith(\"pa\")"] = (x => x.Code.Trim().StartsWith("pa"), "Trim"),
            ["Code.PadLeft(5) == \"  Abc\""] = (x => x.Code.PadLeft(5) == "  Abc", "PadLeft"),
            ["Code[0] == 'A'"] = (x => x.Code[0] == 'A', "Char"),
            ["Code + \"!\" == \"Abc!\""] = (x => x.Code + "!" == "Abc!", "Concat"),
            ["\"Abcdef\".StartsWith(Code)"] = (x => "Abcdef".StartsWith(x.Code), "StartsWith"),
            ["Code.ToUpper(InvariantCulture) == \"ABC\""] = (x => x.Code.ToUpper(CultureInfo.InvariantCulture) == "ABC", "ToUpper"),
            ["string.IsNullOrWhiteSpace(Name)"] = (x => string.IsNullOrWhiteSpace(x.Name), "IsNullOrWhiteSpace"),
            ["!string.IsNullOrWhiteSpace(Name)"] = (x => !string.IsNullOrWhiteSpace(x.Name), "IsNullOrWhiteSpace"),
            // Culture- or case-sensitive comparisons.
            ["string.Compare(Code, \"b\") > 0"] = (x => string.Compare(x.Code, "b") > 0, "Compare"),
            ["string.Compare(Code, \"b\", Ordinal) > 0"] = (x => string.Compare(x.Code, "b", StringComparison.Ordinal) > 0, "Compare"),
            ["string.CompareOrdinal(Code, \"b\") > 0"] = (x => string.CompareOrdinal(x.Code, "b") > 0, "CompareOrdinal"),
            ["Code.CompareTo(\"b\") > 0"] = (x => x.Code.CompareTo("b") > 0, "CompareTo"),
            ["Hits.CompareTo(3) > 0"] = (x => x.Hits.CompareTo(3) > 0, "CompareTo"),
            ["Hits.Equals((object)3)"] = (x => x.Hits.Equals((object)3), "Equals"),
            ["object.Equals(Code, \"xyz\")"] = (x => object.Equals(x.Code, "xyz"), "Equals"),
            // ToString and Convert.
            ["Hits.ToString() == \"3\""] = (x => x.Hits.ToString() == "3", "ToString"),
            ["Guid.ToString() == first"] = (x => x.Guid.ToString() == "8600a1b2-0000-4000-8000-000000000001", "ToString"),
            ["Convert.ToInt32(Amount) == 10"] = (x => Convert.ToInt32(x.Amount) == 10, "ToInt32"),
            ["Convert.ToString(Hits) == \"3\""] = (x => Convert.ToString(x.Hits) == "3", "ToString"),
            ["Convert.ToDecimal(Ratio) > 1m"] = (x => Convert.ToDecimal(x.Ratio) > 1m, "ToDecimal"),
            // Narrowing or lossy casts.
            ["(int)Amount == 10"] = (x => (int)x.Amount == 10, "Int32"),
            ["(int)Amount + 1 == 11"] = (x => (int)x.Amount + 1 == 11, "Int32"),
            ["(int)(Amount + 1m) == 11"] = (x => (int)(x.Amount + 1m) == 11, "Int32"),
            ["(int)Ratio == 2"] = (x => (int)x.Ratio == 2, "Int32"),
            ["(decimal)Ratio > 1m"] = (x => (decimal)x.Ratio > 1m, "Decimal"),
            ["(float)Ratio > 1f"] = (x => (float)x.Ratio > 1f, "Single"),
            ["(int)Big == 3"] = (x => (int)x.Big == 3, "Int32"),
            ["(short)Hits == 3"] = (x => (short)x.Hits == 3, "Int16"),
            ["checked((int)Big) == 3"] = (x => checked((int)x.Big) == 3, "Int32"),
            ["Letter == 'a'"] = (x => x.Letter == 'a', "Char"),
            ["OpenOrClosed.Contains((int)Amount)"] = (x => OpenOrClosed.Contains((int)x.Amount), "Int32"),
            // DateTime members and methods over the column.
            ["At.Year == 2026"] = (x => x.At.Year == 2026, "Year"),
            ["At.Month == 3"] = (x => x.At.Month == 3, "Month"),
            ["At.DayOfWeek == Monday"] = (x => x.At.DayOfWeek == DayOfWeek.Monday, "DayOfWeek"),
            ["At.TimeOfDay > 12h"] = (x => x.At.TimeOfDay > TimeSpan.FromHours(12), "TimeOfDay"),
            ["At.AddDays(1) > Day"] = (x => x.At.AddDays(1) > Day, "AddDays"),
            // Bitwise operators.
            ["(Hits & 1) == 1"] = (x => (x.Hits & 1) == 1, "And"),
            ["(Hits | 1) == 3"] = (x => (x.Hits | 1) == 3, "Or"),
            ["(Hits ^ 1) == 2"] = (x => (x.Hits ^ 1) == 2, "ExclusiveOr"),
            ["(Hits << 1) == 4"] = (x => (x.Hits << 1) == 4, "LeftShift"),
            ["(Hits >> 1) == 1"] = (x => (x.Hits >> 1) == 1, "RightShift"),
            ["(OptFlag ^ Flag) == true"] = (x => (x.OptFlag ^ x.Flag) == true, "ExclusiveOr"),
            // Invocation of a delegate.
            ["Predicate(x)"] = (x => Predicate(x), "Invoke"),
        };

    public static IEnumerable<object[]> RefusedNames => Refused.Keys.Select(k => new object[] { k });

    /// <summary>An update SET value: the column, the SQL update, and what C# writes.</summary>
    public sealed record SetCase(Action<AbstractConnector, LambdaExpression> Run, Action<CallCastRow> Oracle, Func<CallCastRow, object?> Read);

    private static SetCase Set<P>(Expression<Func<CallCastRow, P>> column, Expression<Func<CallCastRow, P>> value)
    {
        var property = (PropertyInfo)((MemberExpression)column.Body).Member;
        var compiled = value.Compile();
        return new SetCase(
            (connector, filter) => connector.Update(typeof(CallCastRow),
                new Dictionary<Expression<Func<CallCastRow, P>>, Expression<Func<CallCastRow, P>>> { [column] = value }, filter),
            row => property.SetValue(row, compiled(row)),
            row => property.GetValue(row));
    }

    public static readonly IReadOnlyDictionary<string, SetCase> SetTranslated = new Dictionary<string, SetCase>
    {
        ["Amount = -Amount"] = Set(x => x.Amount, x => -x.Amount),
        ["Hits = -Hits"] = Set(x => x.Hits, x => -x.Hits),
        ["Ratio = -Ratio"] = Set(x => x.Ratio, x => -x.Ratio),
        ["Hits = +Hits"] = Set(x => x.Hits, x => +x.Hits),
        ["Amount = -(Amount + 1m)"] = Set(x => x.Amount, x => -(x.Amount + 1m)),
        ["Hits = Hits / 4"] = Set(x => x.Hits, x => x.Hits / 4),
        ["Hits = Hits % 3"] = Set(x => x.Hits, x => x.Hits % 3),
        ["Amount = Amount + Hits"] = Set(x => x.Amount, x => x.Amount + x.Hits),
        ["Amount = (decimal)Hits"] = Set(x => x.Amount, x => (decimal)x.Hits),
        ["Big = Hits"] = Set(x => x.Big, x => x.Hits),
        ["Hits = Score.GetValueOrDefault(7)"] = Set(x => x.Hits, x => x.Score.GetValueOrDefault(7)),
        ["Amount = Optional.GetValueOrDefault()"] = Set(x => x.Amount, x => x.Optional.GetValueOrDefault()),
        ["Hits = (int)Status"] = Set(x => x.Hits, x => (int)x.Status),
        ["Ratio = Big"] = Set(x => x.Ratio, x => x.Big),
        ["Ratio = Hits / 2.0"] = Set(x => x.Ratio, x => x.Hits / 2.0),
        ["F = Hits"] = Set(x => x.F, x => x.Hits),
    };

    public static IEnumerable<object[]> SetTranslatedNames => SetTranslated.Keys.Select(k => new object[] { k });

    public static readonly IReadOnlyDictionary<string, (SetCase Case, string Named)> SetRefused = new Dictionary<string, (SetCase, string)>
    {
        ["Hits = (int)Amount"] = (Set(x => x.Hits, x => (int)x.Amount), "Int32"),
        ["Amount = (decimal)Ratio"] = (Set(x => x.Amount, x => (decimal)x.Ratio), "Decimal"),
        ["Amount = (decimal)Hits / Hits2"] = (Set(x => x.Amount, x => (decimal)x.Hits / (x.Hits + 10)), "Divide"),
        ["Hits = Hits & 1"] = (Set(x => x.Hits, x => x.Hits & 1), "And"),
        ["Hits = Hits | 1"] = (Set(x => x.Hits, x => x.Hits | 1), "Or"),
        ["Hits = Hits ^ 1"] = (Set(x => x.Hits, x => x.Hits ^ 1), "ExclusiveOr"),
        ["Hits = Hits << 1"] = (Set(x => x.Hits, x => x.Hits << 1), "LeftShift"),
        ["Hits = ~Hits"] = (Set(x => x.Hits, x => ~x.Hits), "complement"),
        ["Flag = !Flag"] = (Set(x => x.Flag, x => !x.Flag), "Not"),
        ["Code = Code.Substring(1)"] = (Set(x => x.Code, x => x.Code.Substring(1)), "Substring"),
        ["Code = Code.ToUpper(InvariantCulture)"] = (Set(x => x.Code, x => x.Code.ToUpper(CultureInfo.InvariantCulture)), "ToUpper"),
        ["Hits = Code.Length"] = (Set(x => x.Hits, x => x.Code.Length), "Length"),
        ["Hits = Hits.CompareTo(3)"] = (Set(x => x.Hits, x => x.Hits.CompareTo(3)), "CompareTo"),
    };

    /// <summary>
    /// An integer narrowed to its own column's type: exact while it fits, and on overflow the statement fails (C# wraps).
    /// TASK-890: translated on every provider; SQLite, which stores an out-of-range integer silently, refused it until it
    /// range-checked the narrowed value (<c>SqlValueFragmentDialect.NarrowedInteger</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, SetCase> SetNarrowing = new Dictionary<string, SetCase>
    {
        ["Sh = (short)(Sh + 1)"] = Set(x => x.Sh, x => (short)(x.Sh + 1)),
        ["Sh = (short)(Sh * 2)"] = Set(x => x.Sh, x => (short)(x.Sh * 2)),
        ["Hits = (int)Big"] = Set(x => x.Hits, x => (int)x.Big),
    };

    public static IEnumerable<object[]> SetNarrowingNames => SetNarrowing.Keys.Select(k => new object[] { k });

    public static IEnumerable<object[]> SetRefusedNames => SetRefused.Keys.Select(k => new object[] { k });

    private static readonly (string Code, string? Name, decimal Amount, decimal? Optional, int Hits, int? Score, long Big, double Ratio,
        DateTime At, CallCastStatus Status, bool Flag, bool? OptFlag, char Letter)[] Rows =
    {
        ("Abc", "Abc", 10.5m, 10.5m, 3, 3, 3L, 0.5, new DateTime(2026, 3, 9, 8, 30, 0), CallCastStatus.Open, true, true, 'a'),
        ("abd", null, 2.25m, null, 2, null, 40L, 2.25, new DateTime(2026, 3, 9, 13, 0, 0), CallCastStatus.Closed, false, null, 'b'),
        ("xyz", "", -3.75m, -3.75m, -3, 5, -2L, 1.75, new DateTime(2025, 12, 31, 23, 59, 0), CallCastStatus.Draft, true, false, 'A'),
        (" pad ", "Pad", 7m, null, 7, 0, 7L, 3.5, new DateTime(2026, 1, 5, 12, 0, 0), CallCastStatus.Open, false, true, 'z'),
        ("B", "b", 0m, 0m, 0, null, 0L, 0.25, new DateTime(2026, 3, 10, 0, 0, 0), CallCastStatus.Draft, false, false, 'a'),
        ("a_b", "a_b", 1.5m, 1.5m, 5, 5, 5L, 1.25, new DateTime(2026, 3, 8, 23, 59, 59), CallCastStatus.Closed, true, null, 'q'),
        ("MiXeD", "x", 12m, 2.5m, -2, 2, 1L, 6.0, new DateTime(2024, 2, 29, 6, 0, 0), CallCastStatus.Open, true, true, 'm'),
        ("abc1", "abc", 3m, 3m, 4, 7, -5L, 0.75, new DateTime(2026, 7, 1, 18, 15, 0), CallCastStatus.Draft, false, true, 'c'),
    };

    private async Task<(IAsyncBulkStore<CallCastRow> Store, List<CallCastRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = new List<CallCastRow>();
        for (var i = 0; i < Rows.Length; i++)
        {
            var r = Rows[i];
            var row = new CallCastRow
            {
                Guid = i == 0 ? FirstGuid : Guid.NewGuid(),
                Code = r.Code, Name = r.Name, Amount = r.Amount, Optional = r.Optional, Hits = r.Hits, Score = r.Score,
                Big = r.Big, Ratio = r.Ratio, At = r.At, Status = r.Status, Flag = r.Flag, OptFlag = r.OptFlag, Letter = r.Letter,
                F = (float)(i * 0.75 - 1.5), Sh = (short)(i * 3 - 7),
            };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    private static List<string> Codes(IEnumerable<CallCastRow> rows)
        => rows.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(TranslatedNames))]
    public async Task Contract_A_translated_call_or_cast_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Translated[shape];
        var expected = Codes(seed.Where(filter.Compile()));

        Codes(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected, $"'{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");

        await store.DeleteAsync(filter);
        Codes(await store.ReadAsync(x => x.Code != null, null, null, null))
            .Should().Equal(Codes(seed.Where(r => !filter.Compile()(r))), $"'{shape}' must delete the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Contract_An_untranslatable_call_or_cast_is_refused_by_name_before_it_runs(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var (filter, named) = Refused[shape];

        var read = () => store.ReadAsync(filter, null, null, null);
        (await read.Should().ThrowAsync<NotSupportedException>($"'{shape}' has no exact SQL form"))
            .Which.Message.Should().Contain(named);

        var count = () => store.CountAsync(filter);
        await count.Should().ThrowAsync<NotSupportedException>();

        var delete = () => store.DeleteAsync(filter);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await store.CountAsync(x => x.Code != null)).Should().Be(seed.Count, "the refused delete never ran");
    }

    private static AbstractConnector ConnectorOf(IAsyncBulkStore<CallCastRow> store)
        => ((ISqlConnectedStore)store).Connector ?? throw new InvalidOperationException("The store has no connector.");

    private static readonly Expression<Func<CallCastRow, bool>> EveryRow = x => x.Code != null;

    [Theory]
    [MemberData(nameof(SetTranslatedNames))]
    public async Task Contract_A_translated_SET_value_writes_what_CSharp_writes(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var setCase = SetTranslated[shape];

        setCase.Run(ConnectorOf(store), EveryRow);
        foreach (var row in seed) setCase.Oracle(row);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code);
        foreach (var row in seed)
            setCase.Read(stored[row.Code]).Should().Be(setCase.Read(row), $"'{shape}' must write what C# writes for '{row.Code}'");
    }

    [Theory]
    [MemberData(nameof(SetRefusedNames))]
    public async Task Contract_An_untranslatable_SET_value_is_refused_by_name_before_it_runs(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var (setCase, named) = SetRefused[shape];

        var run = () => setCase.Run(ConnectorOf(store), EveryRow);
        run.Should().Throw<NotSupportedException>($"'{shape}' has no exact SQL form").Which.Message.Should().Contain(named);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code);
        foreach (var row in seed)
            setCase.Read(stored[row.Code]).Should().Be(setCase.Read(row), $"the refused update of '{shape}' never ran");
    }

    /// <summary>
    /// TASK-860 — an update SET may narrow an integer to its column's type (<c>(short)(x.Sh + 1)</c>) only where an overflow is
    /// loud: C# wraps 32767 + 1 to -32768, and a provider that stored 32768 instead would write a value C# never computes.
    /// So the statement either writes what C# writes, or fails and writes nothing (TASK-890: it fails on every provider).
    /// </summary>
    [Fact]
    public async Task Contract_A_narrowing_SET_that_overflows_fails_and_writes_nothing()
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        seed[0].Sh = short.MaxValue;
        await store.UpdateAsync(seed[0]);
        var before = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code, x => x.Sh);

        var run = () => ConnectorOf(store).Update(typeof(CallCastRow),
            new Dictionary<Expression<Func<CallCastRow, short>>, Expression<Func<CallCastRow, short>>> { [x => x.Sh] = x => (short)(x.Sh + 1) },
            EveryRow);

        run.Should().Throw<Exception>("C# wraps the overflow, so the only exact answer left to SQL is to refuse it");
        (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code, x => x.Sh)
            .Should().Equal(before, "a refused or failed statement writes nothing");
    }

    [Theory]
    [MemberData(nameof(SetNarrowingNames))]
    public async Task Contract_A_narrowing_SET_writes_what_CSharp_writes(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var setCase = SetNarrowing[shape];

        setCase.Run(ConnectorOf(store), EveryRow);
        foreach (var row in seed) setCase.Oracle(row);

        var stored = (await store.ReadAsync(EveryRow, null, null, null)).ToDictionary(x => x.Code);
        foreach (var row in seed)
            setCase.Read(stored[row.Code]).Should().Be(setCase.Read(row), $"'{shape}' must leave what C# leaves for '{row.Code}'");
    }
}
