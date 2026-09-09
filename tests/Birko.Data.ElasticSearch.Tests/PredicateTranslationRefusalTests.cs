using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using FluentAssertions;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-308 — the three high <c>filter-expression-translation</c> findings in this backend. All three are
/// the same species: <b>a sub-translation that fails yields a query which silently means something else</b>,
/// so TASK-268's top-level guard — which fires only on a <i>null</i> query — never saw any of them.
///
/// <para><b>SH-H025.</b> An ordering comparison whose value would not convert to <c>double</c> emitted a
/// <c>NumericRangeQuery</c> with a <b>null bound</b>, which ElasticSearch reads as an unconstrained range
/// over every document that has the field. Measured before the fix: <c>x =&gt; x.Date &gt; cutoff</c> →
/// <c>NumericRange(field=date, gt=NULL, gte=NULL, lt=NULL, lte=NULL)</c>, and it survived both
/// <c>ParseFilterQuery</c> and <c>ParseRequiredFilterQuery</c> untouched — so
/// <c>DeleteByQuery(x =&gt; x.Date &lt; cutoff)</c> targeted the whole index, on the most ordinary shape a
/// time-series filter has.</para>
///
/// <para><b>SH-H027.</b> <c>CombineBool</c> added only the non-null operand translations and returned null
/// only when <i>both</i> were null, so one untranslatable operand left a one-clause <c>bool</c> query.
/// Measured: <c>untranslatable &amp;&amp; x.Count == 5</c> → <c>Bool(must=1)</c>;
/// <c>untranslatable || x.Count == 5</c> → <c>Bool(should=1)</c>. Note the directions are wrong in
/// <i>opposite</i> ways — a dropped conjunct <b>widens</b> (so a by-query delete destroys more than asked),
/// a dropped disjunct <b>narrows</b> (so a read misses rows) — which is why neither is tolerable and the
/// answer is to refuse.</para>
///
/// <para><b>SH-H028.</b> <c>String.Contains</c> put the caller's value verbatim into a
/// <c>QueryStringQuery</c>, i.e. into Lucene's query <i>grammar</i>. Measured with the field resolving to
/// <c>text.keyword</c>: <c>secretField:*</c> and <c>* OR Count:5</c> both rendered verbatim, so a search
/// term could address fields the predicate never mentioned; <c>unbalanced(</c> became a parse failure
/// rather than a no-match.</para>
///
/// <para><b>None of this was pinned by any existing test</b> — the whole ES suite stayed green through all
/// three fixes, which is why they survived. That absence is the finding behind the finding.</para>
/// </summary>
public class PredicateTranslationRefusalTests
{
    /// <summary>
    /// Genuinely untranslatable, and pinned as such by <c>FilterQueryGuardTests</c> rather than assumed:
    /// <c>Trim()</c> is not in the supported set and is parameter-dependent, so it cannot be folded.
    /// </summary>
    private static readonly Expression<Func<DateModel, bool>> Untranslatable = x => x.Text.Trim() == "a";

    private static QueryBase? Parse(Expression<Func<DateModel, bool>> f)
        => Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(f);

    /// <summary>
    /// A model carrying a <see cref="TimeSpan"/> member, which is the shape that reaches
    /// <c>BuildRangeComparison</c>'s refusal: <c>TimeSpan</c> declares <c>&gt;</c> so the comparison
    /// compiles, and <c>Convert.ToDouble</c> throws for it so no numeric bound can be produced.
    /// <para>⚠ It has to be a plain member compared with a constant. <c>x.Date - x.Date &gt; window</c>
    /// looks equivalent and is not: an arithmetic operand routes to <c>BuildScriptComparison</c>, which
    /// already refuses a <c>TimeSpan</c> constant for its own unrelated reason — so that shape would have
    /// tested a different guard and passed for the wrong reason.</para>
    /// </summary>
    public class SpanModel : Birko.Data.Models.AbstractModel
    {
        public TimeSpan Window { get; set; }
        public int Count { get; set; }
    }

    private static QueryBase? ParseSpan(Expression<Func<SpanModel, bool>> f)
        => Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(f);

    // ── SH-H025: an ordering comparison never emits a range it could not bound ──────────────────────

    [Fact]
    public void A_DateTime_ordering_comparison_becomes_a_bounded_date_range()
    {
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var q = Parse(x => x.Date > cutoff);

        q.Should().BeOfType<DateRangeQuery>(
            "before the fix this was a NumericRangeQuery with every bound null — an unconstrained range");
        ((IDateRangeQuery)q!).GreaterThan.Should().NotBeNull("the bound must actually be carried");
    }

