using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using FluentAssertions;
using Nest;
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests
{
    /// <summary>
    /// Pins the behaviour of the ElasticSearch hand-rolled filter parser
    /// (<see cref="Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(Expression, Type, string)"/>)
    /// against a matrix of filter shapes.
    ///
    /// Reference semantics = the native-LINQ backends (InMemory / JSON / XML / RavenDB / CosmosDB), which
    /// compile or forward the predicate and therefore honour full C# semantics. The SQL hand-rolled parser
    /// matches those reference semantics on the same matrix — verified end-to-end against SQLite in
    /// Birko.Data.SQL.SqLite.Tests.SqlExpressionParityTests.
    ///
    /// These tests cover the shapes that previously diverged (bare bool, constant bool, EndsWith, ToLower,
    /// the IN pattern, bitwise &amp;/|) and now assert the corrected translation. ElasticSearch cannot be
    /// executed in-process, so each case asserts the produced query STRUCTURE rather than a result set.
    /// </summary>
    public class ExpressionDivergenceTests
    {
        private static QueryBase? Parse(Expression<Func<DateModel, bool>> expr)
            => Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(expr);

        // ---- Comparisons / boolean combinators ----

        [Fact]
        public void Equality_ProducesTermQuery()
        {
            var q = Parse(x => x.Count == 5).Should().BeOfType<TermQuery>().Subject;
            q.Field.Name.Should().Be("count");
            q.Value.Should().Be(5);
        }

        [Fact]
        public void NotEqual_ProducesMustNotTerm()
        {
            Parse(x => x.Count != 5).Should().BeOfType<BoolQuery>();
        }

        [Fact]
        public void AndAlso_ProducesBoolMust()
        {
            var q = Parse(x => x.Count > 0 && x.Count < 10).Should().BeOfType<BoolQuery>().Subject;
            q.Must.Should().HaveCount(2);
        }

        [Fact]
        public void OrElse_ProducesBoolShould()
        {
            var q = Parse(x => x.Count == 1 || x.Count == 2).Should().BeOfType<BoolQuery>().Subject;
            q.Should.Should().HaveCount(2);
        }

        // FIXED: bitwise & / | on booleans are logical AND/OR, same as && / || — previously the filter was
        // silently dropped (only AndAlso/OrElse were handled).
        [Fact]
        public void BitwiseAnd_ProducesBoolMust()
        {
            var q = Parse(x => x.IsTest & (x.Count > 0)).Should().BeOfType<BoolQuery>().Subject;
            q.Must.Should().HaveCount(2);
        }

        [Fact]
        public void BitwiseOr_ProducesBoolShould()
        {
            var q = Parse(x => x.IsTest | (x.Count > 0)).Should().BeOfType<BoolQuery>().Subject;
            q.Should.Should().HaveCount(2);
        }

        // ---- Complex nested boolean grouping (precedence must be preserved, not flattened) ----

        // (a || b) && (c || d)  →  bool.Must = [ bool.Should[a,b], bool.Should[c,d] ]
        [Fact]
        public void NestedOrAnd_PreservesGrouping()
        {
            var outer = Parse(x => (x.Count == 1 || x.Count == 2) && (x.Amount > 0m || x.Amount < -1m))
                .Should().BeOfType<BoolQuery>().Subject;
            outer.Must.Should().HaveCount(2);
            outer.Should.Should().BeNullOrEmpty();
            // each Must arm is itself an OR (bool.Should) — i.e. the parentheses were honoured
            outer.Must.Select(qc => qc).Should().OnlyContain(qc => ((IQueryContainer)qc).Bool != null
                && ((IQueryContainer)qc).Bool.Should != null);
        }

        // (a && b) || (c && d)  →  bool.Should = [ bool.Must[a,b], bool.Must[c,d] ]
        [Fact]
        public void NestedAndOr_PreservesGrouping()
        {
            var outer = Parse(x => (x.Count == 1 && x.IsTest) || (x.Count == 2 && x.Amount > 0m))
                .Should().BeOfType<BoolQuery>().Subject;
            outer.Should.Should().HaveCount(2);
            outer.Must.Should().BeNullOrEmpty();
            outer.Should.Should().OnlyContain(qc => ((IQueryContainer)qc).Bool != null
                && ((IQueryContainer)qc).Bool.Must != null);
        }

        // !(a && b)  →  bool.MustNot = [ bool.Must[a,b] ]
        [Fact]
        public void NotOfAnd_ProducesMustNotOverBoolMust()
        {
            var outer = Parse(x => !(x.IsTest && x.Count > 0))
                .Should().BeOfType<BoolQuery>().Subject;
            outer.MustNot.Should().HaveCount(1);
            ((IQueryContainer)outer.MustNot.Single()).Bool.Must.Should().HaveCount(2);
        }

        // ---- Null / HasValue ----

        [Fact]
        public void EqualNull_ProducesMustNotExists() => Parse(x => x.Amount == null).Should().BeOfType<BoolQuery>();

        [Fact]
        public void NotEqualNull_ProducesExists() => Parse(x => x.Amount != null).Should().BeOfType<ExistsQuery>();

        [Fact]
        public void HasValue_ProducesExists() => Parse(x => x.Amount.HasValue).Should().BeOfType<ExistsQuery>();

        // ---- String predicates ----

        [Fact]
        public void StartsWith_ProducesPrefixQuery() => Parse(x => x.Text.StartsWith("z")).Should().BeOfType<PrefixQuery>();

        // FIXED: EndsWith previously THREW; now a leading-wildcard match, the equivalent of SQL LIKE '%z'.
        [Fact]
        public void EndsWith_ProducesLeadingWildcard()
        {
            var q = Parse(x => x.Text.EndsWith("z")).Should().BeOfType<WildcardQuery>().Subject;
            q.Value.Should().Be("*z");
        }

        // FIXED: ToLower()/ToUpper() previously THREW; now transparent (the wrapped column still resolves).
        [Fact]
        public void ToLowerComparison_ResolvesToTermOnColumn()
        {
            var q = Parse(x => x.Text.ToLower() == "abc").Should().BeOfType<TermQuery>().Subject;
            q.Field.Name.Should().Be("text.keyword");
            q.Value.Should().Be("abc");
        }

        // ---- Bare / constant boolean predicates ----

        // FIXED: a bare bool member previously produced a TermQuery with a field but no value; now IsTest == true.
        [Fact]
        public void BareBoolMember_ProducesTermTrue()
        {
            var q = Parse(x => x.IsTest).Should().BeOfType<TermQuery>().Subject;
            q.Field.Name.Should().Be("isTest");
            q.Value.Should().Be(true);
        }

        // FIXED: x => true previously produced a fieldless (malformed) TermQuery; now match-all. This is the
        // idiomatic "read everything" filter (e.g. ReadAsync(x => true)).
        [Fact]
        public void ConstantTrue_ProducesMatchAll() => Parse(x => true).Should().BeOfType<MatchAllQuery>();

        // FIXED: x => false previously produced a fieldless TermQuery; now match-none.
        [Fact]
        public void ConstantFalse_ProducesMatchNone() => Parse(x => false).Should().BeOfType<MatchNoneQuery>();

        // ---- Ternary / null-coalescing via the shared ExpressionNormalizer (STORY-047) ----

        // A boolean ternary c ? t : f is desugared to (c && t) || (!c && f) by the normalizer, so ES
        // sees a plain OR-of-ANDs. Previously ConditionalExpression was unhandled → the filter was dropped.
        [Fact]
        public void BooleanTernary_DesugarsToShouldOfMusts()
        {
            var outer = Parse(x => x.Count > 3 ? x.IsTest : x.Amount == null)
                .Should().BeOfType<BoolQuery>().Subject;
            outer.Should.Should().HaveCount(2);
            outer.Must.Should().BeNullOrEmpty();
            outer.Should.Should().OnlyContain(qc => ((IQueryContainer)qc).Bool != null
                && ((IQueryContainer)qc).Bool.Must != null);
        }

        // A parameter-free ternary (constant/closure test) is funcletized: the surviving branch is all
        // that reaches ES — here the whole predicate collapses to the bare-bool arm.
        [Fact]
        public void ParameterFreeTernary_FuncletizesToSurvivingBranch()
        {
            var flag = true;
            var q = Parse(x => (flag ? x.IsTest : x.Count > 100)).Should().BeOfType<TermQuery>().Subject;
            q.Field.Name.Should().Be("isTest");
            q.Value.Should().Be(true);
        }

        // ---- Value-expression operands → Painless script query (STORY-047 follow-up) ----

        [Fact]
        public void ColumnArithmetic_ProducesGuardedScriptQuery()
        {
            var q = Parse(x => x.Count * 2 > 10).Should().BeOfType<ScriptQuery>().Subject;
            var src = ((InlineScript)q.Script!).Source;
            src.Should().Contain("(doc['count'].value * 2)");
            src.Should().Contain("> 10");
            src.Should().Contain("doc['count'].size() > 0"); // required-field existence guard
        }

        [Fact]
        public void ValueCoalesce_ProducesScriptWithMissingFallback_NoGuardForCoalescedField()
        {
            var q = Parse(x => (x.Amount ?? 0m) > 5m).Should().BeOfType<ScriptQuery>().Subject;
            var src = ((InlineScript)q.Script!).Source;
            src.Should().Contain("doc['amount'].size() == 0 ? 0 : doc['amount'].value");
            // The coalesced field handles its own absence, so it is NOT added to the required guard.
            src.Should().NotContain("doc['amount'].size() > 0");
        }

        [Fact]
        public void ValueTernary_ProducesScriptCase()
        {
            var q = Parse(x => (x.IsTest ? x.Count : 0) > 3).Should().BeOfType<ScriptQuery>().Subject;
            var src = ((InlineScript)q.Script!).Source;
            src.Should().Contain("doc['isTest'].value ?");
            src.Should().Contain("doc['count'].value");
            src.Should().Contain("> 3");
        }

        // ---- IN pattern ----

        // FIXED: collection.Contains(x.Member) previously THREW; now a terms (IN) query.
        [Fact]
        public void InPattern_ProducesTermsQuery()
        {
            var ids = new[] { 1, 2, 3 };
            var q = Parse(x => ids.Contains(x.Count)).Should().BeOfType<TermsQuery>().Subject;
            q.Field.Name.Should().Be("count");
            q.Terms.Should().BeEquivalentTo(new object[] { 1, 2, 3 });
        }

        // ---- IN pattern: the EMPTY / NULL collection (regression, was silent-wrong-rows) ----
        //
        // Reference semantics (the native-LINQ backends this file measures against): an empty collection
        // `Contains` nothing, so the predicate matches NO documents. ParseContains used to return `null`
        // for that case, and CombineBool DROPS null sub-queries — so the membership filter silently
        // vanished and the query returned everything matching the remaining clauses. An empty collection is
        // a normal outcome of the canonical batch pattern (fetch parents, filter children by their ids),
        // so this was reachable from ordinary code. Now it is an explicit match-none.
        //
        // The SQL parser had the same defect with a milder outcome — `Col IN ()`, accepted by SQLite as
        // always-false, rejected outright by PostgreSQL/MSSQL — fixed in Birko.Data.SQL 0801738.

        [Fact]
        public void InPattern_EmptyCollection_MatchesNothing_NotEverything()
        {
            var ids = Array.Empty<int>();

            // Must be an explicit match-none: `null` would be dropped by the clause combiner.
            Parse(x => ids.Contains(x.Count)).Should().BeOfType<MatchNoneQuery>();
        }

        [Fact]
        public void InPattern_NullCollection_MatchesNothing_NotEverything()
        {
            int[]? ids = null;

            // A null collection is the same case as an empty one, one branch earlier in ParseContains.
            Parse(x => ids!.Contains(x.Count)).Should().BeOfType<MatchNoneQuery>();
        }

        [Fact]
        public void InPattern_EmptyCollection_SurvivesClauseCombination()
        {
            var ids = Array.Empty<int>();

            // THE ACTUAL BUG: combined with another clause, the empty membership filter used to disappear
            // and leave `x.Text == "a"` alone — matching rows it must not. Assert the combined query still
            // carries BOTH clauses.
            var q = Parse(x => ids.Contains(x.Count) && x.Text == "a").Should().BeOfType<BoolQuery>().Subject;

            // HaveCount(2) is the whole point: pre-fix this was 1, because the empty clause was dropped.
            q.Must.Should().HaveCount(2, "the empty membership clause must not be dropped");
        }

        [Fact]
        public void InPattern_NegatedEmptyCollection_MatchesEverything()
        {
            var ids = Array.Empty<int>();

            // The asymmetry that must not be got wrong: every document is NOT in the empty set, so a
            // negated empty IN matches EVERYTHING. No special case is needed — ParseNot wraps the operand
            // in MustNot, and "must not match nothing" is every document. This pins that reasoning.
            var q = Parse(x => !ids.Contains(x.Count)).Should().BeOfType<BoolQuery>().Subject;
            q.MustNot.Should().HaveCount(1);
        }

        [Fact]
        public void InPattern_SingleElement_StillProducesARealTermsQuery()
        {
            var ids = new[] { 7 };

            // Boundary: guards against an over-eager emptiness check turning a one-element set into
            // match-none.
            var q = Parse(x => ids.Contains(x.Count)).Should().BeOfType<TermsQuery>().Subject;
            q.Terms.Should().BeEquivalentTo(new object[] { 7 });
        }
    }
}
