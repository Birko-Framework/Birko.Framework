using System;
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