    [Theory]
    [InlineData(ExpressionType.GreaterThan)]
    [InlineData(ExpressionType.GreaterThanOrEqual)]
    [InlineData(ExpressionType.LessThan)]
    [InlineData(ExpressionType.LessThanOrEqual)]
    public void Every_ordering_operator_carries_a_bound_for_a_DateTime(ExpressionType op)
    {
        // All four arms, because the defect was one `switch` and a fix that repaired three of them would
        // leave an unconstrained range reachable through the fourth.
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Expression<Func<DateModel, bool>> f = op switch
        {
            ExpressionType.GreaterThan => x => x.Date > cutoff,
            ExpressionType.GreaterThanOrEqual => x => x.Date >= cutoff,
            ExpressionType.LessThan => x => x.Date < cutoff,
            _ => x => x.Date <= cutoff,
        };

        var q = (IDateRangeQuery)Parse(f)!;

        (q.GreaterThan ?? q.GreaterThanOrEqualTo ?? q.LessThan ?? q.LessThanOrEqualTo)
            .Should().NotBeNull($"{op} must bind its bound");
    }

    [Fact]
    public void A_numeric_ordering_comparison_is_unchanged()
    {
        // Contract pin: the numeric path was correct and must stay a NumericRangeQuery. Without this the
        // fix is indistinguishable from routing everything through the date branch.
        var q = Parse(x => x.Count > 5);

        q.Should().BeOfType<NumericRangeQuery>();
        ((INumericRangeQuery)q!).GreaterThan.Should().Be(5);
    }

    [Fact]
    public void A_DateTimeOffset_is_normalised_to_UTC_rather_than_refused()
    {
        // Included in the fix rather than refused: it is the same kind of value as a DateTime — an instant
        // — and the framework already normalises one to UTC when it stores it (TASK-263's [UtcField]).
        // Refusing it would have turned a shape that "worked" (wrongly, as an unbounded range) into a hard
        // error for no gain.
        var cutoff = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(1));

        var q = Parse(x => x.Date > cutoff.UtcDateTime);

