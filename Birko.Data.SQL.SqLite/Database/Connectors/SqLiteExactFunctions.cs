using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// TASK-870 — the case folds and the integer arithmetic checks SQLite has no exact form of, computed by C# itself.
    /// Registered by <see cref="SqLiteDecimal.Register"/>, which every framework connection calls (rule 64); a statement that
    /// names one on a connection that did not register it fails with <c>no such function</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Case folds.</b> SQLite's <c>LOWER</c> / <c>UPPER</c> fold ASCII only. Measured (TASK-870's contract, 34 texts):
    /// <c>SET Folded = Code.ToLower()</c> stored a different string from C# for 18 texts (<c>Čučoriedka</c> kept its
    /// <c>Č</c>), and <c>x.Code.ToLower() == t</c> / <c>.Contains(t)</c> missed 26 to 35 of 68 cases. <see cref="CaseFoldFunction"/>
    /// is C#'s <c>ToLower</c> / <c>ToUpper</c> in the culture named by the call: the invariant one for <c>ToLowerInvariant</c>,
    /// and for <c>ToLower()</c> the caller's current culture, read when the statement is rendered.
    /// </para>
    /// <para>
    /// <b>Integer overflow.</b> An INTEGER is 64-bit on SQLite whatever the column declares, and a 64-bit overflow turns the
    /// result into a REAL. Measured: <c>SET Hits = Hits + 1</c> at <see cref="int.MaxValue"/> stored 2147483648 with no
    /// error and every later read of the row threw <see cref="OverflowException"/>; <c>SET Big = Big + 1</c> at
    /// <see cref="long.MaxValue"/> stored a REAL. C# wraps (or, checked, throws), and PostgreSQL, MySQL and SQL Server fail
    /// the statement. <see cref="CheckedIntegerFunction"/> raises an error when a value leaves its C# type's range, so the
    /// statement fails and writes nothing, as on the servers.
    /// </para>
    /// </remarks>
    public static class SqLiteExactFunctions
    {
        public const string CaseFoldFunction = "birko_case_fold";

        public const string CheckedIntegerFunction = "birko_checked_int";

        public static void Register(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            connection.CreateFunction<string?, long, string?, string?>(CaseFoldFunction,
                (value, upper, culture) => value == null ? null : Fold(value, upper != 0, culture),
                isDeterministic: true);
            connection.CreateFunction<object?, long, long, object?>(CheckedIntegerFunction, Checked, isDeterministic: true);
        }

        /// <summary>
        /// <c>birko_case_fold(operand, upper, culture)</c>: <paramref name="culture"/> is a culture name, or the empty string
        /// for the invariant culture. NULL when the operand is NULL.
        /// </summary>
        public static string CaseFoldCall(string operand, bool upper, CultureInfo culture)
            => string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2}, '{3}')",
                CaseFoldFunction, operand, upper ? 1 : 0, SqlLiteral.EscapeLiteral(culture.Name));

        /// <summary>
        /// <c>birko_checked_int(value, min, max)</c>: the value when it is an integer within [<paramref name="type"/>'s range],
        /// an error otherwise.
        /// </summary>
        public static string CheckedIntegerCall(string value, Type type)
        {
            var (min, max) = Range(Nullable.GetUnderlyingType(type) ?? type);
            return string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2}, {3})", CheckedIntegerFunction, value, min, max);
        }

        private static (long Min, long Max) Range(Type type) => Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
            TypeCode.Byte => (byte.MinValue, byte.MaxValue),
            TypeCode.Int16 => (short.MinValue, short.MaxValue),
            TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
            TypeCode.Int32 => (int.MinValue, int.MaxValue),
            TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
            TypeCode.Int64 => (long.MinValue, long.MaxValue),
            // A ulong past long.MaxValue has no INTEGER to be stored as on SQLite.
            TypeCode.UInt64 => (0, long.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "An integer type is expected."),
        };

        private static string Fold(string value, bool upper, string? culture)
        {
            var info = string.IsNullOrEmpty(culture) ? CultureInfo.InvariantCulture.TextInfo : CultureInfo.GetCultureInfo(culture).TextInfo;
            return upper ? info.ToUpper(value) : info.ToLower(value);
        }

        private static object? Checked(object? value, long min, long max)
        {
            switch (value)
            {
                case null:
                    return null;
                case long integer when integer >= min && integer <= max:
                    return integer;
                case long integer:
                    throw new OverflowException(string.Format(CultureInfo.InvariantCulture,
                        "Integer overflow: {0} is outside [{1}, {2}], the range of the column's C# type. C# would wrap it; the "
                        + "statement is failed instead, as PostgreSQL, MySQL and SQL Server fail it.", integer, min, max));
                default:
                    throw new OverflowException(string.Format(CultureInfo.InvariantCulture,
                        "Integer overflow: the result ({0}) left the 64-bit integer range, where SQLite turns it into a REAL. C# "
                        + "would wrap it; the statement is failed instead.", value));
            }
        }
    }
}
