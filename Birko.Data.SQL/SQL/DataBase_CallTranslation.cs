using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        /// <summary>
        /// TASK-860 — the calls the SQL parsers translate by rewriting them into nodes they already have, before
        /// <c>ExpressionNormalizer</c> runs: <c>GetValueOrDefault()</c> / <c>GetValueOrDefault(d)</c> into <c>??</c>,
        /// <c>a.Equals(b)</c> (the <c>IEquatable&lt;T&gt;</c> overload) and <c>string.Equals(a, b)</c> into <c>==</c>, and a
        /// <c>bool</c> XOR into <c>!=</c>. Only nodes that read the row are touched; a parameter-free call is funcletized.
        /// </summary>
        /// <remarks>
        /// Each is exact: <c>GetValueOrDefault</c> is defined as <c>?? default</c>, <c>IEquatable&lt;T&gt;.Equals</c> as <c>==</c>
        /// for every type the mapping has (string is ordinal in both), and on a non-nullable <c>bool</c> <c>a ^ b</c> is
        /// <c>a != b</c>, which TASK-863 expands. Before (measured on all four providers): the parser dropped
        /// <c>GetValueOrDefault</c> and compared the bare column, so <c>x.Score.GetValueOrDefault() != 5</c> missed every NULL row,
        /// and <c>x.Hits.Equals(x.Score.Value)</c> matched nothing. <c>Equals(object)</c> and a lifted <c>bool?</c> XOR are not
        /// rewritten, and the parser refuses them. A string <c>Equals</c> with <see cref="StringComparison.Ordinal"/> is C#'s
        /// <c>==</c> and becomes it (TASK-895), so it renders through TASK-873's exact equality, over a value or another column;
        /// with any other <see cref="StringComparison"/> it becomes the static <c>string.Equals(row, value, comparison)</c>,
        /// which the parser translates or refuses (TASK-869).
        /// </remarks>
        internal static Expression RewriteTranslatableCalls(Expression body) => new TranslatableCallRewriter().Visit(body)!;

        private sealed class TranslatableCallRewriter : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (!ContainsParameter(node)) return visited;
                var method = visited.Method;

                if (method.Name == nameof(Nullable<int>.GetValueOrDefault) && visited.Object != null
                    && Nullable.GetUnderlyingType(visited.Object.Type) is { } underlying)
                {
                    var fallback = visited.Arguments.Count == 1
                        ? visited.Arguments[0]
                        : Expression.Constant(Activator.CreateInstance(underlying), underlying);
                    return Expression.Coalesce(visited.Object, fallback);
                }

                if (method.Name == nameof(object.Equals) && method.ReturnType == typeof(bool))
                {
                    var parameters = method.GetParameters();
                    // a.Equals(b) — the typed overload only: Equals(object) boxes, and a StringComparison overload is a
                    // case or culture rule SQL equality does not have.
                    if (!method.IsStatic && visited.Object != null && parameters.Length == 1
                        && parameters[0].ParameterType == visited.Object.Type)
                        return Expression.Equal(visited.Object, visited.Arguments[0]);
                    // x.Opt.Equals(v) on a Nullable<T>, and x.Status.Equals(St.Open) on an enum, box v; when v has the
                    // receiver's own type that is ==, the lifted one for a nullable (null == null included). Enum.Equals
                    // compares the type too, so a boxed value of any other type is left alone, and refused.
                    if (!method.IsStatic && visited.Object != null && parameters.Length == 1
                        && parameters[0].ParameterType == typeof(object)
                        && visited.Arguments[0] is UnaryExpression { NodeType: ExpressionType.Convert } boxed
                        && (Nullable.GetUnderlyingType(visited.Object.Type) ?? visited.Object.Type) is { } valueType
                        && (valueType != visited.Object.Type || valueType.IsEnum)
                        && (boxed.Operand.Type == valueType || boxed.Operand.Type == visited.Object.Type))
                        return EqualValues(visited.Object, boxed.Operand);
                    if (method.IsStatic && method.DeclaringType == typeof(string) && parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string))
                        return Expression.Equal(visited.Arguments[0], visited.Arguments[1]);
                    // TASK-869: a.Equals(b, comparison) is string.Equals(a, b, comparison), with the row's side first, so the
                    // parser has one shape. Static, a null receiver is false rather than a NullReferenceException.
                    if (method.DeclaringType == typeof(string) && IsStringComparisonEquals(method))
                    {
                        var (a, b, comparison) = method.IsStatic
                            ? (visited.Arguments[0], visited.Arguments[1], visited.Arguments[2])
                            : (visited.Object!, visited.Arguments[0], visited.Arguments[1]);
                        // TASK-895: Ordinal is C#'s ==, which TASK-873 renders exactly (OrdinalTextEquality): the index
                        // answers it, it has no LIKE pattern limit, and two columns translate. It was a BIN2 LIKE.
                        if (IsOrdinal(comparison)) return Expression.Equal(a, b);
                        if (!ContainsParameter(a) && ContainsParameter(b)) (a, b) = (b, a);
                        return Expression.Call(StaticStringEqualsWithComparison, a, b, comparison);
                    }
                }

                // Contains / StartsWith / EndsWith with a char is the string overload with a one-character string. The char
                // used to be bound as itself, which no provider compares as text (measured: Contains('b') matched only "B").
                if (method.DeclaringType == typeof(string) && !method.IsStatic && visited.Object != null
                    && method.Name is "Contains" or "StartsWith" or "EndsWith"
                    && visited.Arguments.Count > 0 && visited.Arguments[0].Type == typeof(char))
                {
                    var types = method.GetParameters().Select(p => p.ParameterType == typeof(char) ? typeof(string) : p.ParameterType).ToArray();
                    var overload = typeof(string).GetMethod(method.Name, types);
                    if (overload != null)
                    {
                        var arguments = visited.Arguments.ToArray();
                        arguments[0] = Expression.Call(arguments[0], typeof(char).GetMethod(nameof(char.ToString), Type.EmptyTypes)!);
                        return Expression.Call(visited.Object, overload, arguments);
                    }
                }
                return visited;
            }

            /// <summary>
            /// TASK-895 — a <see cref="StringComparison"/> argument that is <see cref="StringComparison.Ordinal"/> and not read
            /// from the row. Anything else (another comparison, one read from the row, one that cannot be evaluated) is left
            /// to <see cref="TranslatableComparison"/>, which translates or refuses it by name.
            /// </summary>
            private static bool IsOrdinal(Expression comparison)
            {
                if (ContainsParameter(comparison)) return false;
                if (comparison is ConstantExpression constant) return constant.Value is StringComparison.Ordinal;
                try
                {
                    return Expression.Lambda(comparison).Compile().DynamicInvoke() is StringComparison.Ordinal;
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
            }

            /// <summary><c>a == b</c> for values of one type; an enum (or nullable enum) is compared as its integer.</summary>
            private static Expression EqualValues(Expression left, Expression right)
            {
                var type = left.Type;
                var underlying = Nullable.GetUnderlyingType(type) ?? type;
                if (right.Type != type) right = Expression.Convert(right, type);
                if (!underlying.IsEnum) return Expression.Equal(left, right);
                var integer = Enum.GetUnderlyingType(underlying);
                var target = type == underlying ? integer : typeof(Nullable<>).MakeGenericType(integer);
                return Expression.Equal(Expression.Convert(left, target), Expression.Convert(right, target));
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                var visited = base.VisitBinary(node);
                if (visited is BinaryExpression { NodeType: ExpressionType.ExclusiveOr } xor
                    && xor.Type == typeof(bool) && xor.Left.Type == typeof(bool) && xor.Right.Type == typeof(bool)
                    && ContainsParameter(node))
                    return Expression.NotEqual(xor.Left, xor.Right);
                if (visited is BinaryExpression { NodeType: ExpressionType.Divide } quotient && ContainsParameter(node)
                    && TryExactDecimalReciprocal(quotient, out var reciprocal))
                    return Expression.Multiply(quotient.Left, reciprocal);
                return visited;
            }
        }

        /// <summary>The most significant digits a reciprocal may have and still be multiplied exactly on SQL Server (38 in all).</summary>
        private const int MaxReciprocalDigits = 10;

        /// <summary>
        /// TASK-870 — the one decision whether a <c>decimal</c> division over the row is exact in SQL, for filters and update
        /// SET values alike (both parsers run this rewrite). A division by a constant whose reciprocal terminates
        /// (<c>c = ±2^i·5^j·10^k</c>: <c>/ 4m</c>, <c>/ 2.5m</c>, <c>/ 100m</c>) has a quotient that terminates a few decimals past
        /// the dividend, so C# computes it exactly, and SQL computes the same value as the product with the exact reciprocal
        /// (<c>x.Amount * 0.25</c>): a decimal product keeps every digit on every provider. Every other decimal division (by
        /// <c>3m</c>, by a column) stays a division, which each server computes at its own scale (measured, TASK-870): the
        /// server dialects refuse it, and SQLite computes it in C#'s decimal.
        /// </summary>
        private static bool TryExactDecimalReciprocal(BinaryExpression quotient, out Expression reciprocal)
        {
            reciprocal = quotient.Right;
            if (UnwrapNullable(quotient.Type) != typeof(decimal) || !ContainsParameter(quotient.Left) || ContainsParameter(quotient.Right))
                return false;
            if (Expression.Lambda(quotient.Right).Compile().DynamicInvoke() is not decimal divisor)
                return false;
            if (!TerminatingReciprocal(divisor, out var exact))
                return false;
            reciprocal = Expression.Constant(exact, quotient.Right.Type);
            return true;
        }

        /// <summary>
        /// <c>1 / divisor</c> when it is a terminating decimal of at most <see cref="MaxReciprocalDigits"/> significant digits:
        /// the divisor's integer mantissa has no prime factor but 2 and 5.
        /// </summary>
        internal static bool TerminatingReciprocal(decimal divisor, out decimal reciprocal)
        {
            reciprocal = 0m;
            var mantissa = Mantissa(divisor);
            if (mantissa.IsZero) return false;
            while (mantissa % 2 == 0) mantissa /= 2;
            while (mantissa % 5 == 0) mantissa /= 5;
            if (!mantissa.IsOne) return false;
            var candidate = 1m / divisor;
            if (candidate * divisor != 1m) return false;
            if (Mantissa(candidate).ToString().TrimStart('-').TrimEnd('0').Length > MaxReciprocalDigits) return false;
            reciprocal = candidate;
            return true;
        }

        private static System.Numerics.BigInteger Mantissa(decimal value)
        {
            var bits = decimal.GetBits(value);
            var bytes = new byte[13];
            for (var i = 0; i < 3; i++)
                BitConverter.GetBytes(bits[i]).CopyTo(bytes, i * 4);
            return new System.Numerics.BigInteger(bytes);
        }

        /// <summary>
        /// TASK-860 — refuses a conversion that is not exact in SQL: a narrowing or lossy numeric cast (<c>(int)x.Amount</c>
        /// truncates in C#, rounds or keeps the fraction in SQL), a conversion to or from <c>char</c> or <c>bool</c>, an
        /// unboxing. A nullable lift, an enum to its integer, a widening integer cast and a reference conversion are
        /// transparent; an integer to <c>decimal</c> or <c>double</c> is rendered as an explicit cast inside a value fragment
        /// (<see cref="SqlValueFragmentDialect.Conversion"/>), so no division of two integers happens where C# divides
        /// fractions. Before, every conversion was dropped.
        /// </summary>
        private static void RequireTranslatableConversion(Type from, Type to)
        {
            if (SqlConversion.Classify(from, to) != SqlConversionKind.Lossy) return;
            throw new NotSupportedException(
                $"Cannot translate the conversion from {Describe(from)} to {Describe(to)} over the row to SQL: SQL has no form "
                + "of it that gives C#'s result on every provider (C# truncates a fraction and wraps an integer that overflows; "
                + "a char or bool is no number in a column). Before this was refused the conversion was dropped and the "
                + "unconverted value compared. Compare the column itself, or convert the value you compare it with.");
        }

        private static string Describe(Type type)
            => Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

        /// <summary>
        /// TASK-860 — the operand of a comparison that reads the row must be one the parser translates: a column (through
        /// transparent conversions), a nullable's <c>.Value</c> / <c>.HasValue</c>, a column's <c>.Date</c>, a case fold
        /// (<c>ToLower()</c> / <c>ToUpper()</c> and their invariant forms), or a value fragment (arithmetic, <c>??</c>, a
        /// ternary, the <c>Math</c> calls TASK-857 translates). Anything else is refused by name. Before, the comparison
        /// parser recursed into it and kept whatever column it found: <c>x.Code.Substring(1) == "bc"</c> compared
        /// <c>Code</c>, <c>string.Compare(x.Code, "b") &gt; 0</c> compared <c>Code</c> with 0, <c>x.At.Year == 2026</c>
        /// failed with an unrelated error.
        /// </summary>
        private static void RequireTranslatableComparand(Expression operand, Type? exprType)
        {
            while (operand is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion)
            {
                RequireTranslatableConversion(conversion.Operand.Type, conversion.Type);
                operand = conversion.Operand;
            }
            if (!ContainsParameter(operand) || IsValueExpressionOperand(operand)) return;

            switch (operand)
            {
                case MemberExpression member when TryResolveParameterColumn(member, exprType, out _):
                    return;
                case MemberExpression { Member.Name: "Value" or "HasValue", Expression: { } inner } nullable
                        when nullable.Member.ReflectedType != null && Nullable.GetUnderlyingType(nullable.Member.ReflectedType) != null:
                    RequireTranslatableComparand(inner, exprType);
                    return;
                case MemberExpression { Member.Name: "Date", Expression: { } dated } date when date.Member.DeclaringType == typeof(DateTime):
                    RequireTranslatableComparand(dated, exprType);
                    return;
                case MemberExpression member:
                    throw RefuseMember(member);
                case MethodCallExpression call when IsCaseFold(call):
                    RequireTranslatableComparand(call.Object!, exprType);
                    return;
                case MethodCallExpression call:
                    throw RefuseCall(call);
                default:
                    throw RefuseNode(operand);
            }
        }

        /// <summary>
        /// TASK-860 — a call over the row in predicate position, or reached from one, is one the condition parser translates:
        /// a string <c>StartsWith</c> / <c>EndsWith</c> / <c>Contains</c> on a column with a value pattern (LIKE), a set
        /// <c>Contains</c> of a value list (IN), or a case fold. Everything else is refused by name: before, its arguments
        /// were parsed into the condition and its operation lost — <c>string.IsNullOrWhiteSpace(x.Name)</c> rendered
        /// <c>Name = NULL</c>, <c>"Abcdef".StartsWith(x.Code)</c> rendered <c>Code LIKE 'Abcdef%'</c> (the reverse question).
        /// </summary>
        /// <remarks>
        /// TASK-869 (the user's decision, option A): the no-argument LIKE family keeps following the column's collation, as
        /// <c>==</c> does. With a <see cref="StringComparison"/> argument, <c>Ordinal</c> and <c>OrdinalIgnoreCase</c> translate
        /// exactly (<see cref="SqlValueFragmentDialect.StringMatch"/>) and every other comparison is refused by name, as is
        /// the <c>(string, bool, CultureInfo)</c> overload; before, the argument was ignored. <c>string.Equals(row, value,
        /// comparison)</c> translates the same way.
        /// </remarks>
        private static void RequireTranslatableCall(MethodCallExpression call)
        {
            var method = call.Method;
            if (method.DeclaringType == typeof(string) && method.Name is "StartsWith" or "EndsWith" or "Contains")
            {
                if (call.Object != null && ContainsParameter(call.Object))
                {
                    TranslatableComparison(call);
                    return;
                }
                throw new NotSupportedException(
                    $"Cannot translate String.{method.Name} to SQL when the string searched is a value and the pattern a "
                    + "column: LIKE answers the reverse question. Compare against a value instead.");
            }
            if (method.DeclaringType == typeof(string) && IsStringComparisonEquals(method) && method.IsStatic)
            {
                TranslatableComparison(call);
                if (!ContainsParameter(call.Arguments[1])) return;
                throw new NotSupportedException(
                    "Cannot translate String.Equals with StringComparison.OrdinalIgnoreCase to SQL when both strings are read from "
                    + "the row: only a column compared with a value is translated. StringComparison.Ordinal (C#'s ==) translates "
                    + "between two columns. Compare in memory.");
            }
            if (method.Name == "Contains")
            {
                // A value list's Contains: List<T>.Contains(x.Col), Enumerable / MemoryExtensions.Contains(list, x.Col).
                var source = method.IsStatic ? call.Arguments.FirstOrDefault() : call.Object;
                if (source != null && !ContainsParameter(source)) return;
                throw new NotSupportedException(
                    $"Cannot translate {method.DeclaringType?.Name}.Contains over a collection read from the row to SQL: only a "
                    + "list of values searched for a column (IN) is translated.");
            }
            if (IsCaseFold(call)) return;
            throw RefuseCall(call);
        }

        /// <summary>
        /// TASK-869 — a <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> with an explicit Ordinal or OrdinalIgnoreCase,
        /// already parsed into <paramref name="condition"/>: marks it for the provider's exact form. A null term is refused,
        /// as C# throws <see cref="ArgumentNullException"/> for it (the parser would otherwise have made it <c>IS NULL</c>).
        /// </summary>
        private static void WithComparison(Condition condition, StringComparison comparison, string method)
        {
            if (condition.Type is not (ConditionType.Like or ConditionType.StartsWith or ConditionType.EndsWith))
                throw new NotSupportedException(
                    $"Cannot translate String.{method} with a null value to SQL: C# throws ArgumentNullException for it.");
            condition.Comparison = comparison;
        }

        /// <summary>
        /// TASK-869 — <c>string.Equals(row, value, comparison)</c> (the shape <see cref="RewriteTranslatableCalls"/> leaves for
        /// every string <c>Equals</c> with a comparison but <see cref="StringComparison.Ordinal"/>, which it makes <c>==</c>,
        /// TASK-895) as a <see cref="ConditionType.TextEqual"/> condition, rendered by the provider's exact form. As in C#, a NULL column is not equal to a value (so a negation includes the NULL rows,
        /// TASK-859) and a null value is the column's <c>IS NULL</c>. The row's side must be a column or a case fold of one.
        /// </summary>
        private static Condition ParseStringEquals(MethodCallExpression call, Condition condition, Type? exprType)
        {
            var comparison = TranslatableComparison(call)
                ?? throw new InvalidOperationException("string.Equals(a, b, comparison) carries a comparison.");
            var column = call.Arguments[0];
            var inner = column is MethodCallExpression fold && IsCaseFold(fold) ? fold.Object! : column;
            if (UnwrapConvert(inner) is not MemberExpression member || !TryResolveParameterColumn(member, exprType, out _))
                throw new NotSupportedException(
                    "Cannot translate String.Equals with a StringComparison to SQL unless it compares a column (or its ToLower / "
                    + "ToUpper) with a value. Compare in memory.");

            condition.Type = ConditionType.TextEqual;
            condition.Comparison = comparison;
            ParseConditionExpression(call.Arguments[1], condition, exprType);
            ParseConditionExpression(column, condition, exprType);
            if (condition.Type != ConditionType.TextEqual)
            {
                condition.Comparison = null;
                return condition;
            }
            if (!string.IsNullOrEmpty(condition.Name) && OperandCanBeNull(inner, exprType))
            {
                condition.NullableOperands = new[] { NullableOperandOf(condition) };
                condition.NullOperandMatches = false;
            }
            return condition;
        }

        /// <summary><c>ToLower()</c>, <c>ToUpper()</c> and their invariant forms on a string: <c>LOWER</c> / <c>UPPER</c>.</summary>
        private static bool IsCaseFold(MethodCallExpression call)
            => call.Object != null && call.Method.DeclaringType == typeof(string) && call.Arguments.Count == 0
                && call.Method.Name is "ToLower" or "ToLowerInvariant" or "ToUpper" or "ToUpperInvariant";

        private static bool IsUpperFold(MethodCallExpression call) => call.Method.Name is "ToUpper" or "ToUpperInvariant";

        private static bool IsInvariantFold(MethodCallExpression call) => call.Method.Name is "ToLowerInvariant" or "ToUpperInvariant";

        /// <summary>
        /// TASK-870 — names <paramref name="condition"/> after the case fold <paramref name="fold"/> of the already parsed
        /// <paramref name="inner"/>: <c>LOWER(…)</c> / <c>UPPER(…)</c> in the portable form, and a re-render through the provider's
        /// <see cref="SqlValueFragmentDialect.CaseFold"/>, which on SQLite is C#'s own fold.
        /// </summary>
        private static void FoldConditionName(Condition condition, Condition inner, MethodCallExpression fold)
        {
            var upper = IsUpperFold(fold);
            var invariant = IsInvariantFold(fold);
            var innerName = inner.Name!;
            var innerFragment = inner.NameFragment;
            if (innerFragment == null)
            {
                // TASK-890: a fold of a plain column is recorded, so a persistent view can rename the column inside it.
                FoldColumn(condition, new CaseFoldOperand(innerName, upper, invariant));
                return;
            }
            condition.Name = SqlValueFragmentDialect.Portable.CaseFold(innerName, upper, invariant);
            condition.NameFragment = dialect => dialect.CaseFold(innerFragment(dialect), upper, invariant);
        }

        /// <summary>
        /// TASK-890 — names <paramref name="condition"/> after <paramref name="fold"/>, the case fold of a plain column: the
        /// portable <c>LOWER</c> / <c>UPPER</c>, the provider's <see cref="SqlValueFragmentDialect.CaseFold"/> re-render, and the
        /// fold itself (<see cref="Condition.CaseFold"/>). The one producer of a folded column's name, for the parser and for a
        /// persistent view that renames the column (<c>ResolveViewConditions</c>).
        /// </summary>
        internal static void FoldColumn(Condition condition, CaseFoldOperand fold)
        {
            condition.Name = FoldedName(fold);
            condition.NameFragment = dialect => dialect.CaseFold(fold.Column, fold.Upper, fold.Invariant);
            condition.CaseFold = fold;
        }

        /// <summary>TASK-890 — the portable name of <paramref name="fold"/>, which a condition carrying it is named.</summary>
        internal static string FoldedName(CaseFoldOperand fold)
            => SqlValueFragmentDialect.Portable.CaseFold(fold.Column, fold.Upper, fold.Invariant);

        /// <summary>
        /// TASK-890 — the operand of <paramref name="condition"/>'s <c>IS NULL</c> leg: the column of a case fold (a fold is
        /// NULL exactly when its column is, and a persistent view can rename a column, not a fold), else its name.
        /// </summary>
        private static NullableOperand NullableOperandOf(Condition condition)
            => new(condition.CaseFold != null && condition.Name == FoldedName(condition.CaseFold)
                ? condition.CaseFold.Column
                : condition.Name!);

        /// <summary>
        /// TASK-870 — <c>string.Replace(oldValue, newValue)</c> in an update's SET value, matched ordinally as C# does
        /// (<see cref="SqlValueFragmentDialect.Replace"/>). C# throws for a null or empty <c>oldValue</c>, so one is refused by
        /// name, as is one read from the row, which could be empty on some row; a null <c>newValue</c> removes, as in C#.
        /// Before (measured on all four providers): an empty <c>oldValue</c> stored the text unchanged, and
        /// <c>Replace("A", null)</c> stored NULL where C# removes the <c>A</c>.
        /// </summary>
        private static string ParseReplace(MethodCallExpression call, IDictionary<string, object> parameters, bool withTableName,
            Type? exprType, SqlValueFragmentDialect dialect)
        {
            var oldArgument = call.Arguments[0];
            var newArgument = call.Arguments[1];
            if (ContainsParameter(oldArgument))
                throw new NotSupportedException(
                    "Cannot translate String.Replace to SQL when the value to replace is read from the row: C# throws for an "
                    + "empty one, which SQL cannot. Replace a value, or compute the new text in memory.");
            var oldValue = Expression.Lambda(oldArgument).Compile().DynamicInvoke()?.ToString();
            if (string.IsNullOrEmpty(oldValue))
                throw new NotSupportedException(
                    "Cannot translate String.Replace to SQL with a null or empty value to replace: C# throws for it "
                    + "(ArgumentNullException / ArgumentException). Before this was refused SQL stored the text unchanged.");

            var operand = ParseExpression(call.Object!, parameters, withTableName, exprType, dialect)!;
            var oldKey = Bind(parameters, oldValue);
            string newSql;
            if (ContainsParameter(newArgument))
            {
                // A NULL replacement removes in C#; SQL's REPLACE would answer NULL.
                newSql = $"COALESCE({ParseExpression(newArgument, parameters, withTableName, exprType, dialect)}, {Bind(parameters, string.Empty)})";
            }
            else
            {
                newSql = Bind(parameters, Expression.Lambda(newArgument).Compile().DynamicInvoke()?.ToString() ?? string.Empty);
            }
            return dialect.Replace(operand, oldKey, newSql);
        }

        private static string Bind(IDictionary<string, object> parameters, object value)
        {
            var key = "@Const" + parameters.Count;
            parameters.Add(key, value);
            return key;
        }

        /// <summary><c>string.Equals(string, string, StringComparison)</c>, the shape TASK-869's rewrite leaves.</summary>
        private static readonly MethodInfo StaticStringEqualsWithComparison =
            typeof(string).GetMethod(nameof(string.Equals), new[] { typeof(string), typeof(string), typeof(StringComparison) })!;

        /// <summary><c>string.Equals(a, b, comparison)</c> or <c>a.Equals(b, comparison)</c>.</summary>
        private static bool IsStringComparisonEquals(MethodInfo method)
        {
            if (method.Name != nameof(string.Equals) || method.DeclaringType != typeof(string)) return false;
            var parameters = method.GetParameters();
            return method.IsStatic
                ? parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType == typeof(StringComparison)
                : parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(StringComparison);
        }

        /// <summary>
        /// TASK-869 — the <see cref="StringComparison"/> a string call over the row compares with: null for an overload
        /// without one (the collation-following LIKE / <c>=</c>), <see cref="StringComparison.Ordinal"/> or
        /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which translate exactly. Anything else is refused by name before
        /// the statement runs: a culture comparison (<c>CurrentCulture*</c>, <c>InvariantCulture*</c>) uses .NET's culture
        /// data, which no SQL collation reproduces, and so does the <c>(string, bool ignoreCase, CultureInfo)</c> overload.
        /// Before, the argument was ignored and the column's collation decided.
        /// </summary>
        internal static StringComparison? TranslatableComparison(MethodCallExpression call)
        {
            StringComparison? comparison = null;
            var parameters = call.Method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = parameters[i].ParameterType;
                if (type == typeof(bool) || type == typeof(System.Globalization.CultureInfo))
                    throw new NotSupportedException(
                        $"Cannot translate String.{call.Method.Name} with an ignoreCase flag and a CultureInfo to SQL: a culture "
                        + "comparison has no SQL form that matches .NET's culture data. Use StringComparison.Ordinal or "
                        + "StringComparison.OrdinalIgnoreCase.");
                if (type != typeof(StringComparison)) continue;
                var argument = call.Arguments[i];
                if (ContainsParameter(argument))
                    throw new NotSupportedException(
                        $"Cannot translate String.{call.Method.Name} to SQL when its StringComparison is read from the row.");
                var value = argument is ConstantExpression constant
                    ? constant.Value
                    : Expression.Lambda(argument).Compile().DynamicInvoke();
                comparison = value is StringComparison known ? known : throw new NotSupportedException(
                    $"Cannot translate String.{call.Method.Name} to SQL: its StringComparison is null.");
            }
            if (comparison is null or StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase) return comparison;
            throw new NotSupportedException(
                $"Cannot translate String.{call.Method.Name} with StringComparison.{comparison} to SQL: only Ordinal and "
                + "OrdinalIgnoreCase are translated, exactly, on every provider. A culture comparison uses .NET's culture data, "
                + "which no SQL collation reproduces. Before this was refused the comparison was ignored and the column's "
                + "collation decided. Use StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase, or the overload "
                + "without a comparison, which follows the column's collation.");
        }

        private static NotSupportedException RefuseCall(MethodCallExpression call)
            => new($"Cannot translate {call.Method.DeclaringType?.Name}.{call.Method.Name} over the row to SQL: it is not one of "
                + "the calls the SQL filter parser translates exactly (StartsWith, EndsWith, Contains, string.IsNullOrEmpty, "
                + "ToLower/ToUpper without a culture, Equals(T), Equals with StringComparison.Ordinal / OrdinalIgnoreCase, "
                + "GetValueOrDefault, and TASK-857's Math calls). Before this was "
                + "refused the call was dropped and its argument compared instead. Compute the value in memory, or compare "
                + "the column itself.");

        private static NotSupportedException RefuseMember(MemberExpression member)
            => new($"Cannot translate the member {member.Member.DeclaringType?.Name}.{member.Member.Name} over the row to SQL: "
                + "it is neither a mapped column of the entity nor a member the SQL filter parser translates (a nullable's "
                + "Value / HasValue, a DateTime column's Date). Compare the column itself.");

        private static NotSupportedException RefuseNode(Expression node)
        {
            var name = node.NodeType == ExpressionType.Not && node.Type != typeof(bool) && node.Type != typeof(bool?)
                ? "bitwise complement (~)"
                : node is BinaryExpression { Method: { } method }
                    ? $"{node.NodeType} ({method.DeclaringType?.Name}.{method.Name})"
                    : node.NodeType.ToString();
            // The node's type only, never its text: a rendered tree carries the values a closure captured.
            return new($"Cannot translate the {name} operator ({node.GetType().Name}) over the row to SQL: the SQL parser has "
                + "no exact translation for it on every provider. Before this was refused it was dropped or misread. "
                + "Compute the value in memory, or compare the column itself.");
        }
    }
}
