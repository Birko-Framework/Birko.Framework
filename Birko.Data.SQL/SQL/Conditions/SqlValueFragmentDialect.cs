using System;
using System.Linq.Expressions;

namespace Birko.Data.SQL.Conditions
{
    /// <summary>
    /// TASK-856 — how a provider renders the parts of a value fragment (a filter operand that is arithmetic,
    /// <c>COALESCE</c> or <c>CASE</c> over the row, TASK-213) whose portable form compares wrongly on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter parser has no dialect, so it renders every fragment in <see cref="Portable"/> form into
    /// <see cref="Condition.Name"/>, and also keeps a re-render (<see cref="Condition.NameFragment"/>,
    /// <see cref="Condition.FieldFragment"/>) that a connector whose <c>FragmentDialect</c> is not
    /// <see cref="Portable"/> calls at render time. The portable form is what every provider but SQLite runs, except
    /// for <c>Math.Round</c> (TASK-857): every connector's dialect rounds through its own <c>ComputedRound</c>, and the
    /// portable form of a round is no server's SQL.
    /// </para>
    /// <para>
    /// SQLite's reason (measured): a <c>decimal</c> is TEXT there, compared through a collation, and the portable
    /// fragment loses both. <c>Amount + 0</c> is a REAL, never equal to the TEXT a decimal parameter binds as, and
    /// always less than it; <c>COALESCE</c> / <c>CASE</c> keep the TEXT but compare it byte-wise.
    /// </para>
    /// </remarks>
    public class SqlValueFragmentDialect
    {
        /// <summary>The form the parser writes into <see cref="Condition.Name"/>: plain operators, inlined literals.</summary>
        public static readonly SqlValueFragmentDialect Portable = new();

        private readonly Func<string, int, MidpointRounding, string>? _round;

        /// <param name="round">
        /// TASK-857: the provider's <c>Math.Round(value, digits, mode)</c>, the one a computed set uses
        /// (<c>AbstractConnectorBase.ComputedRound</c>). Null for <see cref="Portable"/>, which has none.
        /// </param>
        protected SqlValueFragmentDialect(Func<string, int, MidpointRounding, string>? round = null) => _round = round;

        /// <summary>A provider's dialect: the portable form, with the provider's rounding.</summary>
        public static SqlValueFragmentDialect WithRounding(Func<string, int, MidpointRounding, string> round)
            => new(round ?? throw new ArgumentNullException(nameof(round)));

        /// <summary><c>left op right</c> computed in <paramref name="type"/> (the node's CLR type, maybe nullable).</summary>
        /// <remarks>
        /// TASK-870: a <c>decimal</c> division that reaches here is one the parser could not rewrite into an exact product
        /// (<c>DataBase.TryExactDecimalReciprocal</c>, the one producer of that decision: a division by a constant with a
        /// terminating reciprocal, <c>/ 4m</c>, becomes <c>* 0.25</c>). It divides by <c>3m</c> or by a column, and it is
        /// refused by every provider dialect that does not override it (SQLite computes it in C#'s decimal). C# keeps 28 to 29
        /// significant digits of a quotient; each server keeps its own scale (measured: SQL Server six decimals for
        /// <c>Amount / 3m</c>, MySQL four more than the dividend, PostgreSQL about sixteen significant digits), so equalities
        /// with C#'s quotient, <c>x.Amount / 3m * 3m == x.Amount</c>, orderings within the lost digits and rounding past them
        /// all differ. <see cref="Portable"/>, which the parser renders before it knows the provider, writes the division; the
        /// provider's re-render refuses it before anything runs.
        /// </remarks>
        public virtual string Arithmetic(ExpressionType op, string sqlOperator, string left, string right, Type type)
        {
            if (op == ExpressionType.Divide && SqlConversion.IsIntegral(type))
                return IntegralDivide(left, right);
            if (op == ExpressionType.Divide && (Nullable.GetUnderlyingType(type) ?? type) == typeof(decimal) && !ReferenceEquals(this, Portable))
                throw new NotSupportedException(
                    "Cannot translate the division of a decimal over the row to SQL on this provider: C# keeps 28 to 29 significant "
                    + "digits of the quotient, and this provider keeps its own scale (SQL Server as few as six decimals, MySQL four "
                    + "more than the dividend), so the quotient can differ from C#'s. Only a division by a constant whose "
                    + "reciprocal terminates (/ 2m, / 4m, / 2.5m, / 100m) is translated, as an exact product. Multiply instead "
                    + "(x.Amount > 3m * x.Divisor for x.Amount / x.Divisor > 3m, when the divisor is positive), or compute the "
                    + "value in memory.");
            return $"({left} {sqlOperator} {right})";
        }

