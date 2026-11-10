using System;
using System.Globalization;

namespace Birko.Data.SQL.Fields
{
    /// <summary>
    /// The one reading rule for a numeric aggregate (TASK-887, shared by TASK-892): the value a server answered, converted
    /// exactly to the type it is read into, or refused. A view reads into its property's type
    /// (<c>FunctionField.Read</c>); the store's <c>Aggregate</c> reads into the type <c>AggregateHelper</c> answers in
    /// (<see cref="Birko.Data.Stores.AggregateMath.ResultType"/>).
    /// </summary>
    /// <remarks>
    /// The type a server answers is not the column's: a SUM over a BIGINT is NUMERIC on PostgreSQL and DECIMAL on MySQL, a
    /// SUM over a SMALLINT is BIGINT, INT or DECIMAL depending on the provider, a COUNT is a 64-bit integer everywhere but
    /// SQL Server, and SQLite keeps a decimal as text. Measured before TASK-892: the store's <c>Aggregate</c> handed those
    /// back as they came, so its values' types differed per provider and from every other store's.
    /// </remarks>
    public static class AggregateValue
    {
        /// <summary>
        /// Converts <paramref name="raw"/> to <paramref name="declared"/> exactly. A value it cannot hold exactly (a fraction
        /// in an integral type, NULL in a non-nullable one) is refused with <see cref="InvalidCastException"/>, one out of
        /// range with <see cref="OverflowException"/>.
        /// </summary>
        /// <param name="aggregate">The aggregate as rendered, for messages (<c>AVG(Hits)</c>).</param>
        /// <param name="target">What it is read into, for messages (<c>view property 'V.Total'</c>).</param>
        public static object? Convert(object? raw, Type declared, string aggregate, string target)
        {
            var underlying = Nullable.GetUnderlyingType(declared);
            var type = underlying ?? declared;
            if (raw == null || raw is DBNull)
            {
                if (!declared.IsValueType || underlying != null)
                {
                    return null;
                }
                throw new InvalidCastException(
                    $"{aggregate} answered NULL, and {target} ({declared.Name}) cannot hold NULL. "
                    + $"Declare it {type.Name}? to read it. (TASK-887)");
            }

            var numeric = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            if (!IsNumeric(numeric))
            {
                throw new InvalidCastException(
                    $"{aggregate} is read as a number, and {target} is {declared.Name}. (TASK-887)");
            }

            // SQLite keeps a decimal column as text, so its MIN / MAX, and its exact SUM / AVG, come back as that text.
            var value = raw is string text ? decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) : raw;
            object converted;
            try
            {
                converted = IsIntegral(numeric)
                    ? ToIntegral(value, numeric, aggregate, target, declared)
                    : System.Convert.ChangeType(value, numeric, CultureInfo.InvariantCulture);
            }
            catch (OverflowException ex)
            {
                throw new OverflowException(
                    $"{aggregate} answered {Show(raw)}, which is out of range for {target} ({declared.Name}). (TASK-887)", ex);
            }
            return type.IsEnum ? Enum.ToObject(type, converted) : converted;
        }

        /// <summary>True for a type <see cref="Convert"/> reads a number into: an integer, <c>decimal</c>, <c>double</c>, <c>float</c>.</summary>
        public static bool IsNumeric(Type type)
            => IsIntegral(type) || type == typeof(decimal) || type == typeof(double) || type == typeof(float);

        private static object ToIntegral(object value, Type numeric, string aggregate, string target, Type declared)
        {
            var whole = value switch
            {
                decimal m => m == decimal.Truncate(m),
                double d => !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Truncate(d),
                float f => !float.IsNaN(f) && !float.IsInfinity(f) && f == MathF.Truncate(f),
                _ => IsIntegral(value.GetType()),
            };
            if (!whole)
            {
                throw new InvalidCastException(
                    $"{aggregate} answered {Show(value)}, which is not a whole number, and {target} "
                    + $"is {declared.Name}. Declare it double or decimal to read the fraction. (TASK-887)");
            }
            return System.Convert.ChangeType(value, numeric, CultureInfo.InvariantCulture);
        }

        private static string Show(object value)
            => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;

        private static bool IsIntegral(Type type)
            => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
               || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    }
}
