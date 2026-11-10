using System;

namespace Birko.Data.SQL.Conditions
{
    /// <summary>What a C# conversion over the row is in SQL (TASK-860).</summary>
    public enum SqlConversionKind
    {
        /// <summary>No SQL needed: a nullable lift, an enum to its integer, a widening integer cast, a reference conversion.</summary>
        Transparent,

        /// <summary>
        /// An integer to <c>decimal</c>, <c>double</c> or <c>float</c>: exact in a comparison, and an explicit cast inside a
        /// value fragment, without which SQL divides two integers.
        /// </summary>
        Widening,

        /// <summary>No exact SQL form on every provider: refused.</summary>
        Lossy,
    }

    /// <summary>TASK-860 — the one classification of a conversion the SQL parsers meet.</summary>
    public static class SqlConversion
    {
        public static SqlConversionKind Classify(Type from, Type to)
        {
            if (from == to) return SqlConversionKind.Transparent;
            var source = Nullable.GetUnderlyingType(from) ?? from;
            var target = Nullable.GetUnderlyingType(to) ?? to;
            if (source == target) return SqlConversionKind.Transparent;

            if (!source.IsValueType || !target.IsValueType)
                // Boxing, or a reference conversion to a base type or an interface (Convert(x, IEntity)).
                return target.IsAssignableFrom(source) ? SqlConversionKind.Transparent : SqlConversionKind.Lossy;

            // An enum is stored as its integer (AbstractField maps it to an INTEGER column).
            if (source.IsEnum) source = Enum.GetUnderlyingType(source);
            if (target.IsEnum) target = Enum.GetUnderlyingType(target);
            if (source == target) return SqlConversionKind.Transparent;

            if (IntegralRange(source) is { } from_ && IntegralRange(target) is { } to_)
                return from_.Min >= to_.Min && from_.Max <= to_.Max ? SqlConversionKind.Transparent : SqlConversionKind.Lossy;
            if (IntegralRange(source) is { } integral)
            {
                // An integer to a binary fraction is the compiler's own implicit conversion (`x.Big > 2.5`, `x.F > x.Hits`).
                // Past the mantissa (2^53 for a double, 2^24 for a float) it rounds, to the nearest value, in C# and in
                // SQL's CAST alike.
                if (target == typeof(decimal) || target == typeof(double) || target == typeof(float))
                    return SqlConversionKind.Widening;
                return SqlConversionKind.Lossy;
            }
            return source == typeof(float) && target == typeof(double) ? SqlConversionKind.Transparent : SqlConversionKind.Lossy;
        }

        /// <summary>
        /// TASK-860 — an integer cast to a narrower integer (<c>(short)(x.Sh + 1)</c>). C# wraps on overflow; a SQL value
        /// that does not fit the column it is stored in fails the statement, so an update SET may narrow to its column's type
        /// (<c>SqlValueFragmentDialect.NarrowedInteger</c>, TASK-890). A filter never may.
        /// </summary>
        public static bool IsIntegralNarrowing(Type from, Type to)
        {
            var source = Nullable.GetUnderlyingType(from) ?? from;
            var target = Nullable.GetUnderlyingType(to) ?? to;
            if (source.IsEnum) source = Enum.GetUnderlyingType(source);
            if (target.IsEnum) target = Enum.GetUnderlyingType(target);
            return IntegralRange(source) is { } f && IntegralRange(target) is { } t && (f.Min < t.Min || f.Max > t.Max);
        }

        /// <summary>True for an integer type (not <c>char</c>, not <c>bool</c>).</summary>
        public static bool IsIntegral(Type type) => IntegralRange(Nullable.GetUnderlyingType(type) ?? type) != null;

        private static (decimal Min, decimal Max, int Bits)? IntegralRange(Type type) => Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue, 8),
            TypeCode.Byte => (byte.MinValue, byte.MaxValue, 8),
            TypeCode.Int16 => (short.MinValue, short.MaxValue, 16),
            TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue, 16),
            TypeCode.Int32 => (int.MinValue, int.MaxValue, 32),
            TypeCode.UInt32 => (uint.MinValue, uint.MaxValue, 32),
            TypeCode.Int64 => (long.MinValue, long.MaxValue, 64),
            TypeCode.UInt64 => (ulong.MinValue, ulong.MaxValue, 64),
            _ => null,
        };

        /// <summary>Decimal digits an integer type needs: the precision of an exact <c>DECIMAL(p, 0)</c> cast.</summary>
        public static int DecimalDigits(Type type) => Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
        {
            TypeCode.SByte or TypeCode.Byte => 3,
            TypeCode.Int16 or TypeCode.UInt16 => 5,
            TypeCode.Int32 or TypeCode.UInt32 => 10,
            _ => 20,
        };
    }
}
