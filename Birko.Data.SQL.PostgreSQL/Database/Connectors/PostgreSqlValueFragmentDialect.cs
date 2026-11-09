using System;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-869 — PostgreSQL's spelling of the parts of a filter whose portable form matches something else there: the
    /// portable value fragment, with PostgreSQL's rounding, and a string comparison that matches exactly what C# matches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StringComparison.Ordinal"/> is <c>LIKE</c> under <c>COLLATE "C"</c>, byte-wise and so code-point-exact
    /// (measured: 0 of 240 cases off, mixed case, accents, Greek, metacharacters, supplementary characters). The collation
    /// is named rather than left to the column's because a nondeterministic collation refuses <c>LIKE</c> altogether.
    /// </para>
    /// <para>
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> is a regular expression of C#'s case classes
    /// (<see cref="SqlStringMatch.RegexCaseClassPattern"/>) under <c>COLLATE "C"</c>: 0 of 240 off. <c>UPPER(col) LIKE
    /// UPPER(term)</c> was 32 off, because PostgreSQL's case tables are not C#'s (<c>ı</c>, <c>ſ</c> and 14 more).
    /// </para>
    /// </remarks>
    public sealed class PostgreSqlValueFragmentDialect : SqlValueFragmentDialect
    {
        public PostgreSqlValueFragmentDialect(Func<string, int, MidpointRounding, string> round) : base(round) { }

        /// <inheritdoc />
        public override string StringMatch(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
            => comparison switch
            {
                StringComparison.Ordinal => CollatedLike(operand, "\"C\"", bind(SqlStringMatch.LikePattern(kind, term))),
                StringComparison.OrdinalIgnoreCase => $"{operand} COLLATE \"C\" ~ {bind(SqlStringMatch.RegexCaseClassPattern(kind, term, "^", "$"))}",
                _ => base.StringMatch(kind, operand, term, comparison, bind),
            };

        /// <summary>
        /// TASK-873 — <c>(operand)::text COLLATE "C"</c>, byte-wise. A deterministic collation (every database default, and so
        /// every column the framework creates) already compares equal strings only when they are identical (measured: 0 of 134
        /// shapes off), so this guards a column declared with a nondeterministic ICU collation (measured: <c>Ci = $1</c> with a
        /// <c>text</c> parameter <c>'abc'</c> matched <c>ABC</c>). A <c>citext</c> column compared with the <c>text</c> parameter
        /// Npgsql binds was already exact (<c>citext = text</c> resolves to <c>text = text</c>, measured); the cast to <c>text</c>
        /// keeps it so whatever the parameter's type. An explicit collation on one operand decides the comparison, so a bound
        /// value is left as it is.
        /// </summary>
        protected override string? OrdinalText(string operand, bool bothFromRow) => $"({operand})::text COLLATE \"C\"";
    }
}
