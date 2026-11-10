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
        /// <param name="sumOfNoValuesIsZero">
        /// A <c>SUM</c> over no values (a group of only NULLs) answers 0 rather than NULL: a view, whose property LINQ's
        /// <c>Sum</c> fills with 0 for <c>int?</c> as for <c>int</c>. The store's <c>Aggregate</c> keeps NULL, which is what
        /// <c>AggregateHelper</c> answers on every other store (TASK-878).
        /// </param>
        /// <exception cref="NotSupportedException">
        /// An <c>AVG</c> this provider cannot compute as C# does (<see cref="Average"/> answered null), on a provider's dialect.
        /// </exception>
        public string Aggregate(AggregateFunction function, string? operand, Type? source, Type result, bool sumOfNoValuesIsZero)
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
                    return AggregateResult(sumOfNoValuesIsZero ? $"COALESCE({sum}, {ZeroSum(underlying)})" : sum, function, underlying, result);
                case AggregateFunction.Avg:
                    var average = Average(operand, underlying, Nullable.GetUnderlyingType(result) ?? result);
                    if (average != null) return AggregateResult(average, function, underlying, result);
                    if (ReferenceEquals(this, Portable)) return $"AVG({operand})";
                    throw new NotSupportedException(NoExactAverage(operand, underlying, result));
                case AggregateFunction.Min:
                    return AggregateResult($"MIN({operand})", function, underlying, result);
                case AggregateFunction.Max:
                    return AggregateResult($"MAX({operand})", function, underlying, result);
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

        /// <summary>The 0 a <c>SUM</c> over no values answers where <c>sumOfNoValuesIsZero</c>. SQLite's decimal is text.</summary>
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

        private static bool IsDecimal(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(decimal);

        private static string NoExactAverage(string operand, Type source, Type result)
            => $"AVG({operand}) of {source.Name} values into {(Nullable.GetUnderlyingType(result) ?? result).Name} cannot be computed "
               + "on this provider as C# computes it: C#'s decimal division keeps 28 to 29 significant digits, and the server keeps "
               + "its own scale (SQL Server 6 decimals, MySQL 4 more than the column, PostgreSQL about 16 significant digits). "
               + "Read SUM and COUNT into two view properties and divide them, or average into a double. (TASK-892)";
    }
}
