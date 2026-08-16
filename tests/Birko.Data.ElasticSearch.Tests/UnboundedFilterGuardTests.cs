using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using Birko.Data.Exceptions;
using FluentAssertions;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests
{
    /// <summary>
    /// TASK-215 — a filter that is PRESENT but constrains nothing reached
    /// <c>_delete_by_query</c> / <c>_update_by_query</c> unrefused.
    ///
    /// <para><b>Why CR-H047's guard did not cover this.</b> <c>ParseRequiredFilterQuery</c> refuses a
    /// <b>null</b> query — the "filter supplied but untranslatable" case. Here a query IS produced, and it is
    /// a perfectly ordinary-looking one. Measured offline (no cluster), the inner query type unwrapped from
    /// the <c>QueryContainer</c>:</para>
    ///
    /// <list type="table">
    ///   <item><description><c>!empty.Contains(x.Count)</c> → <c>bool { must_not: [ match_none ] }</c></description></item>
    ///   <item><description><c>!some.Contains(x.Count)</c>  → <c>bool { must_not: [ terms ] }</c></description></item>
    /// </list>
    ///
    /// <para><c>must_not: [match_none]</c> selects <b>every</b> document. The two differ only in the inner
    /// query type — the same structure, so a guard inspecting the rendered query would have to enumerate
    /// every way a backend can spell "everything". That is the third time this family has arrived wearing a
    /// harmless-looking disguise: SQL's <c>1 = 1</c> satisfied a guard that tested whether anything was
    /// rendered (TASK-137), and MongoDB's <c>$nin: []</c> is a one-element document indistinguishable from a
    /// field predicate (TASK-212). The expression says it once, for all three:
    /// <c>!empty.Contains(x)</c> is true of every entity by C# semantics.</para>
    ///
    /// <para><b>Offline by construction.</b> The guard runs before <c>EnsureInitialized()</c>, so these
    /// assertions need no cluster — which is the point, since the suite that would have caught this
    /// end-to-end is gated on a live ElasticSearch and does not run here.</para>
    /// </summary>
    public class UnboundedFilterGuardTests
    {
        private static readonly List<int> Empty = new();
        private static readonly int[] EmptyArray = Array.Empty<int>();
        private static readonly List<int> Some = new() { 1, 5 };

        // No settings → Connector stays null → the by-query call is skipped. Everything below therefore
        // measures the GUARD and its ordering, never the network.
        private static Birko.Data.ElasticSearch.Stores.ElasticSearchStore<DateModel> Store() => new();

        private static Birko.Data.ElasticSearch.Stores.AsyncElasticSearchStore<DateModel> AsyncStore() => new();

        // ── what the parser actually emits: the premise of the whole file ────────────────────────────

        private static string InnerOfSoleMustNot(QueryBase? q)
        {
            var b = q.Should().BeOfType<BoolQuery>().Subject;
            var container = b.MustNot.Should().ContainSingle().Subject;
            var v = (IQueryContainer)container;
            if (v.MatchNone != null) return "match_none";
            if (v.MatchAll != null) return "match_all";
            if (v.Terms != null) return "terms";
            return "other";
        }

        [Fact]
        public void TheDefectShape_RendersMatchEverything_AndLooksLikeAnOrdinaryNegatedQuery()
        {
            Expression<Func<DateModel, bool>> unbounded = x => !Empty.Contains(x.Count);
            Expression<Func<DateModel, bool>> bounded = x => !Some.Contains(x.Count);

            InnerOfSoleMustNot(Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery(unbounded))
                .Should().Be("match_none", "must_not match_none selects every document");
            InnerOfSoleMustNot(Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery(bounded))
                .Should().Be("terms");

            // Pins WHY the guard cannot live on the rendered query: same structure, different inner type.
            // If the parser ever stops rendering match_none here, this fails loudly rather than letting the
            // tests below quietly stop exercising the defect shape.
        }

        // ── the defect, refused at all four overrides ────────────────────────────────────────────────

        [Fact]
        public void UnboundedFilter_Delete_IsRefused()
        {
            var act = () => Store().Delete(x => !Empty.Contains(x.Count));

            act.Should().Throw<WholeTableWriteException>()
                .Which.Operation.Should().Be("delete");
        }

        [Fact]
        public void UnboundedFilter_Update_IsRefused()
        {
            var act = () => Store().Update(
                x => !Empty.Contains(x.Count),
                new Data.Stores.PropertyUpdate<DateModel>().Set(r => r.Text, "clobbered"));

            act.Should().Throw<WholeTableWriteException>()
                .Which.Operation.Should().Be("update");
        }

        [Fact]
        public async Task UnboundedFilter_DeleteAsync_IsRefused()
        {
            var act = async () => await AsyncStore().DeleteAsync(x => !Empty.Contains(x.Count));

            (await act.Should().ThrowAsync<WholeTableWriteException>())
                .Which.Operation.Should().Be("delete");
        }

        [Fact]
        public async Task UnboundedFilter_UpdateAsync_IsRefused()
        {
            var act = async () => await AsyncStore().UpdateAsync(
                x => !Empty.Contains(x.Count),
                new Data.Stores.PropertyUpdate<DateModel>().Set(r => r.Text, "clobbered"));

            (await act.Should().ThrowAsync<WholeTableWriteException>())
                .Which.Operation.Should().Be("update");
        }

        [Fact]
        public void UnboundedFilter_AsAnEmptyArray_IsRefusedToo()
        {
            var act = () => Store().Delete(x => !EmptyArray.Contains(x.Count));

            act.Should().Throw<WholeTableWriteException>();
        }

        // ── the guard runs BEFORE the index is touched ───────────────────────────────────────────────

        [Fact]
        public void TheRefusal_HappensBeforeTheIndexIsTouched()
        {
            // Ordering measured, not assumed, and the two exception types make it unambiguous: on an
            // unconfigured store EnsureInitialized() throws "Settings not initialized". A BOUNDED filter
            // reaches that and gets it. An UNBOUNDED one never does — it gets the scope refusal instead, so
            // the guard demonstrably runs first. That matters: refusing before the index is opened means the
            // refusal holds identically whether or not a cluster is reachable.
            ((Action)(() => Store().Delete(x => x.Count > 4)))
                .Should().Throw<InvalidOperationException>()
                .And.Should().NotBeOfType<WholeTableWriteException>();

            ((Action)(() => Store().Delete(x => !Empty.Contains(x.Count))))
                .Should().Throw<WholeTableWriteException>();
        }

        // ── the explicit door, and the false-positive direction ──────────────────────────────────────

        [Fact]
        public void AnExplicitTruePredicate_IsNotRefused()
        {
            // `x => true` renders match_all on this backend and stays the documented all-rows synonym.
            // § SH-H037: the guard must have a door, or it is a wall.
            Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery<DateModel>(x => true)
                .Should().BeOfType<MatchAllQuery>();

            ((Action)(() => Store().Delete(x => true))).Should().NotThrow<WholeTableWriteException>();
        }

        [Fact]
        public void DeleteAll_IsNotRefused()
        {
            ((Action)(() => Store().DeleteAll())).Should().NotThrow<WholeTableWriteException>();
        }

        [Fact]
        public void ANonEmptyNegatedContains_IsNotRefused()
        {
            ((Action)(() => Store().Delete(x => !Some.Contains(x.Count)))).Should().NotThrow<WholeTableWriteException>();
        }

        [Fact]
        public void AnEmptyUnNegatedContains_IsNotRefused()
        {
            // Always-FALSE, not always-true: it matches nothing, and the parser renders match_none. The
            // mirror image of the defect, and refusing it would break working code.
            Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery<DateModel>(x => Empty.Contains(x.Count))
                .Should().BeOfType<MatchNoneQuery>();

            ((Action)(() => Store().Delete(x => Empty.Contains(x.Count)))).Should().NotThrow<WholeTableWriteException>();
        }

        [Fact]
        public void AnOrdinaryFilter_IsNotRefused()
        {
            ((Action)(() => Store().Delete(x => x.Count > 4))).Should().NotThrow<WholeTableWriteException>();
        }

        [Fact]
        public void ANullFilter_StillGetsTheNullRefusal_NotTheScopeOne()
        {
            // The two refusals stay distinct — RequireBoundedFilter returns on null and lets
            // ParseRequiredFilterQuery (CR-H047) own that case. Asserted as "not the scope refusal" rather
            // than as ArgumentNullException, because on an unconfigured store EnsureInitialized() intervenes
            // first: the null check lives after it, inside the parser. Pinning the exact type here would be
            // pinning the store's init order, which is not what this file is about.
            var act = () => Store().Delete((Expression<Func<DateModel, bool>>)null!);

            act.Should().Throw<Exception>().And.Should().NotBeOfType<WholeTableWriteException>();
        }
    }
}
