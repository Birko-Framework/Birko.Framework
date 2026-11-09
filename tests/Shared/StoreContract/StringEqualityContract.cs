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

/// <summary>The row TASK-873's contract reads: a text that is never null, one that may be, and an indexed one.</summary>
[Table("T873TextRows")]
public class StringEqualityRow : AbstractModel
{
    public string Text { get; set; } = string.Empty;

    public string? Opt { get; set; }

    /// <summary>The same text as <see cref="Text"/>, in a bounded, indexed column (a lookup by code or key).</summary>
    [MaxLengthField(64)]
    [IndexedField("IX_T873TextRows_Code")]
    public string Code { get; set; } = string.Empty;

    public int Hits { get; set; }
}

/// <summary>
/// TASK-873 — a string <c>==</c> / <c>!=</c> on a column, a string <c>IN</c> list (<c>Contains</c> on a collection), an
/// <c>Equals</c> without a comparison, a comparison with another string column and a case fold compared with a value all
/// give C#'s ordinal answer, not the column collation's, on reads, counts, a filtered update and a filtered delete; an
/// ordering comparison of strings stays refused.
/// <para>
/// Before (measured live, the user's decision 2026-11-09 that the default is exact): <c>=</c> followed the collation.
/// SQL Server's default <c>SQL_Latin1_General_CP1_CI_AS</c> ignores case, pads trailing spaces (<c>'abc ' = 'abc'</c>, and
/// so does every collation there, <c>BIN2</c> included) and expands <c>ß</c> to <c>ss</c>; MySQL's <c>utf8mb4_0900_ai_ci</c>
/// ignores case and accents and expands <c>ß</c>; SQLite and PostgreSQL's deterministic default were already exact.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class StringEqualityContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="StringEqualityRow"/>.</summary>
    protected abstract IAsyncBulkStore<StringEqualityRow> NewStore();

    /// <summary>
    /// Mixed case, precomposed and decomposed accents, <c>ß</c> / <c>ss</c>, trailing and leading spaces, the empty text and
    /// blanks, the Kelvin sign, dotless and dotted i, a titlecase digraph, a ligature, <c>æ</c> / <c>ae</c>, a no-break space,
    /// a tab, supplementary-plane letters, LIKE metacharacters.
    /// </summary>
    public static readonly string[] Texts =
    {
        "abc", "ABC", "Abc", "abc ", " abc", "abc  ", "café", "café", "CAFÉ", "cafe", "straße", "strasse", "STRASSE",
        "ß", "ss", "SS", "", " ", "  ", "K", "k", "K", "ı", "i", "I", "İ", "ǅ", "ǆ", "Ǆ", "æ", "ae", "ﬁ", "fi", "a\tb",
        "a b", "a b", "\U00010400", "\U00010428", "ä", "a", "Ä", "x%", "x_",
    };

    /// <summary>The values <c>==</c> and <c>!=</c> are asked with.</summary>
    public static readonly string[] Terms =
    {
        "abc", "ABC", "abc ", "abc  ", "café", "cafe", "ß", "ss", "", " ", "K", "ı", "i", "ǅ", "æ", "fi", "a b",
        "\U00010428", "ä", "x%",
    };

    private static string Escape(string term)
        => string.Concat(term.Select(c => c < 32 || c == ' ' || c > 126 ? $"\\u{(int)c:X4}" : c.ToString()));

    private static readonly string Captured = "abc";
    private static readonly string? NullText = null;
    private static readonly bool T = true;
    private static readonly string[] SomeTexts = { "abc", "ß", "café" };
    private static readonly List<string> SomeList = new() { "ABC", "cafe", "" };
    private static readonly string?[] WithNull = { "abc", null };
    private static readonly string[] Blanks = { "", " " };

    /// <summary>
    /// A long list (1503 values, under SQL Server's 2098-parameter limit, past which a string list is refused, TASK-833):
    /// <c>ABC</c>, <c>ß</c> and <c>abc </c>, which the collations matched to other rows, and junk.
    /// </summary>
    private static readonly string[] LongList =
        Enumerable.Range(0, 1500).Select(i => "junk" + i).Concat(new[] { "ABC", "ß", "abc " }).ToArray();

    /// <summary>Every shape, named.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<StringEqualityRow, bool>>> Shapes =
        Terms.SelectMany(term => new (string, Expression<Func<StringEqualityRow, bool>>)[]
            {
                ($"Text == \"{Escape(term)}\"", x => x.Text == term),
                ($"Text != \"{Escape(term)}\"", x => x.Text != term),
                ($"Code == \"{Escape(term)}\"", x => x.Code == term),
                ($"Opt == \"{Escape(term)}\"", x => x.Opt == term),
                ($"Opt != \"{Escape(term)}\"", x => x.Opt != term),
            })
            .Concat(new (string, Expression<Func<StringEqualityRow, bool>>)[]
            {
                // The value on the left, a captured variable, Equals without a comparison (rewritten to ==, TASK-860).
                ("\"abc\" == Text", x => "abc" == x.Text),
                ("captured == Code", x => Captured == x.Code),
                ("Text.Equals(\"abc\")", x => x.Text.Equals("abc")),
                ("string.Equals(Text, \"ABC\")", x => string.Equals(x.Text, "ABC")),
                ("string.Equals(\"ß\", Code)", x => string.Equals("ß", x.Code)),
                ("!Text.Equals(\"abc\")", x => !x.Text.Equals("abc")),
                ("Opt.Equals(\"abc\") (Opt non-null)", x => x.Opt != null && x.Opt.Equals("abc")),
                // IN lists.
                ("{abc, ß, café}.Contains(Text)", x => SomeTexts.Contains(x.Text)),
                ("!{abc, ß, café}.Contains(Text)", x => !SomeTexts.Contains(x.Text)),
                ("{abc, ß, café}.Contains(Code)", x => SomeTexts.Contains(x.Code)),
                ("!{abc, ß, café}.Contains(Code)", x => !SomeTexts.Contains(x.Code)),
                ("List{ABC, cafe, \"\"}.Contains(Text)", x => SomeList.Contains(x.Text)),
                ("{abc, null}.Contains(Opt)", x => WithNull.Contains(x.Opt)),
                ("!{abc, null}.Contains(Opt)", x => !WithNull.Contains(x.Opt)),
                ("{\"\", \" \"}.Contains(Text)", x => Blanks.Contains(x.Text)),
                ("long list.Contains(Text)", x => LongList.Contains(x.Text)),
                ("long list.Contains(Code)", x => LongList.Contains(x.Code)),
                // Column against column.
                ("Text == Opt", x => x.Text == x.Opt),
                ("Text != Opt", x => x.Text != x.Opt),
                ("Opt == Code", x => x.Opt == x.Code),
                ("!(Opt == Text)", x => !(x.Opt == x.Text)),
                // A case fold, a value fragment, IsNullOrEmpty.
                ("Text.ToLower() == \"abc\"", x => x.Text.ToLower() == "abc"),
                ("Text.ToUpperInvariant() != \"ABC\"", x => x.Text.ToUpperInvariant() != "ABC"),
                ("(Opt ?? \"\") == \"\"", x => (x.Opt ?? "") == ""),
                ("(Opt ?? \"x\") != \"abc\"", x => (x.Opt ?? "x") != "abc"),
                ("(Hits > 3 ? Text : Code) == \"abc\"", x => (x.Hits > 3 ? x.Text : x.Code) == "abc"),
                ("string.IsNullOrEmpty(Opt)", x => string.IsNullOrEmpty(x.Opt)),
                ("!string.IsNullOrEmpty(Text)", x => !string.IsNullOrEmpty(x.Text)),
                // Combined, negated, collapsed, compared with a boolean.
                ("Text == \"abc\" || Code == \"ß\"", x => x.Text == "abc" || x.Code == "ß"),
                ("Text == \"abc\" && Hits >= 0", x => x.Text == "abc" && x.Hits >= 0),
                ("!(Text == \"ABC\" && Hits > 0)", x => !(x.Text == "ABC" && x.Hits > 0)),
                ("!(Code == \"abc\" && T)", x => !(x.Code == "abc" && T)),
                ("(Text == \"abc\") == false", x => (x.Text == "abc") == false),
                ("Hits > 1 && !(Opt == \"ABC\" || Opt == null)", x => x.Hits > 1 && !(x.Opt == "ABC" || x.Opt == NullText)),
            })
            .ToDictionary(c => c.Item1, c => c.Item2);

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    /// <summary>String orderings: no SQL collation orders as C#'s ordinal comparison does, so they stay refused (TASK-860).</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<StringEqualityRow, bool>>> Refused =
        new Dictionary<string, Expression<Func<StringEqualityRow, bool>>>
        {
            ["string.Compare(Text, \"b\") > 0"] = x => string.Compare(x.Text, "b") > 0,
            ["string.CompareOrdinal(Text, \"b\") < 0"] = x => string.CompareOrdinal(x.Text, "b") < 0,
            ["Text.CompareTo(\"b\") >= 0"] = x => x.Text.CompareTo("b") >= 0,
            ["string.Compare(Code, \"b\", Ordinal) == 0"] = x => string.Compare(x.Code, "b", StringComparison.Ordinal) == 0,
        };

    public static IEnumerable<object[]> RefusedNames => Refused.Keys.Select(k => new object[] { k });

    private async Task<(IAsyncBulkStore<StringEqualityRow> Store, List<StringEqualityRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = Texts.Select((text, i) => new StringEqualityRow
        {
            Guid = Guid.NewGuid(),
            Text = text,
            Code = text,
            Opt = i % 3 == 0 ? null : i % 3 == 1 ? text : text.ToUpperInvariant(),
            Hits = i % 7,
        }).ToList();
        await store.CreateAsync(seed);
        return (store, seed);
    }

    private static List<string> Keys(IEnumerable<StringEqualityRow> rows)
        => rows.Select(x => Escape(x.Text) + "|" + Escape(x.Opt ?? "<null>")).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static readonly Expression<Func<StringEqualityRow, bool>> EveryRow = x => x.Hits >= 0;

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task Contract_A_string_equality_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Shapes[shape];
        var compiled = filter.Compile();

        var expected = Keys(seed.Where(compiled));
        Keys(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected, $"'{shape}' must read the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");

        await store.UpdateAsync(filter, new PropertyUpdate<StringEqualityRow>().Set(x => x.Hits, 100));
        Keys(await store.ReadAsync(x => x.Hits == 100, null, null, null))
            .Should().Equal(expected, $"'{shape}' must update the rows C# matches");

        await store.DeleteAsync(filter);
        Keys(await store.ReadAsync(EveryRow, null, null, null))
            .Should().Equal(Keys(seed.Where(r => !compiled(r))), $"'{shape}' must delete the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Contract_A_string_ordering_is_refused_before_it_runs(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Refused[shape];

        var read = () => store.ReadAsync(filter, null, null, null);
        await read.Should().ThrowAsync<NotSupportedException>($"'{shape}' has no exact SQL form");
        var delete = () => store.DeleteAsync(filter);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await store.CountAsync(EveryRow)).Should().Be(seed.Count, "the refused delete never ran");
    }

    /// <summary>
    /// The exact term must not make a long list slow. Measured by the independent review on SQL Server, 20,000 rows and an
    /// indexed <c>NVARCHAR(64)</c>: with both operands cast to <c>NVARCHAR(MAX)</c>, 1000 values took 18.3 s (36 ms before
    /// TASK-873), 2000 timed out, and a 1000-value <c>NOT IN</c> 730 s. The budget is generous (a few hundred ms is normal)
    /// and only catches that shape.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Contract_A_long_list_over_an_indexed_column_of_many_rows_stays_fast(bool negated)
    {
        if (!Ready()) return;
        var store = NewStore();
        var rows = Enumerable.Range(0, 20000)
            .Select(i => new StringEqualityRow { Guid = Guid.NewGuid(), Text = "T" + i, Code = "C" + i, Hits = i % 7 }).ToList();
        foreach (var chunk in rows.Chunk(2000)) await store.CreateAsync(chunk);
        // 1000 values: every 20th code exactly, and its upper-case and trailing-space twins, which C# does not match.
        var ids = Enumerable.Range(0, 334).SelectMany(i => new[] { "C" + i * 20, "c" + i * 20, "C" + i * 20 + " " }).Take(1000).ToArray();
        Expression<Func<StringEqualityRow, bool>> filter = negated ? x => !ids.Contains(x.Code) : x => ids.Contains(x.Code);
        var expected = rows.Count(filter.Compile());

        var watch = System.Diagnostics.Stopwatch.StartNew();
        (await store.CountAsync(filter)).Should().Be(expected);
        (await store.ReadAsync(filter, null, null, null)).Count().Should().Be(expected);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "a 1000-value list over 20,000 rows is a fraction of a second");
    }

    /// <summary>
    /// A value of exactly 4000 characters and its trailing-space twins, in the unbounded <see cref="StringEqualityRow.Text"/>
    /// (SQL Server: <c>NVARCHAR(MAX)</c>). SQL Server cuts a concatenation of two non-<c>MAX</c> strings at 4000 characters, so
    /// the exact term's terminator is lost on a 4000-character parameter; the value is offered cut and uncut.
    /// </summary>
    [Theory]
    [InlineData(3999)]
    [InlineData(4000)]
    [InlineData(4001)]
    public async Task Contract_A_value_near_4000_characters_matches_what_CSharp_matches(int length)
    {
        if (!Ready()) return;
        var store = NewStore();
        var value = new string('a', length);
        var texts = new[] { value, value + " ", value.Substring(0, length - 1), value.Substring(0, length - 1) + " ", "a" };
        await store.CreateAsync(texts.Select((t, i) => new StringEqualityRow { Guid = Guid.NewGuid(), Text = t, Code = "C" + i }).ToList());

        (await store.CountAsync(x => x.Text == value)).Should().Be(1, $"Text == {length} × 'a' matches one row");
        (await store.CountAsync(x => x.Text != value)).Should().Be(texts.Length - 1);
        var list = new[] { value, "zzz" };
        (await store.CountAsync(x => list.Contains(x.Text))).Should().Be(1);
    }
}
