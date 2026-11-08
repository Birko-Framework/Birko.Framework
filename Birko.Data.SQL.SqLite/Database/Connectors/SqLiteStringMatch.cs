using System;
using System.Globalization;
using Birko.Data.SQL.Conditions;
using Microsoft.Data.Sqlite;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-869 — a string <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> / <c>Equals</c> with
    /// <see cref="StringComparison.Ordinal"/> or <see cref="StringComparison.OrdinalIgnoreCase"/> on SQLite, computed by C#
    /// itself through <see cref="Function"/>.
    /// </summary>
    /// <remarks>
    /// SQLite's <c>LIKE</c> is case-insensitive for ASCII only and its <c>UPPER</c> / <c>LOWER</c> fold ASCII only
    /// (measured: <c>UPPER(col) LIKE UPPER(term)</c> was 54 of 240 cases off C#'s <c>OrdinalIgnoreCase</c> — <c>é</c>/<c>É</c>,
    /// <c>ä</c>/<c>Ä</c>, Greek; plain <c>LIKE</c> matched <c>"abc"</c> for <c>Contains("A", Ordinal)</c>). The function
    /// answers with C#'s own comparison: 0 of 240 off, for both. Registered by <see cref="SqLiteDecimal.Register"/>, which
    /// every framework connection calls; a statement that names it on a connection that did not fails with
    /// <c>no such function</c>.
    /// </remarks>
    public static class SqLiteStringMatch
    {
        public const string Function = "birko_string_match";

        public static void Register(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            connection.CreateFunction<string?, string?, long, long, bool?>(Function,
                (value, term, kind, ignoreCase) => value == null || term == null ? null : Matches(value, term, kind, ignoreCase != 0),
                isDeterministic: true);
        }

        /// <summary><c>birko_string_match(operand, @term, kind, ignoreCase)</c>: NULL when the operand is NULL.</summary>
        public static string Call(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
        {
            var ignoreCase = comparison switch
            {
                StringComparison.Ordinal => 0,
                StringComparison.OrdinalIgnoreCase => 1,
                _ => throw new NotSupportedException(
                    $"Cannot translate a string comparison with StringComparison.{comparison} to SQL: only Ordinal and "
                    + "OrdinalIgnoreCase are translated."),
            };
            return string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2}, {3}, {4})",
                Function, operand, bind(term), KindCode(kind), ignoreCase);
        }

        private static long KindCode(ConditionType kind) => kind switch
        {
            ConditionType.Like => 0,
            ConditionType.StartsWith => 1,
            ConditionType.EndsWith => 2,
            ConditionType.TextEqual => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A string match is Like, StartsWith, EndsWith or TextEqual."),
        };

        private static bool Matches(string value, string term, long kind, bool ignoreCase)
        {
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return kind switch
            {
                0 => value.Contains(term, comparison),
                1 => value.StartsWith(term, comparison),
                2 => value.EndsWith(term, comparison),
                3 => string.Equals(value, term, comparison),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown string match kind."),
            };
        }
    }
}
