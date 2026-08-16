using System;
using System.Linq.Expressions;

namespace Birko.Data.Expressions
{
    /// <summary>
    /// Rewrites <c>x.Col.Date &lt;op&gt; constant</c> into a half-open range over the raw member —
    /// <c>x.Col.Date == d</c> becomes <c>x.Col &gt;= d &amp;&amp; x.Col &lt; d.AddDays(1)</c> — for the
    /// backends that hand a raw <see cref="Expression"/> to a driver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why (TASK-224).</b> CosmosDB stores a <see cref="DateTime"/> as an ISO string, and its LINQ
    /// provider translates <c>.Date</c> as a <i>JSON sub-property access</i>:
    /// <c>x.CreatedAt.Date == d</c> emits <c>WHERE (root["CreatedAt"]["Date"] = "…")</c>. That addresses a
    /// member of a string, which does not exist, so the query is valid, runs, and returns
    /// <b>zero rows with no error</b>. Measured against the emulator: 0 rows where the compiled-delegate
    /// oracle says 1.
    /// </para>
    /// <para>
    /// The range form is also <b>sargable</b> — a function on the stored value defeats an index — and it
    /// carries no dialect assumption, which is why it is the shape the SQL connector settled on too.
    /// </para>
    /// <para>
    /// <b>⚠ There is a second implementation of these semantics.</b>
    /// <c>Birko.Data.SQL.DataBase.TryBuildDateTruncatedComparison</c> performs the same rewrite for the
    /// same reason (Symbio TASK-355 — <c>DATE(col) = @p</c> compared a 10-character date against a full
    /// timestamp and matched nothing, also silently), but emits <c>Condition</c> objects rather than an
    /// expression tree, so the code cannot be shared as it stands. The <i>operator table below and the
    /// one there must agree</i>. Consolidating is possible — run this pre-pass before the SQL parser and
    /// delete that method, since the parser would then only ever see plain comparisons — but that is a
    /// change to heavily-tested translation code and is deliberately not done blind here.
    /// </para>
    /// </remarks>
    public static class DateTruncation
    {
        /// <summary>
        /// Rewrites every <c>x.Member.Date &lt;op&gt; constant</c> comparison in <paramref name="filter"/>.
        /// Returns <paramref name="filter"/> unchanged, by reference, when there is nothing to rewrite.
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
            protected override Expression VisitBinary(BinaryExpression node)
            {
                var rewritten = TryRewrite(node);
                return rewritten == null ? base.VisitBinary(node) : rewritten;
            }

            private static Expression? TryRewrite(BinaryExpression node)
            {
                var leftIsCol = TryGetDateTruncatedMember(node.Left, out var leftInner);
                var rightIsCol = TryGetDateTruncatedMember(node.Right, out var rightInner);

                // Both or neither: not this shape. Column-vs-column `a.Date == b.Date` is a genuine
                // comparison of two stored values and has no half-open range to become.
                if (leftIsCol == rightIsCol) return null;

                var member = leftIsCol ? leftInner : rightInner;
                var valueExpr = leftIsCol ? node.Right : node.Left;
                if (ReferencesParameter(valueExpr)) return null;

                // Mirror the operator when the member is on the right (`d < x.Col.Date`).
                var op = node.NodeType;
                if (!leftIsCol)
                {
                    op = op switch
                    {
                        ExpressionType.LessThan => ExpressionType.GreaterThan,
                        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                        ExpressionType.GreaterThan => ExpressionType.LessThan,
                        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                        _ => op,   // Equal / NotEqual are symmetric
                    };
                }

                DateTime day;
                try
                {
                    if (Evaluate(valueExpr) is not DateTime dt) return null;
                    day = dt.Date;
                }
                catch
                {
                    return null;   // not evaluatable now — leave it for the driver
                }
                var next = day.AddDays(1);

                // The member may be DateTime?; compare against a matching constant so the tree stays
                // well typed. Nullable lifting then gives the same three-valued semantics as before.
                Expression Bound(DateTime v) => Expression.Constant(
                    member.Type == typeof(DateTime?) ? (DateTime?)v : v, member.Type);

                // ⚠ This table must agree with Birko.Data.SQL.DataBase.TryBuildDateTruncatedComparison.
                return op switch
                {
                    // .Date == d  ⟺  d <= col < d+1
                    ExpressionType.Equal => Expression.AndAlso(
                        Expression.GreaterThanOrEqual(member, Bound(day)),
                        Expression.LessThan(member, Bound(next))),
                    // .Date != d  ⟺  col < d OR col >= d+1
                    ExpressionType.NotEqual => Expression.OrElse(
                        Expression.LessThan(member, Bound(day)),
                        Expression.GreaterThanOrEqual(member, Bound(next))),
                    ExpressionType.LessThan => Expression.LessThan(member, Bound(day)),
                    ExpressionType.LessThanOrEqual => Expression.LessThan(member, Bound(next)),
                    ExpressionType.GreaterThan => Expression.GreaterThanOrEqual(member, Bound(next)),
                    ExpressionType.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, Bound(day)),
                    _ => null,
                };
            }

            /// <summary>
            /// True when <paramref name="expr"/> is <c>&lt;parameter-bound member&gt;.Date</c>, yielding
            /// the underlying member. Mirrors <c>DataBase.TryGetDateTruncatedColumn</c>.
            /// </summary>
            private static bool TryGetDateTruncatedMember(Expression expr, out Expression inner)
            {
                inner = null!;
                if (SpanContains.UnwrapConvert(expr) is not MemberExpression m) return false;
                if (m.Member.Name != nameof(DateTime.Date) || m.Member.DeclaringType != typeof(DateTime)) return false;
                if (m.Expression is not MemberExpression innerMember) return false;
                if (!ReferencesParameter(innerMember)) return false;
                inner = innerMember;
                return true;
            }

            private static object? Evaluate(Expression expr)
                => expr is ConstantExpression c
                    ? c.Value
                    : Expression.Lambda(Expression.Convert(expr, typeof(object))).Compile().DynamicInvoke();

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
}