        /// <summary>
        /// TASK-860 — C#'s integer division, truncating toward zero. <c>/</c> is that on SQLite, PostgreSQL and SQL Server;
        /// MySQL's <c>/</c> always divides fractions (measured: <c>x.Hits / 4 == 0</c> missed 4 of 5 rows) and overrides this.
        /// </summary>
        public virtual string IntegralDivide(string left, string right) => $"({left} / {right})";

        /// <summary>A constant's literal, or null for the portable one.</summary>
        public virtual string? Literal(object value) => null;

        /// <summary>
        /// A C# conversion of <paramref name="operand"/>. TASK-860: a transparent one (nullable lift, enum to integer,
        /// widening integer) renders nothing; an integer becoming a <c>decimal</c> or <c>double</c> is an explicit
        /// <see cref="Cast"/>, because without it <c>(decimal)x.Hits / 4m</c> divided two integers on PostgreSQL and SQL
        /// Server (measured); any other conversion is refused. It used to drop every conversion.
        /// </summary>
        public virtual string Conversion(string operand, Type from, Type to) => SqlConversion.Classify(from, to) switch
        {
            SqlConversionKind.Transparent => operand,
            SqlConversionKind.Widening => Cast(operand, from, Nullable.GetUnderlyingType(to) ?? to),
            _ => throw new NotSupportedException(
                $"Cannot translate the conversion from {(Nullable.GetUnderlyingType(from) ?? from).Name} to "
                + $"{(Nullable.GetUnderlyingType(to) ?? to).Name} over the row to SQL: it is not exact on every provider."),
        };

        /// <summary>
        /// TASK-860 — an exact widening of the integer <paramref name="operand"/> (of <paramref name="from"/>) to
        /// <paramref name="to"/>, <c>decimal</c>, <c>double</c> or <c>float</c>.
        /// </summary>
        protected virtual string Cast(string operand, Type from, Type to)
            => to == typeof(decimal)
                ? $"CAST({operand} AS DECIMAL({SqlConversion.DecimalDigits(from).ToString(System.Globalization.CultureInfo.InvariantCulture)},0))"
                : $"CAST({operand} AS {FloatingTypeName(to)})";

        /// <summary>
        /// TASK-860 — true when storing an integer that does not fit its column is an error on this provider, so an update
        /// SET may narrow an integer as C# does (<c>(short)(x.Sh + 1)</c>): exact until it overflows, and an overflow fails
        /// instead of storing a value C# would have wrapped. Measured (CallCastFilterContract's overflow case): PostgreSQL,
        /// SQL Server and MySQL (under the STRICT_TRANS_TABLES its 8.x default carries) fail the statement and store nothing.
        /// </summary>
        public virtual bool StoresIntegerOverflowLoudly => true;

        /// <summary>The SQL type a <c>double</c> (or <c>float</c>) cast names; <c>DOUBLE PRECISION</c> is the standard's.</summary>
        protected virtual string FloatingTypeName(Type type) => type == typeof(float) ? "REAL" : "DOUBLE PRECISION";

        /// <summary>An operand of a comparison performed in <paramref name="comparedType"/>.</summary>
        public virtual string Compared(string operand, Type comparedType) => operand;

        /// <summary>
        /// TASK-857 — <c>Math.Round(value, digits, mode)</c> over a <c>decimal</c>, with the mode always explicit. Rounding to
        /// even has no portable SQL (each server truncates differently), so <see cref="Portable"/> writes a call no server
        /// knows: a condition rendered without its provider's dialect fails loudly instead of rounding the wrong way.
        /// </summary>
        public virtual string Round(string value, int digits, MidpointRounding mode, Type type)
            => _round != null
                ? _round(value, digits, mode)
                : $"BIRKO_ROUND_{mode.ToString().ToUpperInvariant()}({value}, {digits.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

        /// <summary>TASK-857 — <c>Math.Abs(operand)</c> in <paramref name="type"/>.</summary>
        public virtual string Abs(string operand, Type type) => $"ABS({operand})";

