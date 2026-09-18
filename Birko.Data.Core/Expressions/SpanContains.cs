using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Expressions
{
    /// <summary>
    /// Handles the .NET 9+ overload change that made <c>array.Contains(value)</c> bind to
    /// <see cref="MemoryExtensions"/> instead of <see cref="Enumerable"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On .NET 9+ an <b>array</b>'s instance-style <c>set.Contains(x.Col)</c> binds to
    /// <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c> — or its
    /// <c>IEqualityComparer&lt;T&gt;</c> overload when <c>T</c> is not <c>IEquatable&lt;T&gt;</c> — rather
    /// than to <c>Enumerable.Contains</c>. The collection then arrives wrapped in an implicit
    /// <c>ReadOnlySpan&lt;T&gt;</c> conversion, and a span is a ref struct that cannot be boxed.
    /// </para>
    /// <para>
    /// The framework has now been bitten by this three times, from three directions, which is why the
    /// two halves below are shared rather than re-derived per site:
    /// </para>
    /// <list type="bullet">
    /// <item><c>PredicateScope</c> could not evaluate the operand, so every array-typed caller went
    /// unguarded by <c>RequireBoundedFilter</c>;</item>
    /// <item>the SQL parser fed the trailing comparer argument in as an operand and flipped the whole
    /// condition to <c>IS NULL</c> — 0 rows against 21 matching (Symbio TASK-249/TASK-254, see
    /// <c>DataBase.IsNonOperandArgument</c>);</item>
    /// <item>MongoDB forwards the raw expression to the driver's LINQ translator, which does not know
    /// the method at all and throws <c>NotSupportedException: Specified method is not supported</c>
    /// (TASK-218).</item>
    /// </list>
    /// <para>
    /// <b>Measured, 2026-08-16, driver 3.2.0 / .NET 10.</b> Of the backends that translate a filter
    /// expression, only MongoDB is affected: SQL's <c>ParseConditionExpression</c> and ElasticSearch's
    /// <c>ParseExpression</c> both render <c>IN (1,5)</c> / <c>terms=(1,5)</c> for all four spellings,
    /// because they evaluate the operand themselves and the unwrapped array evaluates fine. So
    /// <see cref="Rewrite{T}"/> lives here, available to every store, but is <b>wired only in MongoDB</b>
    /// — the same "available to all, wired per backend after measuring" discipline as
    /// <c>PredicateScope</c> (CLAUDE.md § Conventions).
    /// </para>
    /// </remarks>
    public static class SpanContains
    {
        /// <summary>
        /// Strips the implicit <c>ReadOnlySpan&lt;T&gt;</c> / <c>Span&lt;T&gt;</c> conversion the compiler
        /// inserts when an array binds <c>MemoryExtensions.Contains</c>, exposing the underlying
        /// collection so it can be evaluated. A conversion operator is a
        /// <see cref="MethodCallExpression"/> named <c>op_Implicit</c> on the span type, not a
        /// <see cref="UnaryExpression"/>, so an ordinary Convert-unwrap does not see it.
        /// </summary>
        public static Expression UnwrapSpanConversion(Expression expr)
        {
            while (true)
            {
                var unwrapped = UnwrapConvert(expr);
                if (unwrapped == null) return expr;
                expr = unwrapped;

                if (expr is MethodCallExpression conversion
                    && conversion.Method.IsSpecialName
                    && (conversion.Method.Name == "op_Implicit" || conversion.Method.Name == "op_Explicit")
                    && conversion.Object == null
                    && conversion.Arguments.Count == 1)
                {
                    expr = conversion.Arguments[0];
                    continue;
                }
                return expr;
            }
        }

        /// <summary>Strips Convert / ConvertChecked / Quote wrappers.</summary>
        public static Expression? UnwrapConvert(Expression? expr)
        {
            while (expr is UnaryExpression u
                && u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote)
            {
                expr = u.Operand;
            }
            return expr;
        }

        /// <summary>
        /// Rewrites every <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c> node in
        /// <paramref name="filter"/> to the equivalent <c>Enumerable.Contains(IEnumerable&lt;T&gt;, T)</c>,
        /// which every LINQ translator understands. Semantically identical — the same set, the same
        /// element, default equality — and a no-op for a predicate that contains no such node.
        /// </summary>
        public static Expression<Func<T, bool>>? Rewrite<T>(Expression<Func<T, bool>>? filter)
        {
            if (filter == null) return null;

            var body = new Rewriter().Visit(filter.Body);
            return ReferenceEquals(body, filter.Body)
                ? filter
                : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
        }

        private sealed class Rewriter : ExpressionVisitor
        {
            private static readonly MethodInfo EnumerableContains = typeof(Enumerable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Enumerable.Contains)
                          && m.GetParameters().Length == 2);

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var rewritten = TryRewrite(node);
                return rewritten == null ? base.VisitMethodCall(node) : Visit(rewritten);
            }

            private static Expression? TryRewrite(MethodCallExpression node)
            {
                if (node.Method.DeclaringType != typeof(MemoryExtensions)) return null;
                if (node.Method.Name != nameof(MemoryExtensions.Contains)) return null;
                if (node.Arguments.Count is not (2 or 3)) return null;

                // A REAL comparer cannot be honoured by Enumerable.Contains(source, item), and the
                // 3-argument Enumerable overload is no more translatable than the span one — so leave
                // the node alone and let the driver report it. Only the compiler-inserted `null`
                // comparer is rewritten. Narrow on purpose: a wrong rewrite is worse than the throw.
                if (node.Arguments.Count == 3
                    && !(SpanContains.UnwrapConvert(node.Arguments[2]) is ConstantExpression { Value: null }))
                {
                    return null;
                }

                var element = node.Method.GetGenericArguments().FirstOrDefault();
                if (element == null) return null;

                var source = SpanContains.UnwrapSpanConversion(node.Arguments[0]);
                var sequence = typeof(IEnumerable<>).MakeGenericType(element);

                // The unwrap must have produced something that IS a sequence. A caller holding a real
                // Span<T> variable has nothing behind the conversion, and rewriting that would not compile.
                if (!sequence.IsAssignableFrom(source.Type)) return null;

                return Expression.Call(
                    EnumerableContains.MakeGenericMethod(element),
                    source.Type == sequence ? source : Expression.Convert(source, sequence),
                    node.Arguments[1]);
            }
        }
    }
}