        q.Should().BeOfType<DateRangeQuery>();
    }

    [Fact]
    public void A_value_with_no_expressible_range_is_refused_instead_of_emitting_an_unbounded_one()
    {
        // TimeSpan has a `>` operator and Convert.ToDouble throws for it, so before the fix this emitted a
        // NumericRangeQuery with every bound null — matching every document with the field. There is no
        // range query whose bound a TimeSpan could fill, so a refusal is the honest answer.
        var window = TimeSpan.FromHours(1);

        ParseSpan(x => x.Window > window).Should().BeNull();
    }

    [Fact]
    public void The_destructive_by_query_path_refuses_an_unexpressible_ordering()
    {
        // Where the previous test measures the translation, this measures the consequence: on the by-query
        // paths the difference is a whole-index delete versus an error.
        var window = TimeSpan.FromHours(1);

        var act = () => Birko.Data.ElasticSearch.ElasticSearch
            .ParseRequiredFilterQuery<SpanModel>(x => x.Window > window);

        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// ⚠ Pins a deliberate gap so it does not read as an oversight (§ TASK-263). There is <b>no</b>
    /// <c>TermRangeQuery</c> arm, because a <c>string</c> value cannot reach an ordering comparison from
    /// C# at all — <c>string</c> has no <c>&gt;</c> operator. An arm for it would be unreachable code
    /// advertising a capability nobody can call. This asserts the premise rather than the absence, so if a
    /// future C# or a new API makes it reachable, this is what says the switch needs revisiting.
    /// </summary>
    [Fact]
    public void A_string_cannot_reach_an_ordering_comparison_at_all_which_is_why_there_is_no_term_range()
    {
        typeof(string).GetMethods()
            .Where(m => m.IsStatic && m.Name is "op_GreaterThan" or "op_LessThan")
            .Should().BeEmpty("string declares no ordering operators, so ParseComparison never sees one");
    }

    // ── SH-H027: both operands translate, or neither does ──────────────────────────────────────────

    [Fact]
    public void An_untranslatable_conjunct_refuses_rather_than_widening_the_match()
    {
        Parse(x => x.Text.Trim() == "a" && x.Count == 5).Should().BeNull(
            "before the fix this was Bool(must=1) — the Trim conjunct gone, so the query matched more "
            + "documents than the caller asked for");
    }

    [Fact]
    public void An_untranslatable_disjunct_refuses_rather_than_narrowing_the_match()
    {
        Parse(x => x.Text.Trim() == "a" || x.Count == 5).Should().BeNull(
            "before the fix this was Bool(should=1) — the other branch gone, so a read silently missed rows");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_destructive_by_query_path_refuses_a_half_translated_boolean(bool useOr)
    {
        Expression<Func<DateModel, bool>> f = useOr
            ? x => x.Text.Trim() == "a" || x.Count == 5
            : x => x.Text.Trim() == "a" && x.Count == 5;

        var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery(f);

        act.Should().Throw<NotSupportedException>(
            "measured before the fix: ParseRequiredFilterQuery returned the one-clause bool query with no "
            + "throw, on both spellings");
    }

    [Fact]
    public void A_fully_translatable_boolean_still_carries_both_clauses()
    {
        var and = (IBoolQuery)Parse(x => x.Count == 5 && x.IsTest)!;
        and.Must.Should().HaveCount(2);

        var or = (IBoolQuery)Parse(x => x.Count == 5 || x.IsTest)!;
        or.Should.Should().HaveCount(2);
    }

    [Fact]
    public void An_operand_that_legitimately_matches_nothing_still_composes()
    {
        // ⚠ The reason "refuse on null" is safe: TASK-266 made a genuinely-empty collection a
        // MatchNoneQuery, which is NON-null. So null means untranslatable and nothing else, and this fix
        // cannot break an empty-batch filter. Without this test the two cases are indistinguishable.
        var ids = Array.Empty<int>();

        var q = (IBoolQuery)Parse(x => ids.Contains(x.Count) && x.IsTest)!;

        q.Must.Should().HaveCount(2);
    }

    // ── SH-H028: a value in Lucene statement position ──────────────────────────────────────────────

    [Fact]
    public void Contains_no_longer_hands_the_value_to_the_query_grammar()
    {
        var payload = "secretField:*";

        var q = Parse(x => x.Text.Contains(payload));

        q.Should().BeOfType<WildcardQuery>(
            "a QueryStringQuery parses its value as a query expression; a wildcard value has no grammar "
            + "beyond * and ?");
        q.Should().NotBeOfType<QueryStringQuery>();
    }

    [Theory]
    [InlineData("secretField:*", "*secretField:\\**")]
    [InlineData("* OR Count:5", "*\\* OR Count:5*")]
    [InlineData("a AND b", "*a AND b*")]
    [InlineData("unbalanced(", "*unbalanced(*")]
    [InlineData("who?", "*who\\?*")]
    [InlineData("back\\slash", "*back\\\\slash*")]
    [InlineData("plain", "*plain*")]
    public void Contains_escapes_exactly_the_wildcard_metacharacters_and_leaves_the_rest_literal(
        string payload, string expected)
    {
        // The colon, AND/OR and the parenthesis stay LITERAL on purpose — they are only syntax to
        // query_string, and this query type never parses them. That is what makes the containment total
        // rather than a blacklist: only `*`, `?` and `\` mean anything here, and all three are escaped.
        var p = payload;

        var q = (IWildcardQuery)Parse(x => x.Text.Contains(p))!;

        q.Value.Should().Be(expected);
    }

    [Fact]
    public void EndsWith_escapes_on_the_same_terms()
    {
        // § Conventions: guard the whole verb family or none of it. EndsWith already built a wildcard
        // pattern from caller text and did not escape it, so `EndsWith("a*b")` matched more than it named.
        // Narrower than SH-H028 (wildcards only, no field access) and the same one-line containment.
        var p = "a*b";

        var q = (IWildcardQuery)Parse(x => x.Text.EndsWith(p))!;

        q.Value.Should().Be("*a\\*b");
    }

    [Fact]
    public void StartsWith_needs_no_escaping_and_is_unchanged()
    {
        // Asserted rather than assumed: a PrefixQuery takes a literal prefix and has no pattern syntax at
        // all, so adding escaping there would corrupt a legitimate `StartsWith("a*b")`.
        var p = "a*b";

        var q = Parse(x => x.Text.StartsWith(p));

        q.Should().BeOfType<PrefixQuery>();
        ((IPrefixQuery)q!).Value.Should().Be("a*b");
    }
}
