using System;
using System.Linq.Expressions;

namespace Birko.Data.Expressions;

/// <summary>
/// Backend-agnostic pre-pass that rewrites a filter / value expression tree into a canonical
/// form the hand-rolled store parsers (SQL <c>DataBase.ParseConditionExpression</c> /
/// <c>ParseExpression</c>, ElasticSearch) can translate uniformly. Two responsibilities:
///
/// <list type="number">
/// <item>
/// <b>Funcletization (partial evaluation).</b> Any subtree that does not reference a lambda
/// parameter is compiled and replaced with its <see cref="ConstantExpression"/> value. This
/// collapses closure variables and — crucially — <i>parameter-free</i> ternary, null-coalescing
/// and arithmetic (e.g. <c>useDraft ? Draft : Active</c>, <c>basePrice ?? 0</c>, <c>2 * 3</c>) to
/// constants, so no downstream parser needs a special case for those.
/// </item>
/// <item>
/// <b>Desugaring the parameter-dependent survivors</b> into nodes the parsers already understand:
/// <list type="bullet">
/// <item>a <b>boolean-typed</b> conditional <c>c ? t : f</c> is expanded to boolean algebra
/// <c>(c &amp;&amp; t) || (!c &amp;&amp; f)</c>, so predicate parsers that only understand
/// AND/OR/NOT need no ternary logic;</item>
/// <item>a <b>boolean-typed</b> null-coalescing <c>a ?? b</c> (a is <see cref="Nullable{T}"/> of
/// bool) is expanded to <c>(a == true) || (a == null &amp;&amp; b)</c> — again pure boolean
/// algebra;</item>
/// <item>a <b>non-boolean</b> conditional / coalescing (a value position, e.g. an Update SET
/// right-hand side, or a numeric operand of a comparison) is left intact for the value parser to
/// render as <c>CASE WHEN … END</c> / <c>COALESCE(…)</c>.</item>
/// </list>
/// </item>
/// </list>
///
/// The result is semantically identical to the input for every tree the C# compiler can produce;
/// only the tree <i>shape</i> changes. Compiled-delegate backends (InMemory / JSON / XML) and
/// native-LINQ backends (Mongo / Cosmos / Raven) never need it — they honour the raw constructs
/// already — so this runs only inside the hand-rolled translators.
/// </summary>
public sealed class ExpressionNormalizer : ExpressionVisitor
{
    /// <summary>
    /// Normalizes an arbitrary expression subtree. Returns <paramref name="node"/> unchanged when
    /// it is <see langword="null"/>.
    /// </summary>
    public static Expression? Normalize(Expression? node)
        => node == null ? null : new ExpressionNormalizer().Visit(node);

    public override Expression? Visit(Expression? node)
    {
        if (node == null)
        {
            return null;
        }

        // Funcletize: fold any parameter-free subtree (except bare constants / parameters / lambdas)
        // to a constant. This is the single highest-leverage transform — it removes every
        // parameter-free ternary / ?? / arithmetic before the parsers ever see it.
        if (node.NodeType is not (ExpressionType.Constant or ExpressionType.Parameter
                or ExpressionType.Lambda or ExpressionType.Quote)
            && node.Type != typeof(void)
            && !ContainsParameter(node)
            && TryFold(node, out var folded))
        {
            return folded;
        }

        return base.Visit(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Coalesce)
        {
            var left = Visit(node.Left)!;
            var right = Visit(node.Right)!;

            // Boolean predicate use: a ?? b  (a is bool?)  ≡  (a == true) || (a == null && b).
            // Pure boolean algebra, so the predicate parsers need no coalescing logic.
            if (node.Type == typeof(bool))
            {
                var eqTrue = Expression.Equal(left, Expression.Constant(true, left.Type));
                var eqNull = Expression.Equal(left, Expression.Constant(null, left.Type));
                var rightBool = right.Type == typeof(bool) ? right : Expression.Convert(right, typeof(bool));
                return Visit(Expression.OrElse(eqTrue, Expression.AndAlso(eqNull, rightBool)))!;
            }

            // Value use (numeric / string operand): keep it — the value parser renders COALESCE(a, b).
            return node.Update(left, node.Conversion, right);
        }

        return base.VisitBinary(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        var test = Visit(node.Test)!;
        var ifTrue = Visit(node.IfTrue)!;
        var ifFalse = Visit(node.IfFalse)!;

        // A constant test collapses to the surviving branch.
        if (test is ConstantExpression c && c.Value is bool constTest)
        {
            return constTest ? ifTrue : ifFalse;
        }

        // Boolean-position ternary: c ? t : f  ≡  (c && t) || (!c && f).
        if (node.Type == typeof(bool) && ifTrue.Type == typeof(bool) && ifFalse.Type == typeof(bool))
        {
            return Expression.OrElse(
                Expression.AndAlso(test, ifTrue),
                Expression.AndAlso(Expression.Not(test), ifFalse));
        }

        // Non-boolean (value position) ternary: leave it for the value parser to render as CASE WHEN.
        return node.Update(test, ifTrue, ifFalse);
    }

    private static bool TryFold(Expression node, out Expression folded)
    {
        folded = node;
        try
        {
            var body = node.Type.IsValueType ? (Expression)Expression.Convert(node, typeof(object)) : node;
            var value = Expression.Lambda<Func<object?>>(body).Compile()();
            folded = Expression.Constant(value, node.Type);
            return true;
        }
        catch
        {
            // Not evaluatable at parse time (throws / unsupported) — leave the node for the parser.
            return false;
        }
    }

    private static bool ContainsParameter(Expression expr)
    {
        var finder = new ParameterFinder();
        finder.Visit(expr);
        return finder.Found;
    }

    /// <summary>
    /// Flags whether an expression references <i>any</i> <see cref="ParameterExpression"/>. A
    /// nested lambda's own parameter counts as "contains a parameter", which keeps funcletization
    /// conservative (a subtree wrapping an inner lambda is left for the parser) — safe, never wrong.
    /// </summary>
    private sealed class ParameterFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
            => Found || node == null ? node : base.Visit(node);

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return node;
        }
    }
}
