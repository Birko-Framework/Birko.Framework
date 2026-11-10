using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>TASK-895's row: TASK-873's columns (an unbounded text, a nullable one, a bounded indexed code) and a counter.</summary>
[Table("T895TextRows")]
public class StringLeftoverRow : AbstractModel
{
    public string Text { get; set; } = string.Empty;

    public string? Opt { get; set; }

    [MaxLengthField(64)]
    [IndexedField("IX_T895TextRows_Code")]
    public string Code { get; set; } = string.Empty;

    public int Hits { get; set; }
}

/// <summary>
/// TASK-895 — the string-equality leftovers of TASK-873, against C# on every SQL provider:
/// <list type="bullet">
/// <item>a string <c>IN</c> list past the provider's parameter limit is split into chunks (the user's decision, 2026-11-10),
/// and the chunks stay disjoint (an increment applies once), the <c>IS NULL</c> leg of a list holding <c>null</c> included;
/// a <c>NOT IN</c> past the limit is still refused, as for every other type;</item>
/// <item>a rule's string <c>Equal</c> / <c>NotEqual</c> / <c>In</c> / <c>NotIn</c> is exact, as the lambda is (the user's
/// decision, 2026-11-10), and the in-memory <see cref="RuleEvaluator"/> answers the same;</item>
/// <item><c>string.Equals(x.Col, v, Ordinal)</c> renders as <c>==</c> does (no 4000-character LIKE limit on SQL Server), and
/// <c>string.Equals(x.A, x.B, Ordinal)</c> over two columns is translated instead of refused.</item>
/// </list>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class StringEqualityLeftoversContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="StringLeftoverRow"/> and its connector.</summary>
    protected abstract (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector) NewStore();

    /// <summary>The provider's parameter limit: a list longer than this cannot run as one statement.</summary>
    protected abstract int MaxParameters { get; }

    /// <summary>TASK-873's texts: case, accents, <c>ß</c> / <c>ss</c>, trailing and leading spaces, blanks, Kelvin, dotless i.</summary>
    private static readonly string[] Texts =
    {
        "abc", "ABC", "Abc", "abc ", " abc", "café", "café", "CAFÉ", "cafe", "straße", "strasse", "ß", "ss", "SS", "", " ",
        "K", "k", "K", "ı", "i", "I", "a b", "a b", "ä", "a", "x%",
    };

    private static List<StringLeftoverRow> Seed() => Texts.Select((text, i) => new StringLeftoverRow
    {
        Guid = Guid.NewGuid(),
        Text = text,
        Code = text,
        Opt = i % 3 == 0 ? null : i % 3 == 1 ? text : text.ToUpperInvariant(),
        Hits = i % 5,
    }).ToList();

    private (IBulkStore<StringLeftoverRow> Store, AbstractConnector Connector, List<StringLeftoverRow> Seed) Seeded()
    {
        var (store, connector) = NewStore();
        var seed = Seed();
        store.Create(seed);
        return (store, connector, seed);
    }

    private static string Escape(string term)
        => string.Concat(term.Select(c => c < 32 || c == ' ' || c > 126 ? $"\\u{(int)c:X4}" : c.ToString()));

    private static List<string> Keys(IEnumerable<StringLeftoverRow> rows)
        => rows.Select(x => Escape(x.Text) + "|" + Escape(x.Opt ?? "<null>") + "|" + x.Hits)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static List<string> Keys(IEnumerable<object> rows) => Keys(rows.Cast<StringLeftoverRow>());

    private static readonly Expression<Func<StringLeftoverRow, bool>> EveryRow = x => x.Hits >= 0;

    // ── (a) a string IN list past the parameter limit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="count"/> values: junk, then twins of the seeded texts that C# does not match (case, trailing space,
    /// unaccented, <c>ss</c> for <c>ß</c>) spread through the list, so they fall into different chunks than the texts.
    /// </summary>
    private static List<string?> LongList(int count, params string?[] texts)
    {
        var list = Enumerable.Range(0, count).Select(i => (string?)("junk" + i)).ToList();
        var twins = new[] { "aBc", "ABC ", "Cafe", "STRASSE", "s", "abc	" };
        for (var i = 0; i < twins.Length; i++) list[(i + 1) * count / (twins.Length + texts.Length + 2)] = twins[i];
        for (var i = 0; i < texts.Length; i++) list[(twins.Length + i + 1) * count / (twins.Length + texts.Length + 2)] = texts[i];
        return list;
    }

    public static IEnumerable<object[]> LongShapes()
    {
        yield return new object[] { "Code in a long list" };
        yield return new object[] { "Text in a long list" };
        yield return new object[] { "Opt in a long list holding null" };
        yield return new object[] { "Code in a long list, AND Hits >= 1" };
    }

    private Expression<Func<StringLeftoverRow, bool>> LongFilter(string shape)
    {
        var count = MaxParameters + 1500;
        switch (shape)
        {
            case "Code in a long list":
            {
                var list = LongList(count, "abc", "ß", "café", "K", "").Select(s => s!).ToArray();
                return x => list.Contains(x.Code);
            }
            case "Text in a long list":
            {
                // Duplicates and both spellings of é: distinct C# strings, one or two rows each.
                var list = LongList(count, "abc", "abc", "café", "café", " ", "ı").Select(s => s!).ToList();
                return x => list.Contains(x.Text);
            }
            case "Opt in a long list holding null":
            {
                var list = LongList(count, "ABC", null, "abc", "CAFÉ").ToArray();
                return x => list.Contains(x.Opt);
            }
            case "Code in a long list, AND Hits >= 1":
            {
                var list = LongList(count, "abc", "ABC", "ss", "k").Select(s => s!).ToArray();
                return x => list.Contains(x.Code) && x.Hits >= 1;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    [Theory]
    [MemberData(nameof(LongShapes))]
    public void Contract_A_string_list_past_the_parameter_limit_is_split_and_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, _, seed) = Seeded();
        var filter = LongFilter(shape);
        var compiled = filter.Compile();
        var expected = seed.Where(compiled).ToList();
        expected.Should().NotBeEmpty("the list names seeded texts");

        Keys(store.Read(filter, null, null, null)).Should().Equal(Keys(expected), $"'{shape}' must read the rows C# matches");
        store.Count(filter).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");

        // An increment: a row matched by two chunks would gain 2 (TASK-833's disjointness).
        store.Update(filter, new PropertyUpdate<StringLeftoverRow>().Increment(x => x.Hits, 10));
        foreach (var row in seed) row.Hits += compiled(row) ? 10 : 0;
        Keys(store.Read(EveryRow, null, null, null)).Should().Equal(Keys(seed), $"'{shape}' must increment each row C# matches once");

        store.Delete(filter);
        Keys(store.Read(EveryRow, null, null, null)).Should().Equal(Keys(seed.Where(r => !compiled(r))),
            $"'{shape}' must delete the rows C# matches");
    }

    /// <summary>A <c>NOT IN</c> is never split (TASK-833, the user's decision): past the limit it is refused, as for a Guid.</summary>
    [Fact]
    public void Contract_A_negated_string_list_past_the_parameter_limit_is_refused_before_anything_runs()
    {
        if (!Ready()) return;
        var (store, _, seed) = Seeded();
        var list = LongList(MaxParameters + 1500, "abc").Select(s => s!).ToArray();

        var read = () => store.Read(x => !list.Contains(x.Code), null, null, null).ToList();
        read.Should().Throw<NotSupportedException>().WithMessage("*NOT IN*");
        var delete = () => store.Delete(x => !list.Contains(x.Code));
        delete.Should().Throw<NotSupportedException>();
        store.Count(EveryRow).Should().Be(seed.Count, "the refused delete never ran");
    }

    // ── (b) a rule's string equality ───────────────────────────────────────────────────────────────────────────────

    private static Rule R(string field, ComparisonOperator op, object? value) => new(field, op, value);

    public static readonly IReadOnlyDictionary<string, Func<Rule>> RuleShapes = new Dictionary<string, Func<Rule>>
    {
        ["Text Equal abc"] = () => R("Text", ComparisonOperator.Equal, "abc"),
        ["Text Equal ABC"] = () => R("Text", ComparisonOperator.Equal, "ABC"),
        ["Text Equal 'abc '"] = () => R("Text", ComparisonOperator.Equal, "abc "),
        ["Code Equal ss"] = () => R("Code", ComparisonOperator.Equal, "ss"),
        ["Code Equal café"] = () => R("Code", ComparisonOperator.Equal, "café"),
        ["Code Equal ''"] = () => R("Code", ComparisonOperator.Equal, ""),
        ["Text NotEqual abc"] = () => R("Text", ComparisonOperator.NotEqual, "abc"),
        ["Opt NotEqual ABC"] = () => R("Opt", ComparisonOperator.NotEqual, "ABC"),
        ["Opt Equal k"] = () => R("Opt", ComparisonOperator.Equal, "k"),
        ["Code In [abc, ß, Kelvin]"] = () => R("Code", ComparisonOperator.In, new object?[] { "abc", "ß", "K" }),
        ["Code NotIn [abc, ß, Kelvin]"] = () => R("Code", ComparisonOperator.NotIn, new object?[] { "abc", "ß", "K" }),
        ["Opt In [ABC, null]"] = () => R("Opt", ComparisonOperator.In, new object?[] { "ABC", null }),
        ["Opt NotIn [abc, CAFÉ]"] = () => R("Opt", ComparisonOperator.NotIn, new object?[] { "abc", "CAFÉ" }),
        ["Text In 'i' (scalar)"] = () => R("Text", ComparisonOperator.In, "i"),
    };

    public static IEnumerable<object[]> RuleCases()
    {
        foreach (var name in RuleShapes.Keys)
        {
            yield return new object[] { name, "plain" };
            yield return new object[] { name, "negated" };
            yield return new object[] { name, "NOT group" };
        }
    }

    private static IRule BuildRule(string shape, string form)
    {
        var rule = RuleShapes[shape]();
        switch (form)
        {
            case "negated":
                rule.IsNegated = true;
                return rule;
            case "NOT group":
                return new RuleGroup(LogicOperator.And, rule, R("Hits", ComparisonOperator.GreaterThanOrEqual, 0)) { IsNegated = true };
            default:
                return rule;
        }
    }

    [Theory]
    [MemberData(nameof(RuleCases))]
    public void Contract_A_rule_string_equality_is_exact_in_SQL_and_in_memory(string shape, string form)
    {
        if (!Ready()) return;
        var rule = BuildRule(shape, form);
        var (_, connector, seed) = Seeded();
        var compiled = RuleExpressionConverter.ToExpression<StringLeftoverRow>(rule).Compile();
        var expected = seed.Where(compiled).ToList();

        var conditions = RuleConditionConverter.ToConditions<StringLeftoverRow>(rule).ToList();
        Keys(connector.Select(typeof(StringLeftoverRow), conditions)).Should().Equal(Keys(expected), $"{form} {shape}: read");
        connector.SelectCount(typeof(StringLeftoverRow), conditions).Should().Be(expected.Count, $"{form} {shape}: count");
        connector.Delete(typeof(StringLeftoverRow), conditions);
        Keys(connector.Select(typeof(StringLeftoverRow), (IEnumerable<Condition>?)null))
            .Should().Equal(Keys(seed.Where(r => !compiled(r))), $"{form} {shape}: delete");

        var evaluator = new RuleEvaluator();
        Keys(seed.Where(r => evaluator.Evaluate(rule, new ObjectRuleContext<StringLeftoverRow>(r)).IsMatch))
            .Should().Equal(Keys(expected), $"{form} {shape}: the in-memory evaluator answers as the compiled rule");
    }

    /// <summary>A rule's string <c>In</c> past the limit is split too: it is exact now, like the lambda's.</summary>
    [Fact]
    public void Contract_A_rule_string_list_past_the_parameter_limit_is_split()
    {
        if (!Ready()) return;
        var (_, connector, seed) = Seeded();
        var values = LongList(MaxParameters + 1500, "abc", "ß", "café");
        var rule = R("Code", ComparisonOperator.In, values.Cast<object?>().ToArray());
        // Not the compiled rule: an OR chain of this many equalities overflows the stack when compiled.
        var expected = seed.Where(r => values.Contains(r.Code, StringComparer.Ordinal)).ToList();
        expected.Should().HaveCount(3, "abc, ß and the one café spelled as in the list");

        var conditions = RuleConditionConverter.ToConditions<StringLeftoverRow>(rule).ToList();
        Keys(connector.Select(typeof(StringLeftoverRow), conditions)).Should().Equal(Keys(expected));
        connector.SelectCount(typeof(StringLeftoverRow), conditions).Should().Be(expected.Count);
    }

    // ── (c) / (d) string.Equals with StringComparison.Ordinal ─────────────────────────────────────────────────────

    private static readonly string Captured = "ABC";
    private static readonly StringComparison OrdinalVariable = StringComparison.Ordinal;

    public static readonly IReadOnlyDictionary<string, Expression<Func<StringLeftoverRow, bool>>> EqualsShapes =
        new Dictionary<string, Expression<Func<StringLeftoverRow, bool>>>
        {
            ["string.Equals(Text, abc, Ordinal)"] = x => string.Equals(x.Text, "abc", StringComparison.Ordinal),
            ["string.Equals(Code, 'abc ', Ordinal)"] = x => string.Equals(x.Code, "abc ", StringComparison.Ordinal),
            ["Code.Equals(ss, Ordinal)"] = x => x.Code.Equals("ss", StringComparison.Ordinal),
            ["Text.Equals(café, Ordinal)"] = x => x.Text.Equals("café", StringComparison.Ordinal),
            ["string.Equals(captured, Code, Ordinal)"] = x => string.Equals(Captured, x.Code, StringComparison.Ordinal),
            ["!string.Equals(Text, abc, Ordinal)"] = x => !string.Equals(x.Text, "abc", StringComparison.Ordinal),
            ["string.Equals(Opt, ABC, Ordinal)"] = x => string.Equals(x.Opt, "ABC", StringComparison.Ordinal),
            ["!string.Equals(Opt, ABC, Ordinal)"] = x => !string.Equals(x.Opt, "ABC", StringComparison.Ordinal),
            ["string.Equals(Opt, null, Ordinal)"] = x => string.Equals(x.Opt, null, StringComparison.Ordinal),
            ["string.Equals(Code, '', variable Ordinal)"] = x => string.Equals(x.Code, "", OrdinalVariable),
            ["string.Equals(Text.ToLower(), abc, Ordinal)"] = x => string.Equals(x.Text.ToLower(), "abc", StringComparison.Ordinal),
            ["string.Equals(Text, ABC, OrdinalIgnoreCase)"] = x => string.Equals(x.Text, "ABC", StringComparison.OrdinalIgnoreCase),
            // Two columns.
            ["string.Equals(Text, Opt, Ordinal)"] = x => string.Equals(x.Text, x.Opt, StringComparison.Ordinal),
            ["Text.Equals(Code, Ordinal)"] = x => x.Text.Equals(x.Code, StringComparison.Ordinal),
            ["!string.Equals(Opt, Text, Ordinal)"] = x => !string.Equals(x.Opt, x.Text, StringComparison.Ordinal),
            ["string.Equals(Opt, Code, Ordinal) || Hits == 4"] = x => string.Equals(x.Opt, x.Code, StringComparison.Ordinal) || x.Hits == 4,
        };

    public static IEnumerable<object[]> EqualsNames => EqualsShapes.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EqualsNames))]
    public void Contract_String_Equals_with_Ordinal_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, _, seed) = Seeded();
        var filter = EqualsShapes[shape];
        var compiled = filter.Compile();
        var expected = seed.Where(compiled).ToList();

        Keys(store.Read(filter, null, null, null)).Should().Equal(Keys(expected), $"'{shape}' must read the rows C# matches");
        store.Count(filter).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");
        store.Update(filter, new PropertyUpdate<StringLeftoverRow>().Set(x => x.Hits, 100));
        foreach (var row in expected) row.Hits = 100;
        Keys(store.Read(x => x.Hits == 100, null, null, null)).Should().Equal(Keys(expected), $"'{shape}' must update the rows C# matches");
        store.Delete(filter);
        Keys(store.Read(EveryRow, null, null, null)).Should().Equal(Keys(seed.Where(r => !compiled(r))),
            $"'{shape}' must delete the rows C# matches");
    }

    /// <summary>
    /// <c>OrdinalIgnoreCase</c> over two columns has no exact form here (it is a LIKE pattern built from a value), so it stays
    /// refused by name.
    /// </summary>
    [Fact]
    public void Contract_String_Equals_with_OrdinalIgnoreCase_over_two_columns_stays_refused()
    {
        if (!Ready()) return;
        var (store, _, seed) = Seeded();
        var read = () => store.Read(x => string.Equals(x.Text, x.Opt, StringComparison.OrdinalIgnoreCase), null, null, null).ToList();
        read.Should().Throw<NotSupportedException>();
        store.Count(EveryRow).Should().Be(seed.Count);
    }

    /// <summary>
    /// A value past 4000 characters: TASK-869's LIKE refused it on SQL Server (TASK-874's pattern limit); the equality has no
    /// such limit.
    /// </summary>
    [Theory]
    [InlineData(4000)]
    [InlineData(4001)]
    [InlineData(9000)]
    public void Contract_String_Equals_with_Ordinal_takes_a_value_of_any_length(int length)
    {
        if (!Ready()) return;
        var (store, _) = NewStore();
        var value = new string('a', length);
        var texts = new[] { value, value + " ", value.ToUpperInvariant(), value.Substring(0, length - 1), "a" };
        store.Create(texts.Select((t, i) => new StringLeftoverRow { Guid = Guid.NewGuid(), Text = t, Code = "C" + i }).ToList());

        store.Count(x => string.Equals(x.Text, value, StringComparison.Ordinal)).Should().Be(1);
        store.Count(x => !x.Text.Equals(value, StringComparison.Ordinal)).Should().Be(texts.Length - 1);
    }
}
