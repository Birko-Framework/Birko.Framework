using System;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-856 — a <c>decimal</c> value fragment in a filter, computed and compared the way the column is stored:
    /// as TEXT through <see cref="SqLiteDecimal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The portable fragment answered wrongly with no error (measured): <c>Amount + 0</c> is a REAL, which SQLite
    /// orders before every TEXT, so <c>small.Contains(x.Amount + 0m)</c> and <c>x.Amount + 0m == 10.10m</c> matched
    /// nothing, <c>&lt;</c> matched every row and <c>&gt;</c> none; <c>COALESCE</c> / <c>CASE</c> kept the TEXT but
    /// compared it byte-wise (<c>'10.1' &lt; '8.0'</c>), and an inlined <c>0</c> is an INTEGER no decimal equals.
    /// </para>
    /// <para>
    /// Here each decimal step runs in C#'s decimal (the functions a computed set uses, TASK-826), a decimal constant
    /// is the TEXT a decimal parameter binds as, and a decimal comparison names the column's collation, so both sides
    /// compare as decimals. A conversion of a decimal fragment to another type is refused: C# truncates or rounds
    /// there, and the portable fragment drops the conversion.
    /// </para>
    /// <para>
    /// TASK-857: <c>Math.Round</c> is the computed set's <see cref="SqLiteDecimal.RoundCall"/>, a decimal <c>Math.Abs</c> is
    /// <see cref="SqLiteDecimal.AbsFunction"/> and a decimal unary minus is <c>0 - x</c> through
    /// <see cref="SqLiteDecimal.SubtractFunction"/>, as a computed set negates. SQLite's own <c>ABS</c> and <c>-</c> read
    /// the TEXT as a REAL.
    /// </para>
    /// </remarks>
    public sealed class SqLiteValueFragmentDialect : SqlValueFragmentDialect
    {
        public static readonly SqLiteValueFragmentDialect Instance = new();

        private SqLiteValueFragmentDialect() : base(SqLiteDecimal.RoundCall) { }

        private static bool IsDecimal(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(decimal);

        /// <inheritdoc />
        public override string Arithmetic(ExpressionType op, string sqlOperator, string left, string right, Type type)
        {
            if (!IsDecimal(type)) return base.Arithmetic(op, sqlOperator, left, right, type);
            var function = op switch
            {
                ExpressionType.Add or ExpressionType.AddChecked => SqLiteDecimal.AddFunction,
                ExpressionType.Subtract or ExpressionType.SubtractChecked => SqLiteDecimal.SubtractFunction,
                ExpressionType.Multiply or ExpressionType.MultiplyChecked => SqLiteDecimal.MultiplyFunction,
                ExpressionType.Divide => SqLiteDecimal.DivideFunction,
                ExpressionType.Modulo => SqLiteDecimal.ModuloFunction,
                _ => throw new NotSupportedException($"Decimal operator {op} is not supported on SQLite."),
            };
            return $"{function}({left}, {right})";
        }

        /// <inheritdoc />
        public override string? Literal(object value)
            => value is decimal d ? "'" + SqLiteDecimal.Format(d) + "'" : null;

        /// <inheritdoc />
        public override string Conversion(string operand, Type from, Type to)
        {
            var fromDecimal = IsDecimal(from);
            var toDecimal = IsDecimal(to);
            if (fromDecimal == toDecimal) return operand;
            if (fromDecimal)
                throw new NotSupportedException(
                    $"Cannot convert a decimal value fragment ({operand}) to {(Nullable.GetUnderlyingType(to) ?? to).Name} "
                    + "in a SQLite filter: the conversion has no SQL form there. Compare it as a decimal.");
            // A number becoming a decimal: through C#'s own conversion, to the decimal TEXT form.
            return $"{SqLiteDecimal.AddFunction}({operand}, '0.0')";
        }

        /// <inheritdoc />
        public override string Abs(string operand, Type type)
            => IsDecimal(type) ? $"{SqLiteDecimal.AbsFunction}({operand})" : base.Abs(operand, type);

        /// <inheritdoc />
        public override string Negate(string operand, Type type)
            => IsDecimal(type) ? $"{SqLiteDecimal.SubtractFunction}({Literal(0m)}, {operand})" : base.Negate(operand, type);

        /// <inheritdoc />
        public override string Compared(string operand, Type comparedType)
            => IsDecimal(comparedType) ? $"{operand} COLLATE {SqLiteDecimal.Collation}" : operand;
    }
}
