using System;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Conditions
{
    /// <summary>
    /// TASK-892 — how a provider renders an aggregate, for the store's <c>Aggregate</c> and for a view alike: the one
    /// producer of an aggregate's SQL. What it renders is read back by <c>Fields.AggregateValue</c>, the one reading rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured before (SQLite, PostgreSQL, MySQL, SQL Server, against LINQ): SQL Server averaged an integral column in its
    /// own type (the AVG of 1, 1 and 2 was 1 in the store's <c>Aggregate</c>, where TASK-887 had fixed views only); SQLite
    /// summed and averaged a <c>decimal</c> column as a REAL (1234567890124.456790 read back as 1234567890124.46), although
    /// the exact <c>birko_decimal_sum</c> / <c>birko_decimal_avg</c> were registered on every connection; PostgreSQL
    /// averaged a <c>long</c> as a NUMERIC rounded to about 17 digits (1000000003.6666667 where C# says 1000000003.6666666);
    /// and every server averaged a <c>decimal</c> to its own scale (SQL Server 6 decimals, MySQL 4 more than the column,
    /// PostgreSQL 16 to 20 significant digits) where C#'s decimal division keeps 28 to 29.
    /// </para>
    /// <para>
    /// <see cref="Portable"/> is what a caller without a provider renders (a name map, a message, the migration
    /// generator): it never refuses. A provider's dialect refuses an average it has no exact single-column form of.
    /// </para>
    /// </remarks>
    public partial class SqlValueFragmentDialect
    {
        /// <summary>
        /// The SQL of <paramref name="function"/> over <paramref name="operand"/> (a qualified column, or null for
        /// <c>COUNT(*)</c>), whose column holds <paramref name="source"/> values and whose answer is read into
        /// <paramref name="result"/>.
        /// </summary>
        /// <remarks>
        /// A <c>SUM</c> over no values (no rows, or only NULLs) answers 0 rather than NULL, as LINQ's <c>Sum</c> over
        /// <c>int?</c> does: for a view since TASK-892, and for the store's <c>Aggregate</c> since TASK-886 (the user's
        /// decision: one answer on every provider), which used to keep NULL through a <c>sumOfNoValuesIsZero</c> flag.
        /// </remarks>
        /// <param name="function">The aggregate.</param>
        /// <param name="operand">The qualified column, or null for <c>COUNT(*)</c>.</param>
        /// <param name="source">The CLR type of the column's values.</param>
        /// <param name="result">The CLR type the answer is read into.</param>
        /// <param name="stringComparison">
        /// TASK-907: the order a <c>MIN</c> / <c>MAX</c> of a string column is taken in (<see cref="AggregateQuery{T}.StringComparison"/>;
        /// a view's is always <see cref="StringComparison.Ordinal"/>). Ordinal renders <see cref="OrdinalStringExtreme"/>; any
        /// other order is refused by name, since no provider takes a collation that is C#'s culture order.
        /// </param>
        /// <exception cref="NotSupportedException">
        /// An <c>AVG</c> this provider cannot compute as C# does (<see cref="Average"/> answered null), or a string <c>MIN</c> /
        /// <c>MAX</c> in an order other than Ordinal, on a provider's dialect.
        /// </exception>
        public string Aggregate(AggregateFunction function, string? operand, Type? source, Type result,
            StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (function == AggregateFunction.Count)
            {
                return operand == null ? "COUNT(*)" : $"COUNT({operand})";
            }
            if (operand == null || source == null)
            {
                throw new ArgumentException($"{function} needs a column.", nameof(operand));
            }
            var underlying = Nullable.GetUnderlyingType(source) ?? source;
            switch (function)
            {
                case AggregateFunction.Sum:
                    var sum = Sum(operand, underlying);
                    return AggregateResult($"COALESCE({sum}, {ZeroSum(underlying)})", function, underlying, result);
                case AggregateFunction.Avg:
                    var average = Average(operand, underlying, Nullable.GetUnderlyingType(result) ?? result);
                    if (average != null) return AggregateResult(average, function, underlying, result);
                    if (ReferenceEquals(this, Portable)) return $"AVG({operand})";
                    throw new NotSupportedException(NoExactAverage(operand, underlying, result));
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    var name = function == AggregateFunction.Min ? "MIN" : "MAX";
                    if (underlying == typeof(string)) return StringExtreme(function, name, operand, stringComparison);
                    return AggregateResult($"{name}({operand})", function, underlying, result);
                default:
                    throw new NotSupportedException($"Aggregate function {function} is not supported.");
            }
        }

        /// <summary>
        /// Whether this provider averages <paramref name="source"/> values into <paramref name="result"/> as C# does, in one
        /// column. The store's <c>Aggregate</c> reads a <c>SUM</c> and a <c>COUNT</c> instead where it does not; a view, whose
        /// column is one value, refuses.
        /// </summary>
        public bool HasExactAverage(Type source, Type result)
            => Average("x", Nullable.GetUnderlyingType(source) ?? source, Nullable.GetUnderlyingType(result) ?? result) != null;

        /// <summary><c>SUM(operand)</c> over a column of <paramref name="source"/> values. SQLite sums a decimal exactly.</summary>
        public virtual string Sum(string operand, Type source) => $"SUM({operand})";

        /// <summary>The 0 a <c>SUM</c> over no values answers. SQLite's decimal is text.</summary>
        protected virtual string ZeroSum(Type source) => "0";

        /// <summary>
        /// <c>AVG</c> over a column of <paramref name="source"/> values read into <paramref name="result"/>, exactly as C#'s
        /// <c>Average</c>, or null when this provider has no such form in one column.
        /// </summary>
        /// <remarks>
        /// An integral column is averaged as a double (<see cref="FloatingOperand"/>), which is C#'s <c>Average</c> of an
        /// integer: SQL Server averaged it in its own type (TASK-887). The sum of doubles is exact while it stays below
        /// 2^53, and the division is the double division C# performs. A <c>decimal</c> average, or one read into a
        /// <c>decimal</c>, has no exact form on a server, whose quotient keeps the server's own scale: null.
        /// </remarks>
        protected virtual string? Average(string operand, Type source, Type result)
        {
            if (IsDecimal(source) || IsDecimal(result)) return null;
            return SqlConversion.IsIntegral(source) ? $"AVG({FloatingOperand(operand)})" : $"AVG({operand})";
        }

        /// <summary>
        /// An integral <paramref name="operand"/> as a double. <c>1.0E0</c> is a double on SQL Server, MySQL and SQLite;
        /// PostgreSQL, where it is an exact numeric, overrides it.
        /// </summary>
        protected virtual string FloatingOperand(string operand) => operand + " * 1.0E0";

        /// <summary>
        /// The final form of <paramref name="function"/> over a column of <paramref name="source"/> values read into
        /// <paramref name="result"/>. SQLite gives a decimal result, which is text there, the decimal collation, so it orders
        /// as a number.
        /// </summary>
        protected virtual string AggregateResult(string sql, AggregateFunction function, Type source, Type result) => sql;

        /// <summary>
        /// TASK-907: a string <c>MIN</c> / <c>MAX</c> in <paramref name="comparison"/>'s order. <see cref="Portable"/> (the
        /// migration generator, a message) renders the plain form, which follows the column's collation, as it renders a plain
        /// <c>=</c> for an ordinal join (TASK-895); a provider renders Ordinal or refuses.
        /// </summary>
        private string StringExtreme(AggregateFunction function, string name, string operand, StringComparison comparison)
        {
            if (ReferenceEquals(this, Portable)) return $"{name}({operand})";
            if (comparison != StringComparison.Ordinal)
                throw AggregateMath.StringOrderNotSupported("SQL aggregate", function, operand, comparison);
            return OrdinalStringExtreme(name, operand)
                ?? throw AggregateMath.StringOrderNotSupported("SQL aggregate", function, operand, comparison, null);
        }

        /// <summary>
        /// TASK-907: <c>MIN</c> or <c>MAX</c> (<paramref name="function"/>) of a string column in Ordinal order, UTF-16 code
        /// unit by code unit with no padding, answering the column's own value; or null where this provider has no such form.
        /// Measured before: every server took it in the column's collation (PostgreSQL's and MySQL's default put
        /// <c>"apple"</c> below <c>"Banana"</c>, SQL Server's tied <c>"a"</c> and <c>"a "</c> and answered either), and a
        /// byte order (SQLite's <c>BINARY</c>, PostgreSQL's <c>"C"</c>, MySQL's <c>utf8mb4_0900_bin</c>) is code-point
        /// order, which puts a character above U+FFFF above U+E000–U+FFFF where UTF-16 puts it below.
        /// </summary>
        protected virtual string? OrdinalStringExtreme(string function, string operand) => null;

        /// <summary>
        /// TASK-909 (the user's decision, 2026-11-11): the <c>ORDER BY</c> terms that order rows by a string
        /// <paramref name="operand"/> (a column, or an aggregate's SQL) in Ordinal order, NULL below every value, as
        /// <c>AggregateHelper</c> orders them: <see cref="OrdinalOrderKey"/>, after a NULL-first term where this provider puts
        /// NULL last when ascending (<see cref="NullsOrderFirst"/>). The caller appends each term's direction.
        /// <see cref="Portable"/> renders the operand itself, which follows the column's collation, as it renders a plain
        /// string <c>MIN</c>; a provider renders Ordinal or refuses.
        /// </summary>
        /// <remarks>
        /// Measured before (the row order cases of the shared <c>AggregateStringOrderContract</c>): every server ordered in its
        /// column's collation, before its LIMIT / OFFSET, so a page held other rows than the in-memory aggregation's.
        /// PostgreSQL and MySQL put <c>"apple"</c> before <c>"Banana"</c>, PostgreSQL put NULL last, SQL Server put
        /// <c>"a\t"</c> before <c>"a"</c> (space padding), and SQLite's <c>BINARY</c> put U+1F600 above U+FF5E. A string MIN
        /// ordered by its alias followed the column's collation too, except on SQLite, whose result keeps the ordinal collation.
        /// </remarks>
        /// <param name="operand">The string column or aggregate SQL the rows are ordered by.</param>
        /// <param name="role">What is ordered by, for a refusal: e.g. <c>group-by field 'Name'</c>.</param>
        /// <exception cref="NotSupportedException">This provider has no Ordinal key.</exception>
        public System.Collections.Generic.IReadOnlyList<string> OrdinalOrderTerms(string operand, string role)
        {
            if (ReferenceEquals(this, Portable)) return new[] { operand };
            var key = OrdinalOrderKey(operand)
                ?? throw AggregateMath.StringRowOrderNotSupported("SQL aggregate", role, StringComparison.Ordinal, null);
            return NullsOrderFirst ? new[] { key } : new[] { $"CASE WHEN {operand} IS NULL THEN 0 ELSE 1 END", key };
        }

        /// <summary>
        /// TASK-909: an expression over a string <paramref name="operand"/> that orders as C#'s Ordinal does (UTF-16 code units,
        /// no padding), NULL staying NULL; or null where this provider has none. The one producer of the Ordinal key: a
        /// provider's <see cref="OrdinalStringExtreme"/> takes its <c>MIN</c> / <c>MAX</c> of the same key, so a row order and a
        /// string extreme agree.
        /// </summary>
        protected virtual string? OrdinalOrderKey(string operand) => null;

        /// <summary>TASK-909: whether this provider orders NULL below every value when ascending (and above when descending), as C# does.</summary>
        protected virtual bool NullsOrderFirst => true;

        private static bool IsDecimal(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(decimal);

        private static string NoExactAverage(string operand, Type source, Type result)
            => $"AVG({operand}) of {source.Name} values into {(Nullable.GetUnderlyingType(result) ?? result).Name} cannot be computed "
               + "on this provider as C# computes it: C#'s decimal division keeps 28 to 29 significant digits, and the server keeps "
               + "its own scale (SQL Server 6 decimals, MySQL 4 more than the column, PostgreSQL about 16 significant digits). "
               + "Read SUM and COUNT into two view properties and divide them, or average into a double. (TASK-892)";
    }
}
