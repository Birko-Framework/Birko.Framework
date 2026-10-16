using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Pure computation helpers for aggregation — no AbstractModel constraint.
    /// Usable by any in-memory store or provider that needs bucketing / aggregate math.
    /// </summary>
    public static class AggregateMath
    {
        /// <summary>
        /// Truncates a DateTime to the nearest lower bucket boundary.
        /// </summary>
        public static DateTime TruncateToBucket(DateTime dt, long bucketTicks)
        {
            return new DateTime((dt.Ticks / bucketTicks) * bucketTicks, dt.Kind);
        }

        /// <summary>
        /// Computes Sum over a set of boxed values for a given numeric property type.
        /// Returns null if values is empty.
        /// </summary>
        public static object? ComputeSum(List<object?> values, Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!IsNumeric(underlying)) throw new NotSupportedException($"Sum is not defined for {underlying.Name}.");
            if (values.Count == 0) return null;

            if (underlying == typeof(decimal)) return values.Cast<decimal?>().Sum();
            if (underlying == typeof(double)) return values.Cast<double?>().Sum();
            if (underlying == typeof(float)) return values.Cast<float?>().Sum();
            if (underlying == typeof(int)) return values.Cast<int?>().Sum();
            if (underlying == typeof(long)) return values.Cast<long?>().Sum();
            // TASK-152 (SH-M385): the other integral types answered null, where SQL SUM and ES sum answer a number. They
            // sum as long (ulong as decimal, which holds any ulong sum); a non-numeric type is refused, never null.
            if (IsSmallIntegral(underlying)) return values.Sum(v => Convert.ToInt64(v));
            if (underlying == typeof(ulong)) return values.Sum(v => Convert.ToDecimal(v));
            throw new NotSupportedException($"Sum is not defined for {underlying.Name}.");
        }

        /// <summary>
        /// Computes Average over a set of boxed values for a given numeric property type.
        /// Returns null if values is empty.
        /// </summary>
        public static object? ComputeAvg(List<object?> values, Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!IsNumeric(underlying)) throw new NotSupportedException($"Avg is not defined for {underlying.Name}.");
            if (values.Count == 0) return null;

            if (underlying == typeof(decimal)) return values.Cast<decimal?>().Average();
            if (underlying == typeof(double)) return values.Cast<double?>().Average();
            if (underlying == typeof(float)) return values.Cast<float?>().Average();
            if (underlying == typeof(int)) return values.Cast<int?>().Average();
            if (underlying == typeof(long)) return values.Cast<long?>().Average();
            // SH-M385: as ComputeSum; an integral average is a double, as int and long already are.
            if (IsSmallIntegral(underlying)) return values.Average(v => Convert.ToDouble(v));
            if (underlying == typeof(ulong)) return values.Average(v => Convert.ToDouble(v));
            throw new NotSupportedException($"Avg is not defined for {underlying.Name}.");
        }

        private static bool IsNumeric(Type type)
            => type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(int)
               || type == typeof(long) || type == typeof(ulong) || IsSmallIntegral(type);

        private static bool IsSmallIntegral(Type type)
            => type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint);

        /// <summary>
        /// Resolves an aggregate function over a set of boxed values.
        /// Count returns group.Count() directly (values ignored).
        /// Min/Max use Comparer.Default.
        /// </summary>
        public static object? ComputeAggregate(
            AggregateFunction function,
            List<object?> values,
            Type propertyType,
            int groupCount)
        {
            if (function == AggregateFunction.Count) return groupCount;

            var nonNull = values.Where(v => v != null).ToList();
            return function switch
            {
                AggregateFunction.Sum => ComputeSum(nonNull, propertyType),
                AggregateFunction.Avg => ComputeAvg(nonNull, propertyType),
                AggregateFunction.Min => nonNull.Count > 0 ? nonNull.Min() : null,
                AggregateFunction.Max => nonNull.Count > 0 ? nonNull.Max() : null,
                _ => null
            };
        }

        /// <summary>
        /// Reads all public instance properties of an object into a dictionary.
        /// </summary>
        public static Dictionary<string, object?> ToValueDictionary(object item)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                dict[prop.Name] = prop.GetValue(item);
            }
            return dict;
        }
    }
}