        /// <summary>TASK-857 — <c>-operand</c> in <paramref name="type"/>. Parenthesised, so two minus signs never meet as a comment.</summary>
        public virtual string Negate(string operand, Type type) => $"(-({operand}))";

        /// <summary>
        /// TASK-870 — <c>ToLower()</c> / <c>ToUpper()</c> (<paramref name="invariant"/> false: the current culture) or their
        /// invariant forms, over a string. <c>LOWER</c> / <c>UPPER</c> here, which fold by the server's own case tables: measured
        /// against C# over 34 texts, PostgreSQL stores a different fold for 1, MySQL for up to 2, SQL Server for 7 to 8
        /// (<c>K</c> → <c>k</c>, <c>ſ</c> → <c>S</c>, Turkish <c>İ</c> / <c>ı</c>, …). SQLite, which folds ASCII only, overrides it.
        /// </summary>
        public virtual string CaseFold(string operand, bool upper, bool invariant) => upper ? $"UPPER({operand})" : $"LOWER({operand})";

        /// <summary>
        /// TASK-870 — a DateTime column's <c>.Date</c> as an update SET value: midnight of that day, in the column's type.
        /// <c>CAST(… AS DATE)</c>, which PostgreSQL, MySQL and SQL Server convert back to the column's type on assignment. It
        /// used to be <c>DATE(…)</c>, which SQL Server has no function of (measured: "'DATE' is not a recognized built-in
        /// function name"). SQLite, which stores a DateTime as text, overrides it.
        /// </summary>
        public virtual string DateOf(string operand) => $"CAST({operand} AS DATE)";

        /// <summary>
        /// TASK-870 — <c>string.Replace(oldValue, newValue)</c>, which C# matches ordinally. <c>REPLACE</c> is ordinal on
        /// SQLite, PostgreSQL and MySQL (measured); SQL Server matches by the column's collation and overrides it.
        /// </summary>
        public virtual string Replace(string operand, string oldValue, string newValue) => $"REPLACE({operand}, {oldValue}, {newValue})";

        /// <summary>
        /// TASK-869 — the predicate for <c>operand.Contains(term, comparison)</c> (<paramref name="kind"/>
        /// <see cref="ConditionType.Like"/>), <c>StartsWith</c>, <c>EndsWith</c> or <c>string.Equals</c>
        /// (<see cref="ConditionType.TextEqual"/>), matching exactly what C# matches. <paramref name="comparison"/> is
        /// <see cref="StringComparison.Ordinal"/> (case- and accent-sensitive) or <see cref="StringComparison.OrdinalIgnoreCase"/>
        /// (C#'s own case classes, accent-sensitive); the parser refuses every other one. <paramref name="bind"/> binds a
        /// value as a parameter and returns its name. NULL when the operand is NULL, like the <c>LIKE</c> it replaces.
        /// </summary>
        /// <remarks>
        /// The no-argument forms never come here: they render <c>LIKE</c> and follow the column's collation (TASK-869,
        /// the user's decision). The base refuses, so a provider without an exact form fails loudly instead of matching by
        /// its collation.
        /// </remarks>
        public virtual string StringMatch(ConditionType kind, string operand, string term, StringComparison comparison, Func<object, string> bind)
            => throw new NotSupportedException(
                $"Cannot translate a string comparison with StringComparison.{comparison} to SQL on this provider: it has no form "
                + "that matches exactly what C# matches. Compare in memory.");

        /// <summary>
        /// TASK-874 — refuses, before anything runs, a <c>LIKE</c> <paramref name="pattern"/> (escaped, with its wildcards)
        /// this provider cannot run; <paramref name="form"/> names the filter in the message. Every <c>LIKE</c> the framework
        /// renders on a provider with a limit passes here: the no-argument forms, and its <see cref="StringMatch"/>. The base accepts any length
        /// (SQLite, PostgreSQL and MySQL ran every term measured); SQL Server refuses past its 4000-character limit.
        /// </summary>
        public virtual void RequireLikePattern(string pattern, string form) { }

        /// <summary>TASK-869 — <c>operand COLLATE binary LIKE pattern ESCAPE '!'</c>, the ordinal LIKE every server but SQLite uses.</summary>
        protected static string CollatedLike(string operand, string collation, string pattern)
            => $"{operand} COLLATE {collation} LIKE {pattern} ESCAPE '{Connectors.SqlBuilderContext.LikeEscapeCharacter}'";
    }
}
