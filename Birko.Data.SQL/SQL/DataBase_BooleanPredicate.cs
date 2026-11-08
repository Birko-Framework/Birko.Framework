using System;
using System.Linq.Expressions;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        /// <summary>
        /// TASK-863 — the one producer of a boolean compared with a boolean. Rewrites, in predicate position only, every
        /// shape the condition parser has no branch for into the ones it has: <c>e == true</c> → <c>e</c>,
        /// <c>e == false</c> / <c>e != true</c> → <c>!e</c> (either side), a predicate compared with a predicate into its
        /// AND/OR expansion, and <c>.Value</c> of a <c>bool?</c> into the lifted <c>Opt == true</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the comparison branch parsed both operands into
        /// one condition, so the constant either became the condition's value (<c>Name.StartsWith("a") == true</c> bound
        /// <c>true</c> as the LIKE pattern) or was lost (<c>x.Score.HasValue == false</c> rendered <c>Score IS NOT NULL</c>,
        /// <c>(x.Score == x.Peer) == false</c> the equality itself): the complement, so a Delete removed exactly the rows
        /// the caller meant to keep. A bare <c>x.Opt.Value</c> resolved to the column with no value: <c>Opt = NULL</c>.
        /// </para>
        /// <para>
        /// Only operands typed <c>bool</c> are touched: a lifted <c>bool?</c> comparison (<c>x.Opt != true</c>) is already
        /// a column-against-value condition that TASK-859's NULL legs make C#-exact. Rewriting <c>e == false</c> to
        /// <c>!e</c> is exact because <c>e</c> is a non-nullable C# boolean, and the parser renders <c>!e</c> with C#'s
        /// two-valued semantics. A comparison of two plain <c>bool</c> columns is left to the column-comparison builder.
        /// </para>
        /// <para>
        /// <c>.Value</c> of a <c>bool?</c> throws in C# on a NULL row, so the oracle only answers where it is guarded or the
        /// row is not NULL. It is translated so that a NULL row matches neither <c>v</c> nor <c>v == false</c> nor
        /// <c>!v</c> — <c>Opt = 1</c>, <c>Opt = 0</c>, <c>Opt = 0</c> — which agrees with C# wherever C# answers. Under an
        /// enclosing negation of a larger group (<c>!(x.Opt.Value || …)</c>) the NULL row's answer is SQL's choice, as it is
        /// C#'s exception.
        /// </para>
        /// </remarks>
        internal static Expression RewriteBooleanComparisons(Expression predicate)
        {
            switch (predicate)
            {
                case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse or ExpressionType.And or ExpressionType.Or } logical
                    when logical.Type == typeof(bool) && logical.Left.Type == typeof(bool) && logical.Right.Type == typeof(bool):
                    {
                        var left = RewriteBooleanComparisons(logical.Left);
                        var right = RewriteBooleanComparisons(logical.Right);
                        return left == logical.Left && right == logical.Right
                            ? logical
                            : Expression.MakeBinary(logical.NodeType, left, right);
                    }

                case UnaryExpression { NodeType: ExpressionType.Not } not when not.Type == typeof(bool):
                    {
                        if (TryNullableBoolValue(not.Operand, out var negatedColumn))
                            return Expression.Equal(negatedColumn, Expression.Constant(false, typeof(bool?)));
                        var operand = RewriteBooleanComparisons(not.Operand);
                        return operand == not.Operand ? not : Negate(operand);
                    }

                case BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } compare
                    when compare.Left.Type == typeof(bool) && compare.Right.Type == typeof(bool) && compare.Method == null:
                    return RewriteBooleanEquality(compare);

                default:
                    return TryNullableBoolValue(predicate, out var column)
                        ? Expression.Equal(column, Expression.Constant(true, typeof(bool?)))
                        : predicate;
            }
        }

        private static Expression RewriteBooleanEquality(BinaryExpression compare)
        {
            var equal = compare.NodeType == ExpressionType.Equal;
            var leftConstant = compare.Left as ConstantExpression;
            var rightConstant = compare.Right as ConstantExpression;
            if (leftConstant != null && rightConstant != null) return compare;

            var constant = leftConstant ?? rightConstant;
            if (constant?.Value is bool value)
            {
                var other = leftConstant != null ? compare.Right : compare.Left;
                // `x.Opt.Value == c` keeps a NULL row out, as the lifted `x.Opt == c` does.
                if (TryNullableBoolValue(other, out var column))
                    return Expression.Equal(column, Expression.Constant(value == equal, typeof(bool?)));
                // A plain bool column against a constant is already a column-against-value condition: `Flag = 0`.
                if (IsPlainParameterMember(other)) return compare;
                var predicate = RewriteBooleanComparisons(other);
                return value == equal ? predicate : Negate(predicate);
            }

            // Two plain bool columns: the column-comparison builder renders `A = B` / `A <> B` exactly.
            if (IsPlainParameterMember(compare.Left) && IsPlainParameterMember(compare.Right)) return compare;

            var a = RewriteBooleanComparisons(compare.Left);
            var b = RewriteBooleanComparisons(compare.Right);
            return equal
                ? Expression.OrElse(Expression.AndAlso(a, b), Expression.AndAlso(Negate(a), Negate(b)))
                : Expression.OrElse(Expression.AndAlso(a, Negate(b)), Expression.AndAlso(Negate(a), b));
        }

        private static Expression Negate(Expression predicate)
            => predicate is UnaryExpression { NodeType: ExpressionType.Not } inner && inner.Operand.Type == typeof(bool)
                ? inner.Operand
                : Expression.Not(predicate);

        /// <summary><c>x.Opt.Value</c> where <c>x.Opt</c> is a <c>bool?</c> read from the row.</summary>
        private static bool TryNullableBoolValue(Expression expression, out Expression column)
        {
            column = null!;
            if (expression is not MemberExpression { Member.Name: "Value", Expression: { } nullable }
                || nullable.Type != typeof(bool?)
                || !ContainsParameter(nullable))
                return false;
            column = nullable;
            return true;
        }

        /// <summary><c>x.Col</c>, or <c>((I)x).Col</c>: a member read straight off the lambda parameter.</summary>
        private static bool IsPlainParameterMember(Expression expression)
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert && convert.Operand is MemberExpression)
                expression = convert.Operand;
            return expression is MemberExpression { Expression: { } owner }
                && (owner.NodeType == ExpressionType.Parameter
                    || owner is UnaryExpression { NodeType: ExpressionType.Convert, Operand.NodeType: ExpressionType.Parameter });
        }
    }
}
