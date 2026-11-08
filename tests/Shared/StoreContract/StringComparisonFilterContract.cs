using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>The row TASK-869's contract reads: a text that is never null, and one that may be.</summary>
[Table("T869TextRows")]
public class StringMatchRow : AbstractModel
{
    public string Text { get; set; } = string.Empty;

    public string? Opt { get; set; }

    public int Hits { get; set; }
}

/// <summary>How a provider's default collation compares text, for the pinned no-argument forms.</summary>
public enum DefaultTextCollation
{
    /// <summary>Code-point exact (PostgreSQL's deterministic default).</summary>
    Binary,

    /// <summary>ASCII letters case-insensitive, everything else exact (SQLite's <c>LIKE</c>).</summary>
    AsciiCaseInsensitive,

    /// <summary>Case-insensitive, accent-sensitive (SQL Server's <c>SQL_Latin1_General_CP1_CI_AS</c>).</summary>
    CaseInsensitive,

    /// <summary>Case- and accent-insensitive (MySQL 8's <c>utf8mb4_0900_ai_ci</c>).</summary>
    CaseAndAccentInsensitive,
}

/// <summary>
/// TASK-869 — a string <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> / <c>Equals</c> with
/// <see cref="StringComparison.Ordinal"/> or <see cref="StringComparison.OrdinalIgnoreCase"/> matches exactly what C# matches,
/// on reads, counts, a filtered update and a filtered delete; any other comparison is refused by name before anything runs;
/// the no-argument forms follow the column's collation, pinned per provider.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the comparison argument was ignored and the column's
/// collation decided, so <c>Contains("caf", Ordinal)</c> matched <c>CAFÉ</c> on SQLite, MySQL and SQL Server, MySQL matched
/// <c>cafe</c> for <c>café</c>, and <c>Contains("caf", OrdinalIgnoreCase)</c> missed <c>CAF</c> on PostgreSQL. A string
/// <c>Equals</c> with a comparison was refused (TASK-860).
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class StringComparisonFilterContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="StringMatchRow"/>.</summary>
    protected abstract IAsyncBulkStore<StringMatchRow> NewStore();

    /// <summary>How this provider's default collation compares, for <see cref="Contract_The_no_argument_forms_follow_the_column_collation"/>.</summary>
    protected abstract DefaultTextCollation Collation { get; }

    /// <summary>
    /// False where an OrdinalIgnoreCase term holding a cased letter outside the BMP is refused (SQL Server: its LIKE has no
    /// class for a surrogate pair).
    /// </summary>
    protected virtual bool MatchesSupplementaryCaseClasses => true;

    /// <summary>
    /// Mixed case, accents (precomposed and decomposed), Greek final sigma, the dotless i and long s C# keeps apart from
    /// I and S, the Kelvin sign, a titlecase digraph, micro sign, a supplementary-plane letter, LIKE and regex
    /// metacharacters, a trailing space, a line break.
    /// </summary>
    public static readonly string[] Texts =
    {
        "café", "cafe", "CAFÉ", "Café", "é", "Äpfel", "äpfel", "ΣΟΦΙΑΣ", "σοφιας", "σοφιaς", "a%b", "a_b", "axb", "a[b]",
        "a!b", "a.b", "a\\b", "a$b", "straße", "STRASSE", "ı", "I", "i", "ſ", "s", "pad ", "pad", "Abc", "abc", "ABC", "ǅ",
        "ǆ", "Ǆ", "K", "k", "µ", "μ", "\U00010400", "\U00010428x", "a\nb", "ab\n",
    };

    /// <summary>The terms each kind and comparison is asked with.</summary>
    public static readonly string[] Terms =
    {
        "caf", "CAFÉ", "é", "ä", "Σ", "ς", "%", "_", "[b]", "!", ".", "\\", "$", "i", "I", "ı", "s", "ſ", "ss", "pad", "pad ",
        "", "k", "µ", "ǅ", "\U00010428", "\n", "b",
    };

    private static readonly string[] Kinds = { "Contains", "StartsWith", "EndsWith", "Equals" };
    private static readonly StringComparison[] Translated = { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase };

    private static Expression<Func<StringMatchRow, bool>> Shape(string kind, string term, StringComparison comparison) => kind switch
    {
        "Contains" => x => x.Text.Contains(term, comparison),
        "StartsWith" => x => x.Text.StartsWith(term, comparison),
        "EndsWith" => x => x.Text.EndsWith(term, comparison),
        _ => x => x.Text.Equals(term, comparison),
    };

    private static string Escape(string term)
        => string.Concat(term.Select(c => c < 32 ? $"\\x{(int)c:X2}" : c.ToString()));

    /// <summary>Every kind × Ordinal / OrdinalIgnoreCase × term, named "Kind(term, Comparison)".</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<StringMatchRow, bool>>> Exact =
        Kinds.SelectMany(kind => Translated.SelectMany(comparison => Terms.Select(term =>
                (Name: $"{kind}(\"{Escape(term)}\", {comparison})", Filter: Shape(kind, term, comparison)))))
            .Concat(new (string, Expression<Func<StringMatchRow, bool>>)[]
            {
                // Negated, combined, the static form, the char overload, a case fold and a captured comparison.
                ("!Contains(\"caf\", Ordinal)", x => !x.Text.Contains("caf", StringComparison.Ordinal)),
                ("!StartsWith(\"A\", OrdinalIgnoreCase)", x => !x.Text.StartsWith("A", StringComparison.OrdinalIgnoreCase)),
                ("Contains(\"a\", Ordinal) && Hits > 3", x => x.Text.Contains("a", StringComparison.Ordinal) && x.Hits > 3),
                ("Contains(\"É\", OIC) || EndsWith(\"c\", Ordinal)",
                    x => x.Text.Contains("É", StringComparison.OrdinalIgnoreCase) || x.Text.EndsWith("c", StringComparison.Ordinal)),
                ("string.Equals(Text, \"abc\", OrdinalIgnoreCase)", x => string.Equals(x.Text, "abc", StringComparison.OrdinalIgnoreCase)),
                ("string.Equals(\"abc\", Text, Ordinal)", x => string.Equals("abc", x.Text, StringComparison.Ordinal)),
                ("!Text.Equals(\"abc\", OrdinalIgnoreCase)", x => !x.Text.Equals("abc", StringComparison.OrdinalIgnoreCase)),
                ("Text.Equals(\"abc\", OIC) == false", x => x.Text.Equals("abc", StringComparison.OrdinalIgnoreCase) == false),
                ("Contains('A', OrdinalIgnoreCase)", x => x.Text.Contains('A', StringComparison.OrdinalIgnoreCase)),
                ("Contains('a', Ordinal)", x => x.Text.Contains('a', StringComparison.Ordinal)),
                ("Text.ToUpper().Contains(\"CAF\", Ordinal)", x => x.Text.ToUpper().Contains("CAF", StringComparison.Ordinal)),
                ("Contains(\"caf\", captured Ordinal)", x => x.Text.Contains("caf", CapturedOrdinal)),
                // A nullable column: as in C#, string.Equals(null, v) is false, so its negation holds for the NULL rows.
                ("string.Equals(Opt, \"abc\", OIC)", x => string.Equals(x.Opt, "abc", StringComparison.OrdinalIgnoreCase)),
                ("!string.Equals(Opt, \"abc\", OIC)", x => !string.Equals(x.Opt, "abc", StringComparison.OrdinalIgnoreCase)),
                ("!string.Equals(Opt, \"abc\", Ordinal)", x => !string.Equals(x.Opt, "abc", StringComparison.Ordinal)),
                ("string.Equals(Opt, null, Ordinal)", x => string.Equals(x.Opt, NullText, StringComparison.Ordinal)),
                ("!string.Equals(Opt, null, OIC)", x => !string.Equals(x.Opt, NullText, StringComparison.OrdinalIgnoreCase)),
            })
            .Concat(Collapsed())
            .ToDictionary(c => c.Item1, c => c.Item2);

    private static readonly bool T = true;
    private static readonly bool F = false;

    /// <summary>
    /// A group that collapses onto its one non-constant child (the parser folds a constant-bool sibling away): the child's
    /// comparison must survive the collapse. Measured before the fix: the collapse dropped it, and the filter fell back to
    /// the collation's LIKE (SQLite and PostgreSQL deleted rows C# keeps) or, for Equals, threw InvalidOperationException.
    /// </summary>
    private static IEnumerable<(string, Expression<Func<StringMatchRow, bool>>)> Collapsed()
    {
        const System.Reflection.BindingFlags Static = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var t = Expression.Field(null, typeof(StringComparisonFilterContract).GetField(nameof(T), Static)!);
        var f = Expression.Field(null, typeof(StringComparisonFilterContract).GetField(nameof(F), Static)!);
        var calls = new (string Kind, string Term)[] { ("Contains", "caf"), ("StartsWith", "a"), ("EndsWith", "C"), ("Equals", "abc") };
        foreach (var (kind, term) in calls)
        foreach (var comparison in Translated)
        {
            var call = Shape(kind, term, comparison);
            var x = call.Parameters[0];
            var hits = Expression.GreaterThanOrEqual(Expression.Property(x, nameof(StringMatchRow.Hits)), Expression.Constant(0));
            var name = $"{kind}(\"{term}\", {comparison})";
            yield return ($"!({name} && T)", Expression.Lambda<Func<StringMatchRow, bool>>(Expression.Not(Expression.AndAlso(call.Body, t)), x));
            yield return ($"!(F || {name})", Expression.Lambda<Func<StringMatchRow, bool>>(Expression.Not(Expression.OrElse(f, call.Body)), x));
            yield return ($"Hits >= 0 && !({name} && T)",
                Expression.Lambda<Func<StringMatchRow, bool>>(Expression.AndAlso(hits, Expression.Not(Expression.AndAlso(call.Body, t))), x));
            yield return ($"({name} && T) || F", Expression.Lambda<Func<StringMatchRow, bool>>(Expression.OrElse(Expression.AndAlso(call.Body, t), f), x));
        }
    }

    private static readonly StringComparison CapturedOrdinal = StringComparison.Ordinal;
    private static readonly string? NullText = null;

    public static IEnumerable<object[]> ExactNames => Exact.Keys.Select(k => new object[] { k });

    /// <summary>Comparisons with no exact SQL form, each with the name its refusal must carry.</summary>
    public static readonly IReadOnlyDictionary<string, (Expression<Func<StringMatchRow, bool>> Filter, string Named)> Refused =
        new Dictionary<string, (Expression<Func<StringMatchRow, bool>>, string)>
        {
            ["Contains(\"caf\", CurrentCulture)"] = (x => x.Text.Contains("caf", StringComparison.CurrentCulture), "CurrentCulture"),
            ["Contains(\"caf\", CurrentCultureIgnoreCase)"] = (x => x.Text.Contains("caf", StringComparison.CurrentCultureIgnoreCase), "CurrentCultureIgnoreCase"),
            ["StartsWith(\"caf\", InvariantCulture)"] = (x => x.Text.StartsWith("caf", StringComparison.InvariantCulture), "InvariantCulture"),
            ["EndsWith(\"É\", InvariantCultureIgnoreCase)"] = (x => x.Text.EndsWith("É", StringComparison.InvariantCultureIgnoreCase), "InvariantCultureIgnoreCase"),
            ["Equals(\"abc\", CurrentCultureIgnoreCase)"] = (x => x.Text.Equals("abc", StringComparison.CurrentCultureIgnoreCase), "CurrentCultureIgnoreCase"),
            ["string.Equals(Text, \"abc\", InvariantCulture)"] = (x => string.Equals(x.Text, "abc", StringComparison.InvariantCulture), "InvariantCulture"),
            ["StartsWith(\"a\", true, InvariantCulture)"] = (x => x.Text.StartsWith("a", true, CultureInfo.InvariantCulture), "CultureInfo"),
            ["EndsWith(\"a\", false, CurrentCulture)"] = (x => x.Text.EndsWith("a", false, CultureInfo.CurrentCulture), "CultureInfo"),
            ["Contains(null, Ordinal)"] = (x => x.Text.Contains(NullText!, StringComparison.Ordinal), "null"),
            ["string.Equals(Text, Opt, Ordinal)"] = (x => string.Equals(x.Text, x.Opt, StringComparison.Ordinal), "both strings"),
            ["Text.Trim().Equals(\"pad\", Ordinal)"] = (x => x.Text.Trim().Equals("pad", StringComparison.Ordinal), "Equals"),
        };

    public static IEnumerable<object[]> RefusedNames => Refused.Keys.Select(k => new object[] { k });

    /// <summary>The no-argument forms, over the rows where every collation's behaviour is plain (no ß, no decomposed accent).</summary>
    public static readonly IReadOnlyDictionary<string, (Expression<Func<StringMatchRow, bool>> Filter, string Kind, string Term)> Collated =
        new Dictionary<string, (Expression<Func<StringMatchRow, bool>>, string, string)>
        {
            ["Contains(\"caf\")"] = (x => x.Text.Contains("caf"), "Contains", "caf"),
            ["Contains(\"CAF\")"] = (x => x.Text.Contains("CAF"), "Contains", "CAF"),
            ["Contains(\"é\")"] = (x => x.Text.Contains("é"), "Contains", "é"),
            ["StartsWith(\"café\")"] = (x => x.Text.StartsWith("café"), "StartsWith", "café"),
            ["StartsWith(\"äp\")"] = (x => x.Text.StartsWith("äp"), "StartsWith", "äp"),
            ["EndsWith(\"É\")"] = (x => x.Text.EndsWith("É"), "EndsWith", "É"),
            ["EndsWith(\"BC\")"] = (x => x.Text.EndsWith("BC"), "EndsWith", "BC"),
        };

    public static IEnumerable<object[]> CollatedNames => Collated.Keys.Select(k => new object[] { k });

    private static readonly HashSet<string> PlainTexts = new(StringComparer.Ordinal)
    {
        "café", "cafe", "CAFÉ", "Café", "Äpfel", "äpfel", "Abc", "abc", "ABC", "pad", "axb",
    };

    private async Task<(IAsyncBulkStore<StringMatchRow> Store, List<StringMatchRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = Texts.Select((text, i) => new StringMatchRow
        {
            Guid = Guid.NewGuid(),
            Text = text,
            Opt = i % 3 == 0 ? null : i % 3 == 1 ? text : text.ToUpperInvariant(),
            Hits = i % 7,
        }).ToList();
        await store.CreateAsync(seed);
        return (store, seed);
    }

    private static List<string> TextsOf(IEnumerable<StringMatchRow> rows)
        => rows.Select(x => x.Text).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static readonly Expression<Func<StringMatchRow, bool>> EveryRow = x => x.Hits >= 0;

    /// <summary>True when <paramref name="shape"/> holds a cased letter outside the BMP under OrdinalIgnoreCase.</summary>
    private static bool IsSupplementaryCaseClass(string shape)
        => shape.Contains("OrdinalIgnoreCase", StringComparison.Ordinal) && shape.Contains("\U00010428", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(ExactNames))]
    public async Task Contract_An_ordinal_comparison_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Exact[shape];
        var compiled = filter.Compile();

        if (!MatchesSupplementaryCaseClasses && IsSupplementaryCaseClass(shape))
        {
            var refused = () => store.ReadAsync(filter, null, null, null);
            (await refused.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("Basic Multilingual Plane");
            return;
        }

        var expected = TextsOf(seed.Where(compiled));
        TextsOf(await store.ReadAsync(filter, null, null, null)).Should().Equal(expected, $"'{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");

        await store.UpdateAsync(filter, new PropertyUpdate<StringMatchRow>().Set(x => x.Hits, 100));
        TextsOf(await store.ReadAsync(x => x.Hits == 100, null, null, null))
            .Should().Equal(expected, $"'{shape}' must update the rows C# matches");

        await store.DeleteAsync(filter);
        TextsOf(await store.ReadAsync(EveryRow, null, null, null))
            .Should().Equal(TextsOf(seed.Where(r => !compiled(r))), $"'{shape}' must delete the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Contract_A_comparison_with_no_exact_SQL_form_is_refused_by_name_before_it_runs(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var (filter, named) = Refused[shape];

        var read = () => store.ReadAsync(filter, null, null, null);
        (await read.Should().ThrowAsync<NotSupportedException>($"'{shape}' has no exact SQL form"))
            .Which.Message.Should().Contain(named);

        var count = () => store.CountAsync(filter);
        await count.Should().ThrowAsync<NotSupportedException>();

        var update = () => store.UpdateAsync(filter, new PropertyUpdate<StringMatchRow>().Set(x => x.Hits, 100));
        await update.Should().ThrowAsync<NotSupportedException>();

        var delete = () => store.DeleteAsync(filter);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await store.CountAsync(EveryRow)).Should().Be(seed.Count, "the refused delete never ran");
        (await store.CountAsync(x => x.Hits == 100)).Should().Be(0, "the refused update never ran");
    }

    /// <summary>The longest LIKE pattern this provider accepts, or null where none applies (SQL Server: 4000).</summary>
    protected virtual int? MaxLikePatternLength => null;

    /// <summary>
    /// A long OrdinalIgnoreCase term: SQL Server's LIKE takes 4000 pattern characters (Msg 8152 past it, measured, also as
    /// nvarchar(max)), and each cased letter costs four ([Aa]); past the limit it is refused by name before anything runs,
    /// below it, and on every other provider, it matches what C# matches.
    /// </summary>
    [Theory]
    [InlineData(999)]
    [InlineData(1001)]
    public async Task Contract_A_long_OrdinalIgnoreCase_term_matches_or_is_refused_by_its_limit(int letters)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var term = "ab" + new string('a', letters - 2);
        await store.CreateAsync(new StringMatchRow { Guid = Guid.NewGuid(), Text = "x" + term.ToUpperInvariant() + "y", Hits = 1 });
        Expression<Func<StringMatchRow, bool>> filter = x => x.Text.Contains(term, StringComparison.OrdinalIgnoreCase);

        if (MaxLikePatternLength is { } limit && letters * 4 + 2 > limit)
        {
            var read = () => store.ReadAsync(filter, null, null, null);
            (await read.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(limit.ToString(CultureInfo.InvariantCulture));
            var delete = () => store.DeleteAsync(filter);
            await delete.Should().ThrowAsync<NotSupportedException>();
            (await store.CountAsync(EveryRow)).Should().Be(seed.Count + 1, "the refused delete never ran");
            return;
        }
        (await store.CountAsync(filter)).Should().Be(1, "the one long row contains the term, ignoring case");
    }

    /// <summary>
    /// The user's decision (TASK-869, option A): without a comparison the LIKE family follows the column's collation, so
    /// these differ from C# on purpose and per provider. Pinned, so a change to it is seen.
    /// </summary>
    [Theory]
    [MemberData(nameof(CollatedNames))]
    public async Task Contract_The_no_argument_forms_follow_the_column_collation(string shape)
    {
        if (!Ready()) return;
        var (store, _) = await Seeded();
        var (filter, kind, term) = Collated[shape];

        var expected = PlainTexts.Where(text => CollationMatches(kind, Fold(text), Fold(term)))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        TextsOf(await store.ReadAsync(filter, null, null, null)).Where(PlainTexts.Contains).ToList()
            .Should().Equal(expected, $"'{shape}' follows the {Collation} collation");
    }

    private static bool CollationMatches(string kind, string text, string term) => kind switch
    {
        "Contains" => text.Contains(term, StringComparison.Ordinal),
        "StartsWith" => text.StartsWith(term, StringComparison.Ordinal),
        _ => text.EndsWith(term, StringComparison.Ordinal),
    };

    private string Fold(string text) => Collation switch
    {
        DefaultTextCollation.Binary => text,
        DefaultTextCollation.AsciiCaseInsensitive => string.Concat(text.Select(c => c < 128 ? char.ToLowerInvariant(c) : c)),
        DefaultTextCollation.CaseInsensitive => text.ToUpperInvariant(),
        _ => string.Concat(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant(),
    };
}
