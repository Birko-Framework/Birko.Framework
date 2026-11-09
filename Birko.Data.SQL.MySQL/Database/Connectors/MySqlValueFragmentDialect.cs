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
    }
}
