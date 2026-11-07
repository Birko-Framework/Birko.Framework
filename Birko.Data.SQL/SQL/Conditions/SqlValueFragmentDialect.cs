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
        public virtual string Arithmetic(ExpressionType op, string sqlOperator, string left, string right, Type type)
            => $"({left} {sqlOperator} {right})";

        /// <summary>A constant's literal, or null for the portable one.</summary>
        public virtual string? Literal(object value) => null;

        /// <summary>A C# conversion of <paramref name="operand"/>; the portable form drops it.</summary>
        public virtual string Conversion(string operand, Type from, Type to) => operand;

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
    }
}
