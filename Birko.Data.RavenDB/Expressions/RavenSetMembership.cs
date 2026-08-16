using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Data.Expressions;
using Raven.Client.Documents.Linq;

namespace Birko.Data.RavenDB.Expressions;

/// <summary>
/// Rewrites the portable set-membership spelling <c>collection.Contains(x.Member)</c> into RavenDB's own
/// <c>x.Member.In(collection)</c>, which is the only form its LINQ provider translates.
/// </summary>
/// <remarks>
/// <para>
/// Measured offline (TASK-221), driver as referenced by this project: a baseline
/// <c>x =&gt; x.Amount &gt; 3</c> renders <c>from 'Docs' where Amount &gt; $p0</c>, while <b>every</b>
/// collection <c>Contains</c> spelling — array, <c>List&lt;T&gt;</c>, and the explicit
/// <c>Enumerable.Contains</c> — fails with
/// <c>NotSupportedException: Expression type not supported: TypedParameterExpression</c>.
/// <c>x.Amount.In(arr)</c> renders <c>from 'Docs' where Amount in ($p0)</c>.
/// </para>
/// <para>
/// This is <b>not</b> the .NET 9+ <c>MemoryExtensions</c> binding that
/// <see cref="SpanContains"/> handles for MongoDB and CosmosDB — every spelling fails here, so that
/// rewrite would have turned one failure into an identical one. Raven simply has no
/// <c>Contains</c>-shaped set membership. `IN` is the canonical batch-load pattern, so without this a
/// filter that works on SQL, ElasticSearch, MongoDB and CosmosDB throws on RavenDB alone and the store
/// stops being substitutable.
/// </para>
/// <para>
/// <b>The direction of membership decides everything, and getting it wrong breaks working code.</b>
/// Two shapes share the method name:
/// </para>
/// <list type="bullet">
/// <item><c>constCollection.Contains(x.Member)</c> — "is this entity's value one of these?". Broken;
/// rewritten.</item>
/// <item><c>x.CollectionMember.Contains(constValue)</c> — "does this entity's collection hold this?".
/// Measured to <b>already work</b> (<c>x.Tags.Contains("red")</c> renders
/// <c>from 'Docs' where Tags = $p0</c>), so it is left strictly alone.</item>
/// </list>
/// <para>
/// They are told apart by which operand references the lambda parameter — the same test
/// <c>ElasticSearch.ParseContains</c> makes for the same reason.
/// </para>
/// </remarks>
public static class RavenSetMembership
{
    private static readonly MethodInfo? InMethod = typeof(RavenQueryableExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == nameof(RavenQueryableExtensions.In)
                          && m.IsGenericMethodDefinition
                          && m.GetParameters().Length == 2
                          && m.GetParameters()[1].ParameterType.IsGenericType
                          && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

    /// <summary>
    /// Rewrites every translatable <c>collection.Contains(x.Member)</c> in <paramref name="filter"/> to
    /// <c>x.Member.In(collection)</c>. Returns <paramref name="filter"/> unchanged, by reference, when
    /// there is nothing to rewrite.
    /// </summary>
    public static Expression<Func<T, bool>>? Rewrite<T>(Expression<Func<T, bool>>? filter)
    {
        if (filter == null) return null;

        // `x => true` is this framework's documented read-all / *All synonym (CLAUDE.md § Conventions),
        // and RavenDB refuses it outright: "Constants expressions such as Where(x => true) are not
        // allowed in the RavenDB queries". Same root cause as the Contains refusal — a portable spelling
        // every other backend accepts — so it is handled here rather than left to fail. Dropping the
        // predicate is exactly equivalent: no Where clause IS all rows. Recognised by
        // PredicateScope.IsExplicitAllRows, the framework's single producer of that judgement, so this
        // cannot disagree with the destructive guards about what "explicitly everything" means.
        if (PredicateScope.IsExplicitAllRows(filter)) return null;

        if (InMethod == null) return filter;

        var body = new Rewriter().Visit(filter.Body);
        return ReferenceEquals(body, filter.Body)
            ? filter
            : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
    }

    private sealed class Rewriter : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var rewritten = TryRewrite(node);
            return rewritten == null ? base.VisitMethodCall(node) : rewritten;
        }

        private static Expression? TryRewrite(MethodCallExpression node)
        {
            if (node.Method.Name != "Contains") return null;

            // A string Contains is a substring test, not set membership.
            if (node.Method.DeclaringType == typeof(string)) return null;
            if (node.Object?.Type == typeof(string)) return null;

            Expression? collection, value;
            if (node.Object != null)
            {
                collection = node.Object;
                value = node.Arguments.ElementAtOrDefault(0);
            }
            else
            {
                // Static form: Enumerable.Contains(source, item) — and MemoryExtensions.Contains, whose
                // source arrives wrapped in an implicit span conversion. Unwrapping it here is why an
                // array spelling works too, using the same producer SpanContains and PredicateScope share.
                collection = node.Arguments.ElementAtOrDefault(0);
                value = node.Arguments.ElementAtOrDefault(1);
            }
            if (collection == null || value == null) return null;

            collection = SpanContains.UnwrapSpanConversion(collection);

            // THE discrimination. Only "is the entity's value one of this outside set?" is rewritten.
            // The mirror image — x.Tags.Contains(const) — already translates and must not be touched.
            if (ReferencesParameter(collection)) return null;
            if (!ReferencesParameter(value)) return null;

            var element = value.Type;
            var sequence = typeof(IEnumerable<>).MakeGenericType(element);
            if (!sequence.IsAssignableFrom(collection.Type)) return null;

            return Expression.Call(
                InMethod!.MakeGenericMethod(element),
                value,
                collection.Type == sequence ? collection : Expression.Convert(collection, sequence));
        }

        /// <summary>
        /// Whether the subtree reads from the lambda's entity parameter. Deliberately a local copy: the
        /// same question is asked by <c>PredicateScope</c>, <c>ExpressionNormalizer</c> and
        /// <c>ElasticSearch.ParseContains</c>, each with its own private version. Consolidating all four
        /// is worth doing and is not this task.
        /// </summary>
        private static bool ReferencesParameter(Expression expr) => new ParameterFinder().Found(expr);

        private sealed class ParameterFinder : ExpressionVisitor
        {
            private bool _found;

            public bool Found(Expression expr)
            {
                _found = false;
                Visit(expr);
                return _found;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                _found = true;
                return base.VisitParameter(node);
            }
        }
    }
}
