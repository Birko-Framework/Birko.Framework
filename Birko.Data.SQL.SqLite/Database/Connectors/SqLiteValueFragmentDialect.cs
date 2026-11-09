using System;
using System.Globalization;
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
            if (SqlConversion.IsIntegral(type))
                return SqLiteExactFunctions.CheckedIntegerCall(base.Arithmetic(op, sqlOperator, left, right, type), type);
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

        /// <summary>
        /// TASK-888 — through <see cref="SqLiteExactFunctions.DivisorFunction"/>, which fails a zero divisor: SQLite's
        /// <c>/</c> and <c>%</c> answer NULL. A <c>decimal</c> never comes here: its functions compute in C#'s decimal, which
        /// throws on a zero divisor already.
        /// </summary>
        public override string Divisor(string divisor, Type type) => SqLiteExactFunctions.DivisorCall(divisor);

        /// <inheritdoc />
        public override string? Literal(object value)
            => value is decimal d ? "'" + SqLiteDecimal.Format(d) + "'" : null;

        /// <inheritdoc />
        public override string Conversion(string operand, Type from, Type to)
        {
            var fromDecimal = IsDecimal(from);
            var toDecimal = IsDecimal(to);
            // TASK-860: between two non-decimal types the portable rules apply, so an integer becoming a double is an
            // explicit cast here too; without it SQLite divided two integers (measured: x.Hits / 4.0 == 0.75 matched nothing).
            if (!fromDecimal && !toDecimal) return base.Conversion(operand, from, to);
            if (fromDecimal && toDecimal) return operand;
            if (fromDecimal)
                throw new NotSupportedException(
                    $"Cannot convert a decimal value fragment ({operand}) to {(Nullable.GetUnderlyingType(to) ?? to).Name} "
                    + "in a SQLite filter: the conversion has no SQL form there. Compare it as a decimal.");
            // An integer becoming a decimal: through C#'s own conversion, to the decimal TEXT form. Nothing else is exact.
            if (SqlConversion.Classify(from, to) == SqlConversionKind.Lossy)
                return base.Conversion(operand, from, to);
            return $"{SqLiteDecimal.AddFunction}({operand}, '0.0')";
        }

        /// <inheritdoc />
        protected override string FloatingTypeName(Type type) => "REAL";

        /// <summary>
        /// TASK-860 — measured: <c>SET Sh = (short)(Sh + 1)</c> on a <c>short</c> column holding 32767 stored 32768 with no
        /// error (an INTEGER is 64-bit here, whatever the column declares); the row then failed every read with
        /// <see cref="OverflowException"/>, and a filter saw the out-of-range value. C# writes -32768.
        /// </summary>
        public override bool StoresIntegerOverflowLoudly => false;

        /// <inheritdoc />
        public override string Abs(string operand, Type type)
            => IsDecimal(type) ? $"{SqLiteDecimal.AbsFunction}({operand})" : base.Abs(operand, type);

        /// <inheritdoc />
        public override string Negate(string operand, Type type)
            => IsDecimal(type)
                ? $"{SqLiteDecimal.SubtractFunction}({Literal(0m)}, {operand})"
                : SqlConversion.IsIntegral(type)
                    ? SqLiteExactFunctions.CheckedIntegerCall(base.Negate(operand, type), type)
                    : base.Negate(operand, type);

        /// <summary>
        /// TASK-870 — C#'s own fold through <see cref="SqLiteExactFunctions.CaseFoldFunction"/>: SQLite's <c>LOWER</c> / <c>UPPER</c>
        /// fold ASCII only. <c>ToLower()</c> folds in the current culture, read here, when the statement is rendered.
        /// </summary>
        public override string CaseFold(string operand, bool upper, bool invariant)
            => SqLiteExactFunctions.CaseFoldCall(operand, upper, invariant ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture);

        /// <summary>
        /// TASK-870 — midnight as the text a DateTime parameter binds as (<c>yyyy-MM-dd HH:mm:ss</c>). Measured: <c>DATE(…)</c>
        /// stored <c>2026-03-09</c>, which read back as the right DateTime, but compared as text with the bound
        /// <c>2026-03-09 00:00:00</c>, so <c>x.At == day</c> matched none of the updated rows.
        /// </summary>
        public override string DateOf(string operand) => $"(DATE({operand}) || ' 00:00:00')";

        /// <summary>TASK-869 — C#'s own comparison, through <see cref="SqLiteStringMatch.Function"/>.</summary>
        public override string StringMatch(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
            => SqLiteStringMatch.Call(kind, operand, term, comparison, bind);

        /// <inheritdoc />
        public override string Compared(string operand, Type comparedType)
            => IsDecimal(comparedType) ? $"{operand} COLLATE {SqLiteDecimal.Collation}" : operand;
    }
}
