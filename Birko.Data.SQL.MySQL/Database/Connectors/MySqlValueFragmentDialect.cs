using System;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-860 — MySQL's spelling of the parts of a value fragment whose portable form computes something else there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MySQL's <c>/</c> always divides fractions, so an integer division (C# truncates toward zero) is <c>DIV</c>, which
    /// truncates the same way. Measured before: <c>x.Hits / 4 == 0</c> matched 1 of 5 rows, and an update
    /// <c>SET Hits = Hits / 4</c> stored 1 where C# writes 0 (the quotient 0.75 was rounded into the INT column).
    /// </para>
    /// <para>A <c>double</c> cast is <c>CAST(x AS DOUBLE)</c>: MySQL's <c>CAST</c> does not take <c>DOUBLE PRECISION</c>.</para>
    /// </remarks>
    public sealed class MySqlValueFragmentDialect : SqlValueFragmentDialect
    {
        public MySqlValueFragmentDialect(Func<string, int, MidpointRounding, string> round) : base(round) { }

        /// <inheritdoc />
        public override string IntegralDivide(string left, string right) => $"({left} DIV {right})";

        /// <inheritdoc />
        protected override string FloatingTypeName(Type type) => type == typeof(float) ? "FLOAT" : "DOUBLE";
    }
}
