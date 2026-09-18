using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Expressions
{
    /// <summary>
    /// Answers "how much does this filter predicate constrain?" — **before** any backend translates it.
    /// The portable counterpart to the SQL connector's condition-tree reduction, for the backends that hand
    /// a raw <see cref="Expression"/> straight to a driver and so never build a condition tree at all
    /// (MongoDB, and any future store of that shape).
    /// </summary>
    /// <remarks>
    /// <para><b>Why the expression and not the translated query (TASK-212).</b> A guard on the emitted query
    /// has to recognise every way a backend can spell "everything", and the spellings do not look alike.
    /// Measured on MongoDB.Driver 3.2.0: <c>x =&gt; !empty.Contains(x.Amount)</c> renders
    /// <c>{ "Amount": { "$nin": [] } }</c> — a <b>one-element</b> document that is indistinguishable, by
    /// inspection, from an ordinary field predicate. So "refuse an empty filter document" would not catch it,
    /// exactly as the SQL side's "refuse when nothing was rendered" did not catch <c>1 = 1</c> (TASK-137).
    /// The expression, by contrast, is unambiguous: <c>!empty.Contains(x.Amount)</c> is true of every entity
    /// by C# semantics, whatever the driver does downstream. Guarding here refuses the <i>expressed intent</i>
    /// and cannot be defeated by a translation nobody anticipated.</para>
    /// <para><b>The two questions are deliberately separate.</b> <see cref="IsExplicitAllRows"/> asks "did the
    /// caller say every row, out loud" and answers yes only for a single normalized constant — the documented
    /// <c>DeleteAll()</c> / <c>UpdateAll()</c> synonym. <see cref="ReducesToAllRows"/> asks "does this happen
    /// to cover every row", which is the case a destructive path must refuse. Conflating them would either
    /// break the documented synonym or bless the accident.</para>
    /// <para><b>Narrow on purpose.</b> Every shape below is provably always-true in C# semantics; nothing is
    /// inferred from a name or a type. A refusal that fires on a predicate which does constrain something is
    /// worse than the hole it closes, so when in doubt this returns <c>false</c> and the write proceeds.</para>
    /// <para><b>It evaluates the collection operand, so a side-effecting one is evaluated here too.</b>
    /// Deciding whether a set is empty means materialising it, which for anything but a constant means
    /// compiling that sub-expression. The collection is already evaluated by the backend when it builds the
    /// query, so a caller writing <c>GetTheSet().Contains(x.Field)</c> gets that call made twice rather than
    /// once. Judged acceptable — the operand must not reference the entity (checked first), the compile
    /// happens only on a destructive path that is about to hit the network or disk anyway, and any throw is
    /// swallowed into "cannot prove it empty". Worth knowing before putting a side effect in a filter.</para>
    /// </remarks>
    public static class PredicateScope
    {
        /// <summary>
        /// True when the caller explicitly asked for every row with a constant predicate — <c>x =&gt; true</c>,
        /// <c>x =&gt; 1 == 1</c>, <c>x =&gt; capturedTrueFlag</c>, all of which
        /// <see cref="ExpressionNormalizer"/> folds to the same single <see cref="ConstantExpression"/>.
        /// </summary>
        /// <remarks>
        /// A <b>one-node</b> test, not a catalogue of always-true shapes: a whitelist rots the moment a new
        /// reduction site appears, and its failure mode is a refused destructive operation on working code.
        /// </remarks>
        public static bool IsExplicitAllRows(LambdaExpression? predicate)
        {
            if (predicate == null) return false;
            var body = ExpressionNormalizer.Normalize(predicate.Body) ?? predicate.Body;
            return body is ConstantExpression constant && constant.Value is bool value && value;
        }

        /// <summary>
        /// True when the predicate matches every entity — whether or not the caller meant it to. Includes the
        /// explicit constant, so a destructive guard tests <c>ReducesToAllRows &amp;&amp; !IsExplicitAllRows</c>
        /// to refuse the accident while leaving the documented synonym working.
        /// </summary>
        public static bool ReducesToAllRows(LambdaExpression? predicate)
        {
            if (predicate == null) return false;
            var body = ExpressionNormalizer.Normalize(predicate.Body) ?? predicate.Body;
            return IsAlwaysTrue(body);
        }

        private static bool IsAlwaysTrue(Expression? expr)
        {
            expr = Unwrap(expr);
            if (expr == null) return false;

            switch (expr)
            {
                // `x => true`, and everything the normalizer folds into it.
                case ConstantExpression c when c.Value is bool b:
                    return b;

                // `A && B` covers everything only when both do; `A || B` when either does.
                case BinaryExpression andExpr when andExpr.NodeType is ExpressionType.AndAlso or ExpressionType.And:
                    return IsAlwaysTrue(andExpr.Left) && IsAlwaysTrue(andExpr.Right);
                case BinaryExpression orExpr when orExpr.NodeType is ExpressionType.OrElse or ExpressionType.Or:
                    return IsAlwaysTrue(orExpr.Left) || IsAlwaysTrue(orExpr.Right);

                // `!(…)` is always-true exactly when its operand is always-FALSE. This is the arm that catches
                // the defect shape: `!empty.Contains(x.Field)`, whose operand matches nothing.
                case UnaryExpression not when not.NodeType == ExpressionType.Not:
                    return IsAlwaysFalse(not.Operand);

                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the expression matches NO entity. Deliberately not the negation of
        /// <see cref="IsAlwaysTrue"/>: most predicates are neither, and both questions answer <c>false</c> for
        /// those.
        /// </summary>
        private static bool IsAlwaysFalse(Expression? expr)
        {
            expr = Unwrap(expr);
            if (expr == null) return false;

            switch (expr)
            {
                case ConstantExpression c when c.Value is bool b:
                    return !b;

                // `set.Contains(x.Field)` over an EMPTY set matches nothing — so its negation matches
                // everything, which is the whole point of this class.
                case MethodCallExpression call:
                    return IsEmptySetContains(call);

                case BinaryExpression andExpr when andExpr.NodeType is ExpressionType.AndAlso or ExpressionType.And:
                    return IsAlwaysFalse(andExpr.Left) || IsAlwaysFalse(andExpr.Right);
                case BinaryExpression orExpr when orExpr.NodeType is ExpressionType.OrElse or ExpressionType.Or:
                    return IsAlwaysFalse(orExpr.Left) && IsAlwaysFalse(orExpr.Right);

                case UnaryExpression not when not.NodeType == ExpressionType.Not:
                    return IsAlwaysTrue(not.Operand);

                default:
                    return false;
            }
        }

        /// <summary>
        /// True when <paramref name="call"/> is <c>collection.Contains(value)</c> over a collection that
        /// evaluates to an empty sequence. Both the instance form (<c>List&lt;T&gt;.Contains</c>, collection
        /// in <see cref="MethodCallExpression.Object"/>) and the extension form
        /// (<c>Enumerable.Contains(source, value)</c>, collection in argument 0) are recognised.
        /// </summary>
        private static bool IsEmptySetContains(MethodCallExpression call)
        {
            if (call.Method.Name != "Contains") return false;
            // A string Contains is a substring test, not set membership — never always-true when negated.
            if (call.Method.DeclaringType == typeof(string)) return false;
            if (call.Object?.Type == typeof(string)) return false;

            var collection = call.Object ?? (call.Arguments.Count > 0 ? call.Arguments[0] : null);
            if (collection == null) return false;

            // On .NET 9+ an ARRAY `set.Contains(x.Col)` binds to MemoryExtensions.Contains rather than to
            // Enumerable.Contains, so the collection arrives wrapped in an implicit ReadOnlySpan conversion.
            // A span is a ref struct and cannot be boxed, so evaluating the wrapper throws and the analyser
            // would silently decline — leaving every array-typed caller unguarded. Unwrap first. The unwrap
            // is shared with SpanContains.Rewrite (TASK-218), which fixes the same binding for the MongoDB
            // driver — one producer, so the guard and the rewriter cannot disagree about what a span-bound
            // Contains looks like. (The SQL parser hit it from a third direction; see
            // DataBase.IsNonOperandArgument.)
            collection = SpanContains.UnwrapSpanConversion(collection);

            // The collection must be evaluatable without the entity — a captured local, a field, a constant.
            // Anything referencing the lambda parameter is a per-entity collection and says nothing about
            // scope.
            if (ReferencesParameter(collection)) return false;

            object? value;
            try
            {
                value = Expression.Lambda(collection).Compile().DynamicInvoke();
            }
            catch (Exception)
            {
                // Cannot evaluate it → cannot claim it is empty → do not refuse.
                return false;
            }

            // A null collection is NOT claimed to be empty. `null.Contains(x)` throws when evaluated and a
            // driver rejects it at translation, so the operation fails either way — there is nothing to guard,
            // and modelling it would be modelling a case that never reaches a write.
            if (value == null) return false;
            if (value is IEnumerable sequence)
            {
                foreach (var _ in sequence) return false; // stops at the first element
                return true;
            }
            return false;
        }

        private static bool ReferencesParameter(Expression expr)
            => new ParameterFinder().Found(expr);

        private static Expression? Unwrap(Expression? expr) => SpanContains.UnwrapConvert(expr);

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
