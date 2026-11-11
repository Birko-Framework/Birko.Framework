using System;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-860 — MySQL's spelling of the parts of a value fragment whose portable form computes something else there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MySQL's <c>/</c> always divides fractions, so an integer division (C# truncates toward zero) is <c>DIV</c>, which
    /// truncates the same way. Measured before: <c>x.Hits / 4 == 0</c> matched 1 of 5 rows, and an update
    /// <c>SET Hits = Hits / 4</c> stored 1 where C# writes 0 (the quotient 0.75 was rounded into the INT column).
    /// </para>
    /// <para>A <c>double</c> cast is <c>CAST(x AS DOUBLE)</c>: MySQL's <c>CAST</c> does not take <c>DOUBLE PRECISION</c>.</para>
    /// </remarks>
    public sealed class MySqlValueFragmentDialect : SqlValueFragmentDialect
    {
        public MySqlValueFragmentDialect(Func<string, int, MidpointRounding, string> round) : base(round) { }

        /// <inheritdoc />
        public override string IntegralDivide(string left, string right) => $"({left} DIV {right})";

        /// <summary>
        /// TASK-890 — integer arithmetic computed in a C# type narrower than 64 bits is range-checked by
        /// <see cref="CheckedInteger"/>. MySQL computes every integer expression in <c>BIGINT</c>, so an <c>int</c> result
        /// past its range answered silently (measured on 8.4: <c>x.Hits * 2 / 2 == x.Hits</c> matched the
        /// <see cref="int.MaxValue"/> row and <c>SET Hits = Hits * 2 / 2</c> stored it unchanged, where C# wraps to -1, and
        /// PostgreSQL, SQL Server and SQLite fail the statement). A <c>long</c> overflows <c>BIGINT</c> itself, which MySQL fails
        /// with error 1690.
        /// </summary>
        public override string Arithmetic(System.Linq.Expressions.ExpressionType op, string sqlOperator, string left, string right, Type type)
            => CheckedInteger(base.Arithmetic(op, sqlOperator, left, right, type), type);

        /// <summary>TASK-890 — as <see cref="Arithmetic"/>: <c>-x.Hits</c> at <see cref="int.MinValue"/> answered 2147483648.</summary>
        public override string Negate(string operand, Type type) => CheckedInteger(base.Negate(operand, type), type);

        /// <summary>TASK-890 — as <see cref="Arithmetic"/>: <c>Math.Abs(x.Hits)</c> at <see cref="int.MinValue"/> answered 2147483648.</summary>
        public override string Abs(string operand, Type type) => CheckedInteger($"ABS({operand})", type);

        /// <summary>TASK-890 — nothing: every integer is computed in <c>BIGINT</c> here already.</summary>
        protected override string IntegerWidening(string operand, Type from, Type to) => operand;

        /// <summary>
        /// TASK-890 — <paramref name="value"/>, failing the statement when it leaves the range of the signed C# integer
        /// <paramref name="type"/> (<c>int</c>, <c>short</c>, <c>sbyte</c>): <c>((value * 2^(64-n)) DIV 2^(64-n))</c>. The product
        /// fits a signed <c>BIGINT</c> exactly when the value fits n bits, and otherwise MySQL fails it with error 1690 ("BIGINT value
        /// is out of range in …"), in a read and a write alike, as it fails a <c>long</c> overflow; the division gives the value
        /// back. The value is referenced once, so nested arithmetic does not grow the statement (a <c>CASE</c> would repeat it at
        /// every level). NULL stays NULL. Other types are returned unchanged: a <c>long</c> overflows <c>BIGINT</c> itself, and an
        /// unsigned type would need an offset an <c>UNSIGNED</c> column cannot take.
        /// </summary>
        private static string CheckedInteger(string value, Type type)
        {
            var bits = Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
            {
                TypeCode.Int32 => 32,
                TypeCode.Int16 => 16,
                TypeCode.SByte => 8,
                _ => 0,
            };
            if (bits == 0) return value;
            var scale = (1UL << (64 - bits)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"(({value} * {scale}) DIV {scale})";
        }

        /// <summary>The text <see cref="Divisor"/> raises; MySQL reports it as error 1411 ("Incorrect string value: '…'").</summary>
        public const string DivisionByZeroText = "Division by zero in a filter or SET value: C# throws DivideByZeroException";

        /// <summary>
        /// TASK-888 — a zero divisor fails the statement. MySQL divides by zero into NULL in a <c>SELECT</c> whatever its
        /// <c>sql_mode</c> (measured on 8.4: <c>x.Hits / x.Div == 2</c> read no row and counted none for a zero <c>Div</c>), and
        /// fails a data-change statement (1365) only under <c>ERROR_FOR_DIVISION_BY_ZERO</c> with strict mode. <c>SIGNAL</c> is
        /// not an expression, so the divisor is <c>CASE WHEN d = 0 THEN LENGTH(UUID_TO_BIN('…')) ELSE d END</c>: the branch is
        /// evaluated only for a zero divisor (measured: rows with a non-zero divisor, and an empty table with a constant zero,
        /// answer as before) and raises error 1411 naming <see cref="DivisionByZeroText"/>, in a read and a write alike.
        /// <c>LENGTH</c> keeps the branch an integer, so the CASE keeps the divisor's numeric type (a string branch would turn a
        /// <c>decimal</c> divisor into a double). A NULL divisor is not <c>= 0</c> and stays NULL.
        /// </summary>
        public override string Divisor(string divisor, Type type)
            => $"(CASE WHEN {divisor} = 0 THEN LENGTH(UUID_TO_BIN('{DivisionByZeroText}')) ELSE {divisor} END)";

        /// <inheritdoc />
        protected override string FloatingTypeName(Type type) => type == typeof(float) ? "FLOAT" : "DOUBLE";

        /// <summary>
        /// TASK-869 — <see cref="StringComparison.Ordinal"/> is <c>LIKE</c> under <c>COLLATE utf8mb4_bin</c> (the default
        /// <c>utf8mb4_0900_ai_ci</c> ignores both case and accents: <c>Contains("cafe")</c> matched <c>CAFÉ</c>); measured 0 of
        /// 240 cases off. <see cref="StringComparison.OrdinalIgnoreCase"/> is <c>REGEXP_LIKE(…, 'c')</c> over C#'s case classes
        /// (<see cref="SqlStringMatch.RegexCaseClassPattern"/>, anchored with ICU's <c>\A</c> / <c>\z</c>): 0 of 240 off, where
        /// <c>UPPER(col) LIKE UPPER(term)</c> was 24 off and an accent-sensitive <c>_as_ci</c> collation 17.
        /// </summary>
        public override string StringMatch(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
            => comparison switch
            {
                StringComparison.Ordinal => CollatedLike(operand, "utf8mb4_bin", bind(SqlStringMatch.LikePattern(kind, term))),
                StringComparison.OrdinalIgnoreCase => $"REGEXP_LIKE({operand}, {bind(SqlStringMatch.RegexCaseClassPattern(kind, term, "\\A", "\\z"))}, 'c')",
                _ => base.StringMatch(kind, operand, term, comparison, bind),
            };

        /// <summary>
        /// TASK-873 — <c>CONVERT(operand USING utf8mb4) COLLATE utf8mb4_0900_bin</c>: code-point order and NO PAD, so neither
        /// case, accents, <c>ß</c> / <c>ss</c>, <c>K</c> (Kelvin) / <c>K</c> nor a no-break space are folded, and trailing spaces
        /// count. Not <c>utf8mb4_bin</c>, the collation TASK-869's LIKE uses: it is PAD SPACE, so <c>'abc ' = 'abc'</c> under it
        /// (measured), which LIKE does not do. The <c>CONVERT</c> makes it valid on a column of another character set (measured:
        /// a bare <c>COLLATE utf8mb4_0900_bin</c> on a <c>utf8mb3</c> column fails with error 1253); MySQL 8.0 or later. An
        /// explicit collation on one operand decides the comparison, so a bound value is left as it is.
        /// </summary>
        protected override string? OrdinalText(string operand, bool bothFromRow)
            => $"CONVERT({operand} USING utf8mb4) COLLATE utf8mb4_0900_bin";

        /// <summary>
        /// TASK-907 — the extreme of the value's UTF-16 bytes, read back as text: <c>CAST(CONVERT(operand USING utf16) AS
        /// BINARY)</c> is big-endian UTF-16, and a binary string compares byte by byte with no padding, which is UTF-16 code
        /// unit order, C#'s Ordinal. Measured: the default collation answered <c>"apple"</c> below <c>"Banana"</c>;
        /// <c>utf8mb4_0900_bin</c> (code-point order) answered U+FF5E below U+1F600, where UTF-16 puts the surrogate pair first;
        /// <c>utf8mb4_bin</c> is PAD SPACE and ties <c>"a"</c> with <c>"a "</c>.
        /// </summary>
        protected override string? OrdinalStringExtreme(string function, string operand)
            => $"CONVERT(CONVERT({function}({OrdinalOrderKey(operand)}) USING utf16) USING utf8mb4)";

        /// <summary>
        /// TASK-909 — the Ordinal key <see cref="OrdinalStringExtreme"/> takes its extreme of: the value's big-endian UTF-16
        /// bytes as a binary string, compared byte by byte with no padding, so a row order and a MIN / MAX agree.
        /// </summary>
        protected override string? OrdinalOrderKey(string operand) => $"CAST(CONVERT({operand} USING utf16) AS BINARY)";
    }
}
