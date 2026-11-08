using System;
using System.Linq.Expressions;

namespace Birko.Data.Expressions
{
    /// <summary>
    /// Folds a lifted ordering against null (<c>x.Score &gt; nul</c> with <c>nul</c> null) to the constant <c>false</c> C#
    /// defines it as, then reduces the boolean constants that leaves — for the backends that hand a filter to a translator
    /// whose own null semantics differ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why (TASK-867).</b> Measured live: RavenDB renders <c>Score &gt; null</c> as matching every non-null Score, so
    /// <c>Score &gt; nul || A &gt; 4</c> read and wrote the wrong rows; Cosmos DB's <c>Score &gt; null</c> is undefined and so
    /// is its NOT, so <c>!(Score &gt; nul) &amp;&amp; A &gt; 4</c> missed rows; Elasticsearch refused the shape. Folding before
    /// rendering hands every translator a tree with no null ordering in it, so none of them has to know what null means.
    /// </para>
    /// <para>
    /// <b>One producer.</b> <see cref="IsNullOrdering"/> is also what <see cref="PredicateScope"/> asks, so the destructive
    /// guard and the translators cannot disagree about which comparison is always false (TASK-865).
    /// </para>
    /// <para>
    /// <b>Run it after the guard, never before.</b> Folded, <c>!(x.Score &gt; nul)</c> is the constant <c>true</c>, which
    /// <see cref="PredicateScope.IsExplicitAllRows"/> reads as the documented all-rows synonym. The stores apply it in
    /// their <c>*Core</c> translation, after the public write has refused the filter.
    /// </para>
    /// <para>
    /// After the fold a boolean constant survives only as the whole body: <c>&amp;&amp;</c>, <c>||</c>, <c>!</c> and a
    /// conditional over a constant are reduced. A translator still has to render a top-level <c>true</c> / <c>false</c>
    /// (<see cref="MatchesNothing"/>).
    /// </para>
    /// </remarks>
    public static class NullOrderingFold
    {
        /// <summary>
        /// Folds every null ordering in <paramref name="filter"/>. Returns <paramref name="filter"/> unchanged, by
        /// reference, when it holds none.
        /// </summary>
        public static Expression<Func<T, bool>>? Rewrite<T>(Expression<Func<T, bool>>? filter)
        {
            if (filter == null) return null;
            var body = Fold(filter.Body)!;
            return ReferenceEquals(body, filter.Body) ? filter : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
        }

        /// <summary>Folds every null ordering in <paramref name="body"/>; the same node when it holds none.</summary>
        public static Expression? Fold(Expression? body) => body == null ? null : new Folder().Visit(body);

        /// <summary>True when the filter's body is the constant <c>false</c>: it matches no entity.</summary>
        public static bool MatchesNothing(LambdaExpression? filter)
            => filter?.Body is ConstantExpression { Value: false };

        /// <summary>
        /// True when <paramref name="ordering"/> is a LIFTED <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c> one of whose
        /// operands is null: the null constant, or a subtree that does not read the entity and evaluates to null
        /// (<c>x.Score &gt; nul</c>, <c>nul &lt; x.Score</c>, <c>x.Score &lt;= (int?)null</c>). C# defines such an ordering as
        /// <c>false</c>, a lifted user-defined operator included.
        /// </summary>
        /// <remarks>
        /// <c>==</c> / <c>!=</c> are not here: <c>x.Score == nul</c> is true of the null rows, an ordinary filter. Neither is
        /// an ordering that is not lifted — an operator declared on nullable operands decides for itself what null means.
        /// An operand that cannot be evaluated is not claimed null.
        /// </remarks>
        public static bool IsNullOrdering(BinaryExpression ordering)
        {
            if (ordering.NodeType is not (ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual))
            {
                return false;
            }
            if (!ordering.IsLifted || ordering.IsLiftedToNull)
            {
                return false;
            }
            return IsNull(ordering.Left) || IsNull(ordering.Right);
        }

        private static bool IsNull(Expression operand)
        {
            var unwrapped = SpanContains.UnwrapConvert(operand);
            if (unwrapped is ConstantExpression constant) return constant.Value == null;
            if (unwrapped == null || ReferencesParameter(operand)) return false;
            try
            {
                var body = operand.Type.IsValueType ? Expression.Convert(operand, typeof(object)) : operand;
                return Expression.Lambda<Func<object?>>(body).Compile()() == null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ReferencesParameter(Expression expr)
        {
            var finder = new ParameterFinder();
            finder.Visit(expr);
            return finder.Found;
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }

            public override Expression? Visit(Expression? node) => Found || node == null ? node : base.Visit(node);

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found = true;
                return node;
            }
        }

        private static bool IsBool(Expression e, out bool value)
        {
            if (e is ConstantExpression { Value: bool b })
            {
                value = b;
                return true;
            }
            value = false;
            return false;
        }

        private sealed class Folder : ExpressionVisitor
        {
            protected override Expression VisitBinary(BinaryExpression node)
            {
                if (IsNullOrdering(node)) return Expression.Constant(false);

                var visited = base.VisitBinary(node);
                if (ReferenceEquals(visited, node) || visited is not BinaryExpression binary || binary.Type != typeof(bool))
                {
                    return visited;
                }

                var leftConst = IsBool(binary.Left, out var left);
                var rightConst = IsBool(binary.Right, out var right);
                switch (binary.NodeType)
                {
                    case ExpressionType.AndAlso:
                    case ExpressionType.And:
                        if ((leftConst && !left) || (rightConst && !right)) return Expression.Constant(false);
                        if (leftConst) return binary.Right;
                        if (rightConst) return binary.Left;
                        return binary;
                    case ExpressionType.OrElse:
                    case ExpressionType.Or:
                        if ((leftConst && left) || (rightConst && right)) return Expression.Constant(true);
                        if (leftConst) return binary.Right;
                        if (rightConst) return binary.Left;
                        return binary;
                    case ExpressionType.Equal when leftConst && rightConst:
                        return Expression.Constant(left == right);
                    case ExpressionType.NotEqual when leftConst && rightConst:
                    case ExpressionType.ExclusiveOr when leftConst && rightConst:
                        return Expression.Constant(left != right);
                    default:
                        return binary;
                }
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                var visited = base.VisitUnary(node);
                if (!ReferenceEquals(visited, node)
                    && visited is UnaryExpression { NodeType: ExpressionType.Not } not
                    && not.Type == typeof(bool)
                    && IsBool(not.Operand, out var operand))
                {
                    return Expression.Constant(!operand);
                }
                return visited;
            }

            protected override Expression VisitConditional(ConditionalExpression node)
            {
                var visited = base.VisitConditional(node);
                if (!ReferenceEquals(visited, node) && visited is ConditionalExpression conditional && IsBool(conditional.Test, out var test))
                {
                    return test ? conditional.IfTrue : conditional.IfFalse;
                }
                return visited;
            }
        }
    }
}
