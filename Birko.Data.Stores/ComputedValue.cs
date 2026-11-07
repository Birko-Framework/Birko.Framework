using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Stores
{
    /// <summary>
    /// The value of a <see cref="ComputedAssignment"/>: a <c>decimal</c> formula over the same row's columns (TASK-826).
    /// A closed set of nodes, reached only through <see cref="Match{TResult}"/>, so a provider that translates it must
    /// name every node, and a new node breaks every translator at compile time (as <see cref="PropertyAssignment"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only what every provider can compute exactly: a <c>decimal</c> column, a <c>decimal</c> constant, <c>+ - * /</c>,
    /// and <c>Math.Round</c> with an explicit <see cref="MidpointRounding"/> of <see cref="MidpointRounding.ToEven"/> or
    /// <see cref="MidpointRounding.AwayFromZero"/>. The mode must be explicit because the defaults disagree: C# and
    /// MongoDB round half to even, SQL Server, PostgreSQL and MySQL half away from zero, so an implicit mode would store
    /// different cents on different backends.
    /// </para>
    /// <para>
    /// <see cref="Evaluate"/> is the reference: what C# computes on the same row. Every native translation is held to
    /// it by the cross-provider contract.
    /// </para>
    /// </remarks>
    public abstract class ComputedValue
    {
        private protected ComputedValue() { }

        /// <summary>Dispatches on the node kind.</summary>
        public abstract TResult Match<TResult>(
            Func<ComputedColumn, TResult> column,
            Func<ComputedConstant, TResult> constant,
            Func<ComputedBinary, TResult> binary,
            Func<ComputedRound, TResult> round);

        /// <summary>The value C# computes for <paramref name="entity"/>: the reference every provider must match.</summary>
        public decimal Evaluate(object entity) => Match(
            column => (decimal)column.Property.GetValue(entity)!,
            constant => constant.Value,
            binary =>
            {
                var left = binary.Left.Evaluate(entity);
                var right = binary.Right.Evaluate(entity);
                return binary.Operator switch
                {
                    ComputedOperator.Add => left + right,
                    ComputedOperator.Subtract => left - right,
                    ComputedOperator.Multiply => left * right,
                    ComputedOperator.Divide => left / right,
                    _ => throw new InvalidOperationException($"Unknown operator {binary.Operator}."),
                };
            },
            round => Math.Round(round.Operand.Evaluate(entity), round.Digits, round.Mode));

        /// <summary>Every column the formula reads.</summary>
        public System.Collections.Generic.IEnumerable<PropertyInfo> Columns() => Match(
            column => new[] { column.Property },
            constant => Enumerable.Empty<PropertyInfo>(),
            binary => binary.Left.Columns().Concat(binary.Right.Columns()),
            round => round.Operand.Columns());

        /// <summary>
        /// Translates a lambda body into a formula, or refuses it. A subtree that does not read the row (a constant, a
        /// captured variable, <c>1 + rate</c>) is evaluated here, once, to a <c>decimal</c> constant.
        /// </summary>
        /// <exception cref="ArgumentException">The expression uses anything outside the supported set.</exception>
        internal static ComputedValue From(LambdaExpression lambda)
        {
            var parameter = lambda.Parameters[0];
            return Build(lambda.Body, parameter, lambda);
        }

        private static ComputedValue Build(Expression node, ParameterExpression parameter, LambdaExpression whole)
        {
            if (!ReadsParameter(node, parameter))
            {
                return new ComputedConstant(EvaluateConstant(node, whole));
            }

            switch (node)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
                    when convert.Type == typeof(decimal) && convert.Operand.Type == typeof(decimal):
                    return Build(convert.Operand, parameter, whole);

                case MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression }:
                    if (property.PropertyType != typeof(decimal))
                    {
                        throw Refuse(whole, $"'{property.Name}' is a {Describe(property.PropertyType)}; a computed set reads "
                            + "only non-nullable decimal columns (NULL arithmetic differs between providers).");
                    }
                    return new ComputedColumn(property);

                case MemberExpression member:
                    throw Refuse(whole, $"'{member}' is not a top-level property of the row.");

                case UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } negate
                    when negate.Type == typeof(decimal):
                    return new ComputedBinary(ComputedOperator.Subtract, new ComputedConstant(0m), Build(negate.Operand, parameter, whole));

                case BinaryExpression binary when binary.Type == typeof(decimal) && Operator(binary.NodeType) is { } op:
                    return new ComputedBinary(op, Build(binary.Left, parameter, whole), Build(binary.Right, parameter, whole));

                case MethodCallExpression call when IsRound(call.Method):
                    return BuildRound(call, parameter, whole);

                default:
                    throw Refuse(whole, $"'{node}' ({node.NodeType}) is not supported.");
            }
        }

        private static ComputedValue BuildRound(MethodCallExpression call, ParameterExpression parameter, LambdaExpression whole)
        {
            var refusal = ReadRound(call, node => ReadsParameter(node, parameter), out var digits, out var mode);
            if (refusal != null) throw Refuse(whole, refusal);
            return new ComputedRound(Build(call.Arguments[0], parameter, whole), digits, mode);
        }

        /// <summary>
        /// Reads the digits and the mode of a <c>decimal</c> <c>Math.Round</c> / <c>decimal.Round</c> call, or says why it cannot
        /// be translated exactly: no explicit <see cref="MidpointRounding"/>, a mode other than <see cref="MidpointRounding.ToEven"/>
        /// or <see cref="MidpointRounding.AwayFromZero"/>, digits outside 0 to 28, or digits or mode that read the row. The one
        /// rule for a computed set and for a SQL filter (TASK-857).
        /// </summary>
        /// <param name="call">A call for which <see cref="IsDecimalRound"/> is true.</param>
        /// <param name="readsRow">True for a subtree that reads the row.</param>
        /// <returns>Null when the call translates, otherwise the reason it does not.</returns>
        public static string? ReadRound(MethodCallExpression call, Func<Expression, bool> readsRow, out int digits, out MidpointRounding mode)
        {
            digits = 0;
            mode = MidpointRounding.ToEven;
            var parameters = call.Method.GetParameters();
            if (parameters.Length == 0 || parameters[^1].ParameterType != typeof(MidpointRounding))
            {
                return $"'{call}' does not name its MidpointRounding. C# and MongoDB round half to even by default, "
                    + "SQL half away from zero, so the mode must be explicit: Math.Round(value, digits, MidpointRounding.ToEven) "
                    + "or MidpointRounding.AwayFromZero.";
            }

            if (parameters.Length == 3)
            {
                if (readsRow(call.Arguments[1]))
                    return $"the digits of '{call}' must not depend on the row.";
                digits = (int)Expression.Lambda(Expression.Convert(call.Arguments[1], typeof(object))).Compile().DynamicInvoke()!;
            }
            if (digits is < 0 or > 28)
                return $"'{call}' rounds to {digits} digits; 0 to 28 are supported.";

            var modeArgument = call.Arguments[^1];
            if (readsRow(modeArgument))
                return $"the rounding mode of '{call}' must not depend on the row.";
            mode = (MidpointRounding)Expression.Lambda(Expression.Convert(modeArgument, typeof(object))).Compile().DynamicInvoke()!;
            if (mode is not (MidpointRounding.ToEven or MidpointRounding.AwayFromZero))
                return $"'{call}' rounds {mode}; only MidpointRounding.ToEven and AwayFromZero are supported.";
            return null;
        }

        /// <summary><c>Math.Round</c> or <c>decimal.Round</c> over a <c>decimal</c>.</summary>
        public static bool IsDecimalRound(MethodInfo method) => IsRound(method);

        private static bool IsRound(MethodInfo method)
            => method.Name == nameof(Math.Round)
               && (method.DeclaringType == typeof(Math) || method.DeclaringType == typeof(decimal))
               && method.ReturnType == typeof(decimal);

        private static ComputedOperator? Operator(ExpressionType type) => type switch
        {
            ExpressionType.Add or ExpressionType.AddChecked => ComputedOperator.Add,
            ExpressionType.Subtract or ExpressionType.SubtractChecked => ComputedOperator.Subtract,
            ExpressionType.Multiply or ExpressionType.MultiplyChecked => ComputedOperator.Multiply,
            ExpressionType.Divide => ComputedOperator.Divide,
            _ => null,
        };

        private static decimal EvaluateConstant(Expression node, LambdaExpression whole)
        {
            object? value;
            try
            {
                value = Expression.Lambda(Expression.Convert(node, typeof(object))).Compile().DynamicInvoke();
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Cannot compute '{node}' in '{whole}': {e.InnerException?.Message ?? e.Message}", e);
            }

            return value switch
            {
                decimal d => d,
                null => throw Refuse(whole, $"'{node}' is null."),
                _ => throw Refuse(whole, $"'{node}' is a {value.GetType().Name}; convert it to decimal explicitly."),
            };
        }

        private static bool ReadsParameter(Expression node, ParameterExpression parameter)
        {
            var finder = new ParameterFinder(parameter);
            finder.Visit(node);
            return finder.Found;
        }

        private static string Describe(Type type)
            => Nullable.GetUnderlyingType(type) is { } inner ? inner.Name + "?" : type.Name;

        private static ArgumentException Refuse(LambdaExpression whole, string reason)
            => new($"Cannot compute '{whole}': {reason}", "value");

        private sealed class ParameterFinder : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;
            public ParameterFinder(ParameterExpression parameter) => _parameter = parameter;
            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _parameter) Found = true;
                return node;
            }
        }
    }

    /// <summary>The arithmetic a <see cref="ComputedBinary"/> applies.</summary>
    public enum ComputedOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
    }

    /// <summary>A <c>decimal</c> column of the same row, as it was before the update.</summary>
    public sealed class ComputedColumn : ComputedValue
    {
        internal ComputedColumn(PropertyInfo property) => Property = property;

        /// <summary>The property the column is mapped from.</summary>
        public PropertyInfo Property { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<ComputedColumn, TResult> column, Func<ComputedConstant, TResult> constant,
            Func<ComputedBinary, TResult> binary, Func<ComputedRound, TResult> round) => column(this);
    }

    /// <summary>A <c>decimal</c> constant, already evaluated.</summary>
    public sealed class ComputedConstant : ComputedValue
    {
        internal ComputedConstant(decimal value) => Value = value;

        /// <summary>The value.</summary>
        public decimal Value { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<ComputedColumn, TResult> column, Func<ComputedConstant, TResult> constant,
            Func<ComputedBinary, TResult> binary, Func<ComputedRound, TResult> round) => constant(this);
    }

    /// <summary><c>left op right</c> in <c>decimal</c> arithmetic.</summary>
    public sealed class ComputedBinary : ComputedValue
    {
        internal ComputedBinary(ComputedOperator op, ComputedValue left, ComputedValue right)
        {
            Operator = op;
            Left = left;
            Right = right;
        }

        /// <summary>The operation.</summary>
        public ComputedOperator Operator { get; }

        /// <summary>The left operand.</summary>
        public ComputedValue Left { get; }

        /// <summary>The right operand.</summary>
        public ComputedValue Right { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<ComputedColumn, TResult> column, Func<ComputedConstant, TResult> constant,
            Func<ComputedBinary, TResult> binary, Func<ComputedRound, TResult> round) => binary(this);
    }

    /// <summary><c>Math.Round(operand, digits, mode)</c>, with the mode always explicit.</summary>
    public sealed class ComputedRound : ComputedValue
    {
        internal ComputedRound(ComputedValue operand, int digits, MidpointRounding mode)
        {
            Operand = operand;
            Digits = digits;
            Mode = mode;
        }

        /// <summary>The value rounded.</summary>
        public ComputedValue Operand { get; }

        /// <summary>Decimal places kept, 0 to 28.</summary>
        public int Digits { get; }

        /// <summary><see cref="MidpointRounding.ToEven"/> or <see cref="MidpointRounding.AwayFromZero"/>.</summary>
        public MidpointRounding Mode { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<ComputedColumn, TResult> column, Func<ComputedConstant, TResult> constant,
            Func<ComputedBinary, TResult> binary, Func<ComputedRound, TResult> round) => round(this);
    }
}
