using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-513 — exact <c>decimal</c> arithmetic and ordering on SQLite, which has no decimal storage class.
    /// A <c>decimal</c> is kept as TEXT (Microsoft.Data.Sqlite already binds a <c>decimal</c> parameter as
    /// TEXT), and these registrations make that TEXT behave as a number: <see cref="Collation"/> orders and
    /// compares it, <see cref="AddFunction"/> adds without passing through a double, and
    /// <see cref="SumAggregate"/> / <see cref="AvgAggregate"/> aggregate it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every connection that touches a framework-created table must call <see cref="Register"/></b>, before
    /// or after <c>Open</c> (measured: a registration on an unopened connection survives <c>Open</c>, a
    /// <c>Close</c>/<c>Open</c> of the same object, and pooling on and off). The framework's own connections
    /// all come from <see cref="SqLiteConnector.CreateConnection"/>, which does it. A connection that did not
    /// — a consumer's raw <c>SqliteConnection</c>, the <c>sqlite3</c> CLI — can still <c>SELECT</c> the
    /// column, but <c>ORDER BY</c> on it, and any write to a table with an index on it, fail with
    /// <c>no such collation sequence: BIRKO_DECIMAL</c> (measured).
    /// </para>
    /// <para>
    /// A value that cannot be parsed refuses: the functions and aggregates throw, which SQLite surfaces as a
    /// <see cref="SqliteException"/> on the statement, and an overflow past <see cref="decimal.MaxValue"/>
    /// does the same rather than rounding. The collation alone cannot refuse — a comparison callback has no
    /// error channel — so it orders an unparseable text after every number, ordinally among themselves,
    /// which keeps the order total and the index consistent.
    /// </para>
    /// </remarks>
    public static class SqLiteDecimal
    {
        public const string Collation = "BIRKO_DECIMAL";
        public const string AddFunction = "birko_decimal_add";
        public const string SumAggregate = "birko_decimal_sum";
        public const string AvgAggregate = "birko_decimal_avg";

        /// <summary>
        /// The text form Microsoft.Data.Sqlite binds a <c>decimal</c> parameter as (measured: <c>10.10m</c>
        /// → <c>10.1</c>, <c>10m</c> → <c>10.0</c>), so a value computed in SQL is stored in the same form as
        /// one written by a parameter.
        /// </summary>
        private const string TextFormat = "0.0###########################";

        public static void Register(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            connection.CreateCollation(Collation, Compare);
            connection.CreateFunction<object?, object?, string?>(AddFunction,
                (a, b) => a == null || b == null ? null : Format(ToDecimal(a) + ToDecimal(b)),
                isDeterministic: true);
            connection.CreateAggregate<object?, (decimal Sum, long Count), string?>(SumAggregate,
                (0m, 0L),
                (acc, value) => value == null ? acc : (acc.Sum + ToDecimal(value), acc.Count + 1),
                acc => acc.Count == 0 ? null : Format(acc.Sum),
                isDeterministic: true);
            connection.CreateAggregate<object?, (decimal Sum, long Count), string?>(AvgAggregate,
                (0m, 0L),
                (acc, value) => value == null ? acc : (acc.Sum + ToDecimal(value), acc.Count + 1),
                acc => acc.Count == 0 ? null : Format(acc.Sum / acc.Count),
                isDeterministic: true);
        }

        public static string Format(decimal value) => value.ToString(TextFormat, CultureInfo.InvariantCulture);

        internal static int Compare(string? x, string? y)
        {
            var xOk = TryParse(x, out var xv);
            var yOk = TryParse(y, out var yv);
            if (xOk && yOk) return xv.CompareTo(yv);
            if (xOk) return -1;
            if (yOk) return 1;
            return string.CompareOrdinal(x, y);
        }

        private static decimal ToDecimal(object value)
        {
            switch (value)
            {
                case string text:
                    if (TryParse(text, out var parsed)) return parsed;
                    throw new FormatException($"'{text}' is not a decimal value.");
                case long integer:
                    return integer;
                case double real:
                    // A REAL already lost whatever a double cannot hold; converting it does not lose more.
                    return (decimal)real;
                default:
                    throw new FormatException($"A {value.GetType().Name} value is not a decimal value.");
            }
        }

        private static bool TryParse(string? text, out decimal value)
            => decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);
    }
}
