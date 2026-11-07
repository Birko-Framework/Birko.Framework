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
    /// <see cref="Portable"/> calls at render time. The portable form is what every provider but SQLite runs.
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

        protected SqlValueFragmentDialect() { }

        /// <summary><c>left op right</c> computed in <paramref name="type"/> (the node's CLR type, maybe nullable).</summary>
        public virtual string Arithmetic(ExpressionType op, string sqlOperator, string left, string right, Type type)
            => $"({left} {sqlOperator} {right})";

        /// <summary>A constant's literal, or null for the portable one.</summary>
        public virtual string? Literal(object value) => null;

        /// <summary>A C# conversion of <paramref name="operand"/>; the portable form drops it.</summary>
        public virtual string Conversion(string operand, Type from, Type to) => operand;

        /// <summary>An operand of a comparison performed in <paramref name="comparedType"/>.</summary>
        public virtual string Compared(string operand, Type comparedType) => operand;
    }
}
