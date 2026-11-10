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

        /// <summary>
        /// TASK-892 — a double. <c>1.0E0</c> is an exact numeric here, so the average was a NUMERIC rounded to about 17
        /// significant digits, and read as a double it was not C#'s (measured: 1000000003.6666667, C# 1000000003.6666666).
        /// </summary>
        protected override string FloatingOperand(string operand) => $"CAST({operand} AS DOUBLE PRECISION)";

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

        /// <summary>
        /// TASK-907 — the extreme of a key under <c>COLLATE "C"</c>, unwrapped: the key is the text with U+10FFFF put before
        /// each character in U+E000–U+FFFF. <c>"C"</c> is byte order, which on a UTF8 database is code-point order; UTF-16,
        /// and so C#'s Ordinal, puts a character above U+FFFF (a surrogate pair, D800–DFFF) below U+E000–U+FFFF, and the
        /// prefix moves those above every character that follows them. Measured: the default collation answered
        /// <c>"apple"</c> below <c>"Banana"</c>; <c>"C"</c> alone answered U+FF5E below U+1F600. U+10FFFF itself, a
        /// noncharacter, is dropped from the answer and is not ordered as C# orders it: the one known edge, kept documented
        /// (the user's decision).
        /// </summary>
        protected override string? OrdinalStringExtreme(string function, string operand)
            => $"replace({function}(regexp_replace(({operand})::text, '[' || chr(57344) || '-' || chr(65535) || ']', "
               + $"chr(1114111) || E'\\\\&', 'g') COLLATE \"C\"), chr(1114111), '')";
    }
}
