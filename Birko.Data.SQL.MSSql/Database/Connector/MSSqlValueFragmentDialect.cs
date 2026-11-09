using System;
using System.Collections.Generic;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-869 — SQL Server's spelling of the parts of a filter whose portable form matches something else there: the
    /// portable value fragment, with SQL Server's rounding, and a string comparison that matches exactly what C# matches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both comparisons are <c>LIKE</c> under <c>COLLATE Latin1_General_100_BIN2</c>, which compares UTF-16 code units, so
    /// neither the column's case- nor its accent-insensitivity applies (the default <c>SQL_Latin1_General_CP1_CI_AS</c> is
    /// both case-insensitive and blind to nothing C# cares about here). <see cref="StringComparison.Ordinal"/> binds the
    /// escaped term (measured: 0 of 240 cases off). <see cref="StringComparison.OrdinalIgnoreCase"/> binds C#'s case classes
    /// as bracket classes (<see cref="SqlStringMatch.LikeCaseClassPattern"/>): 0 of 232 off, and a term holding a cased
    /// letter outside the Basic Multilingual Plane is refused, because a bracket class of surrogate pairs matched nothing.
    /// <c>UPPER(col) LIKE UPPER(term)</c> was 33 off: SQL Server's case tables put 625 to 1101 characters in a different
    /// class than C# does.
    /// </para>
    /// </remarks>
    public sealed class MSSqlValueFragmentDialect : SqlValueFragmentDialect
    {
        private const string BinaryCollation = "Latin1_General_100_BIN2";

        public MSSqlValueFragmentDialect(Func<string, int, MidpointRounding, string> round) : base(round) { }

        /// <summary>
        /// The longest LIKE pattern SQL Server accepts, in UTF-16 code units. Measured on 2022: 4000 runs, 4001 fails with
        /// Msg 8152 ("String or binary data would be truncated"), bound as an inferred <c>nvarchar</c> and as
        /// <c>nvarchar(max)</c> alike, so the limit is the pattern's, not the parameter's.
        /// </summary>
        public const int MaxLikePatternLength = 4000;

        /// <summary>
        /// TASK-870 — <c>REPLACE</c> over the operand in <see cref="BinaryCollation"/>, so it matches ordinally, as C# does.
        /// Measured under the default collation, 8 of 11 shapes stored another string than C#: <c>Replace("b", "X")</c>
        /// changed <c>ABC</c>, <c>Replace("ss", "_")</c> changed <c>Straße</c>, <c>Replace("ß", "_")</c> changed
        /// <c>STRASSE</c>, and <c>Replace("e", "_")</c> left <c>e</c> followed by a combining accent alone.
        /// </summary>
        public override string Replace(string operand, string oldValue, string newValue)
            => base.Replace($"{operand} COLLATE {BinaryCollation}", oldValue, newValue);

        /// <summary>
        /// TASK-873 — <c>(operand + N'|') COLLATE Latin1_General_100_BIN2</c>, and the same on the value's side
        /// (<see cref="OrdinalValue"/>). <see cref="BinaryCollation"/> compares UTF-16 code units, but SQL Server's <c>=</c> pads
        /// the shorter operand with spaces under every collation, BIN2 included (measured: <c>N'abc ' COLLATE …_BIN2 = N'abc'</c>
        /// is true, and so is <c>N'' = N' '</c>), so a terminator is appended to both: the trailing spaces are no longer trailing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// No <c>CAST(… AS NVARCHAR(MAX))</c> on a column compared with values: it made every row's operand and every list item
        /// a LOB and the <c>IN</c> a linear scan per row. Measured on 20,000 rows with an indexed <c>NVARCHAR(64)</c>: 1000 values
        /// took 18.3 s (36 ms before TASK-873), 2000 timed out, a 500-value <c>NOT IN</c> 35.6 s; without the cast 92 ms and
        /// 29 ms (independent review). The concatenation of two non-<c>MAX</c> strings is cut at 4000 characters, so the
        /// terminator is lost only on a 4000-character operand; the plain term in front (<c>col = @p</c>) already rules out every
        /// row whose text differs before that, so the cut can only make two 4000-character texts that are equal compare as
        /// equal, or keep a <c>MAX</c> row equal to a 4000-character value from matching it. <see cref="OrdinalValue"/> closes
        /// that: such a value is offered both cut and uncut.
        /// </para>
        /// <para>
        /// Two operands read from the row (<c>x.A == x.B</c>, <paramref name="bothFromRow"/>) are cast to <c>NVARCHAR(MAX)</c>:
        /// their columns may be cut differently, and no index answers that comparison anyway.
        /// </para>
        /// </remarks>
        protected override string? OrdinalText(string operand, bool bothFromRow)
            => bothFromRow
                ? $"(CAST({operand} AS NVARCHAR(MAX)) + N'|') COLLATE {BinaryCollation}"
                : $"({operand} + N'|') COLLATE {BinaryCollation}";

        /// <summary>
        /// TASK-873 — <c>(@p + N'|') COLLATE Latin1_General_100_BIN2</c>. A value of 4000 characters or more is offered a second
        /// time, cast to <c>NVARCHAR(MAX)</c>: SqlClient declares it <c>nvarchar(4000)</c>, so its terminator is cut, which a
        /// column of at most 4000 characters (cut the same way) matches, while an <c>NVARCHAR(MAX)</c> column keeps its terminator
        /// and matches the uncut form.
        /// </summary>
        protected override IEnumerable<string> OrdinalValue(string parameter, object? value)
        {
            yield return $"({parameter} + N'|') COLLATE {BinaryCollation}";
            if (value is string text && text.Length >= 4000)
                yield return $"(CAST({parameter} AS NVARCHAR(MAX)) + N'|') COLLATE {BinaryCollation}";
        }

        /// <inheritdoc />
        public override string StringMatch(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
        {
            var pattern = comparison switch
            {
                StringComparison.Ordinal => SqlStringMatch.LikePattern(kind, term),
                StringComparison.OrdinalIgnoreCase => SqlStringMatch.LikeCaseClassPattern(kind, term),
                _ => null,
            };
            if (pattern == null) return base.StringMatch(kind, operand, term, comparison, bind);
            // An OrdinalIgnoreCase pattern spends four characters on each cased letter ([Aa]), so a ~1000-letter term reaches it.
            RequireLikePattern(pattern, $"a string comparison with StringComparison.{comparison}");
            return CollatedLike(operand, BinaryCollation, bind(pattern));
        }

        /// <summary>
        /// TASK-874 — refuses a pattern past <see cref="MaxLikePatternLength"/>. The limit counts the whole pattern: the
        /// <c>%</c> wildcards and each escape character (measured: 3999 plain characters inside <c>%…%</c> fail, and so do
        /// 2000 escaped <c>%</c>), so a no-argument <c>Contains</c> takes at most 3998 plain characters. Before, a read
        /// past it returned no rows, silently (the error arrives after the result's metadata and the reader never read),
        /// and a count, update or delete failed with Msg 8152.
        /// </summary>
        public override void RequireLikePattern(string pattern, string form)
        {
            if (pattern.Length <= MaxLikePatternLength) return;
            throw new NotSupportedException(
                $"Cannot translate {form} to SQL Server: its LIKE pattern would be {pattern.Length} characters, past SQL "
                + $"Server's limit of {MaxLikePatternLength} (Msg 8152). Shorten the term, or compare in memory.");
        }
    }
}
