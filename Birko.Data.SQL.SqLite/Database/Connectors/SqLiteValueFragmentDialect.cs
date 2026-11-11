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
        /// TASK-890 — the narrowed value through <see cref="SqLiteExactFunctions.CheckedIntegerCall"/> with the target type's
        /// range, so an overflow fails the statement as the servers fail it. Measured before TASK-860 refused it: <c>SET Sh =
        /// (short)(Sh + 1)</c> on a <c>short</c> column holding 32767 stored 32768 with no error (an INTEGER is 64-bit here,
        /// whatever the column declares); the row then failed every read with <see cref="OverflowException"/>. From TASK-860
        /// to TASK-890 every narrowing SET was refused, the in-range ones too.
        /// </summary>
        public override string NarrowedInteger(string value, Type target) => SqLiteExactFunctions.CheckedIntegerCall(value, target);

        /// <summary>TASK-890 — nothing: an INTEGER is 64-bit here, and each integer result is checked against its C# type.</summary>
        protected override string IntegerWidening(string operand, Type from, Type to) => operand;

        /// <summary>
        /// A decimal through <see cref="SqLiteDecimal.AbsFunction"/>. TASK-890: an integer is range-checked, as C#'s
        /// <c>Math.Abs</c> throws for the type's minimum (measured: <c>Math.Abs(x.Hits) &gt; 0</c> matched the
        /// <see cref="int.MinValue"/> row, whose absolute value is 2147483648 in SQLite's 64 bits).
        /// </summary>
        public override string Abs(string operand, Type type)
            => IsDecimal(type)
                ? $"{SqLiteDecimal.AbsFunction}({operand})"
                : SqlConversion.IsIntegral(type)
                    ? SqLiteExactFunctions.CheckedIntegerCall($"ABS({operand})", type)
                    : base.Abs(operand, type);

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

        /// <summary>
        /// TASK-873 — <c>(operand) COLLATE BINARY</c>, which compares the stored bytes. A column the framework creates is
        /// already <c>BINARY</c> (measured: <c>==</c> matched what C# matches for all 134 shapes), so this guards a column
        /// declared <c>NOCASE</c> or <c>RTRIM</c> elsewhere (measured: <c>Nc = 'abc'</c> matched <c>ABC</c> on a <c>NOCASE</c>
        /// column, the two-term form only <c>abc</c>, still searching its index). An explicit collation on one operand decides
        /// the comparison, so a bound value is left as it is.
        /// </summary>
        protected override string? OrdinalText(string operand, bool bothFromRow) => $"({operand}) COLLATE BINARY";

        /// <summary>
        /// TASK-907 — the extreme under <see cref="SqLiteExactFunctions.OrdinalCollation"/>, C#'s own
        /// <see cref="string.CompareOrdinal(string, string)"/>. SQLite's <c>BINARY</c> compares UTF-8 bytes, which is code-point
        /// order (measured: U+FF5E below U+1F600, where UTF-16 puts the surrogate pair first).
        /// </summary>
        protected override string? OrdinalStringExtreme(string function, string operand)
            => $"{function}({OrdinalOrderKey(operand)})";

        /// <summary>
        /// TASK-909 — the Ordinal key <see cref="OrdinalStringExtreme"/> takes its extreme of: the value under
        /// <see cref="SqLiteExactFunctions.OrdinalCollation"/>, so a row order and a MIN / MAX agree.
        /// </summary>
        protected override string? OrdinalOrderKey(string operand) => $"({operand}) COLLATE {SqLiteExactFunctions.OrdinalCollation}";

        /// <summary>
        /// TASK-892 — a decimal SUM in C#'s decimal, through <see cref="SqLiteDecimal.SumAggregate"/>. SQLite's <c>SUM</c> reads
        /// the TEXT as a REAL (measured: 1234567890123.456789 + 0.000001 + 1 summed to 1234567890124.46).
        /// </summary>
        public override string Sum(string operand, Type source)
            => IsDecimal(source) ? $"{SqLiteDecimal.SumAggregate}({operand})" : base.Sum(operand, source);

        /// <summary>TASK-892 — the decimal 0 as the TEXT a decimal parameter binds as, so it orders among the other sums.</summary>
        protected override string ZeroSum(Type source) => IsDecimal(source) ? Literal(0m)! : base.ZeroSum(source);

        /// <summary>
        /// TASK-892 — a decimal average, or one read into a decimal, in C#'s decimal through
        /// <see cref="SqLiteDecimal.AvgAggregate"/>: the sum divided by the count, as LINQ's <c>Average</c> divides it.
        /// </summary>
        protected override string? Average(string operand, Type source, Type result)
            => IsDecimal(source) || IsDecimal(result) ? $"{SqLiteDecimal.AvgAggregate}({operand})" : base.Average(operand, source, result);

        /// <summary>
        /// TASK-892 — a decimal aggregate is TEXT, which SQLite orders byte-wise ('10.5' before '9.0'); the decimal collation
        /// orders it as a number, in an <c>ORDER BY</c> on the expression and on a persistent view's column alike.
        /// </summary>
        protected override string AggregateResult(string sql, Birko.Data.Stores.AggregateFunction function, Type source, Type result)
            => IsDecimal(source) || (function == Birko.Data.Stores.AggregateFunction.Avg && IsDecimal(result))
                ? $"{sql} COLLATE {SqLiteDecimal.Collation}"
                : sql;
    }
}
