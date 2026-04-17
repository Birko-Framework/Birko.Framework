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
        /// Applies time-bucket truncation to a property on each item, returning new items
        /// with the time property replaced by its bucketed value.
        /// Does not mutate originals — creates shallow copies when bucketing is needed.
        /// </summary>
        public static IEnumerable<(T Item, DateTime BucketTime)> BucketByTime<T>(
            IEnumerable<T> items,
            Func<T, DateTime> timeAccessor,
            long bucketTicks)
        {
            foreach (var item in items)
            {
                var dt = timeAccessor(item);
                var bucketTime = TruncateToBucket(dt, bucketTicks);
                yield return (item, bucketTime);
            }
        }

        /// <summary>
        /// Computes Sum over a set of boxed values for a given numeric property type.
        /// Returns null if values is empty.
        /// </summary>
        public static object? ComputeSum(List<object?> values, Type propertyType)
        {
            if (values.Count == 0) return null;
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (underlying == typeof(decimal)) return values.Cast<decimal?>().Sum();
            if (underlying == typeof(double)) return values.Cast<double?>().Sum();
            if (underlying == typeof(float)) return values.Cast<float?>().Sum();
            if (underlying == typeof(int)) return values.Cast<int?>().Sum();
            if (underlying == typeof(long)) return values.Cast<long?>().Sum();
            return null;
        }

        /// <summary>
        /// Computes Average over a set of boxed values for a given numeric property type.
        /// Returns null if values is empty.
        /// </summary>
        public static object? ComputeAvg(List<object?> values, Type propertyType)
        {
            if (values.Count == 0) return null;
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (underlying == typeof(decimal)) return values.Cast<decimal?>().Average();
            if (underlying == typeof(double)) return values.Cast<double?>().Average();
            if (underlying == typeof(float)) return values.Cast<float?>().Average();
            if (underlying == typeof(int)) return values.Cast<int?>().Average();
            if (underlying == typeof(long)) return values.Cast<long?>().Average();
            return null;
        }

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
