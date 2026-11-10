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
        /// Computes Sum over a set of boxed values for a given numeric property type, in the type
        /// <see cref="ResultType"/> names. Over no values it is 0, as LINQ's <c>Sum</c> over <c>int?</c> or <c>decimal?</c> is
        /// (TASK-886, the user's decision: it used to be null).
        /// </summary>
        public static object? ComputeSum(List<object?> values, Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!IsNumeric(underlying)) throw new NotSupportedException($"Sum is not defined for {underlying.Name}.");

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
        /// TASK-886: the Sum over no values (or over only nulls) of a property of <paramref name="propertyType"/>: 0 in the
        /// type <see cref="ResultType"/> names. The one producer a store that sums natively answers that edge with, where
        /// its server answers null or a 0 of another type. <see cref="ResultType"/> of a Sum is its own Sum type, so the
        /// Sum's result type may be passed as well.
        /// </summary>
        /// <exception cref="NotSupportedException"><paramref name="propertyType"/> is not numeric.</exception>
        public static object SumOfNoValues(Type propertyType) => ComputeSum(new List<object?>(), propertyType)!;

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

        /// <summary>
        /// The CLR type <see cref="ComputeAggregate"/> answers <paramref name="function"/> over a property of
        /// <paramref name="propertyType"/> in (when it answers a value). TASK-892: a store that aggregates natively converts
        /// its server's answer to this type, so every store answers in the same one: Count an <c>int</c>; Sum an
        /// <c>int</c>, <c>long</c>, <c>decimal</c>, <c>double</c> or <c>float</c> as the property is, a smaller integer as
        /// <c>long</c>, a <c>ulong</c> as <c>decimal</c>; Avg an integer as <c>double</c>, otherwise as the property is; Min and
        /// Max the property's own type. A Sum or Avg of a non-numeric type is refused, as <see cref="ComputeSum"/> refuses it.
        /// </summary>
        public static Type ResultType(AggregateFunction function, Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            switch (function)
            {
                case AggregateFunction.Count:
                    return typeof(int);
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    return underlying;
                case AggregateFunction.Sum:
                    if (!IsNumeric(underlying)) throw new NotSupportedException($"Sum is not defined for {underlying.Name}.");
                    if (IsSmallIntegral(underlying)) return typeof(long);
                    return underlying == typeof(ulong) ? typeof(decimal) : underlying;
                case AggregateFunction.Avg:
                    if (!IsNumeric(underlying)) throw new NotSupportedException($"Avg is not defined for {underlying.Name}.");
                    return underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float) ? underlying : typeof(double);
                default:
                    throw new NotSupportedException($"Aggregate function {function} is not supported.");
            }
        }

        private static bool IsNumeric(Type type)
            => type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(int)
               || type == typeof(long) || type == typeof(ulong) || IsSmallIntegral(type);

        private static bool IsSmallIntegral(Type type)
            => type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint);

        /// <summary>
        /// Resolves an aggregate function over a set of boxed values.
        /// Count returns group.Count() directly (values ignored).
        /// Min/Max compare by <see cref="ValueComparer"/>: a string in <paramref name="stringComparison"/>'s order, any other
        /// value by <see cref="Comparer{T}.Default"/>.
        /// </summary>
        /// <remarks>
        /// TASK-907: a string Min / Max used <see cref="Comparer{T}.Default"/>, which is the current culture's order, so the
        /// answer depended on the machine (sk-SK: <c>"apple"</c> below <c>"Banana"</c>) and no store answered it. It is
        /// <see cref="StringComparison.Ordinal"/> unless the query asks for another order
        /// (<see cref="AggregateQuery{T}.StringComparison"/>).
        /// </remarks>
        public static object? ComputeAggregate(
            AggregateFunction function,
            List<object?> values,
            Type propertyType,
            int groupCount,
            StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (function == AggregateFunction.Count) return groupCount;

            var nonNull = values.Where(v => v != null).ToList();
            return function switch
            {
                AggregateFunction.Sum => ComputeSum(nonNull, propertyType),
                AggregateFunction.Avg => ComputeAvg(nonNull, propertyType),
                AggregateFunction.Min => nonNull.Count > 0 ? nonNull.Min(ValueComparer(stringComparison)) : null,
                AggregateFunction.Max => nonNull.Count > 0 ? nonNull.Max(ValueComparer(stringComparison)) : null,
                _ => null
            };
        }

        /// <summary>
        /// TASK-907: the order a Min / Max takes its values in: two strings in <paramref name="stringComparison"/>'s order
        /// (<see cref="StringComparer.FromComparison"/>), anything else by <see cref="Comparer{T}.Default"/>.
        /// </summary>
        public static IComparer<object?> ValueComparer(StringComparison stringComparison)
            => new ExtremeComparer(StringComparer.FromComparison(stringComparison));

        /// <summary>
        /// TASK-907: the refusal of a provider that cannot take a Min / Max of a string in <paramref name="comparison"/>'s
        /// order. The one producer of its text, so every provider names the comparison and the member the same way.
        /// </summary>
        /// <param name="provider">What refuses, for the message: e.g. <c>"MongoDB aggregate of Order"</c>.</param>
        /// <param name="function">The Min or Max.</param>
        /// <param name="member">The string member, e.g. <c>Order.Name</c>.</param>
        /// <param name="comparison">The order the query asked for.</param>
        /// <param name="supported">The order this provider does take, for the message; null where it takes none exactly.</param>
        public static NotSupportedException StringOrderNotSupported(
            string provider, AggregateFunction function, string member, StringComparison comparison, string? supported = "Ordinal")
            => new($"{provider} refused: {function} of the string {member} in {comparison} order. "
                   + (supported == null
                       ? "This provider has no string Min / Max in an exact order. "
                       : $"This provider takes a string Min / Max only in {supported} order. ")
                   + "It does not answer in the database's own order: read the rows and aggregate them in memory"
                   + (supported == null ? "" : $", or aggregate in {supported} order")
                   + ". (TASK-907)");

        /// <summary>
        /// TASK-907: the refusal of a provider that orders strings by code point, for a group whose values hold both a
        /// character above U+FFFF and one in U+E000–U+FFFF: the one case where code-point order and Ordinal (UTF-16 code
        /// units, which put the surrogate pair D800–DFFF below U+E000) disagree. Any other group is answered exactly.
        /// </summary>
        /// <param name="provider">What refuses, for the message.</param>
        /// <param name="function">The Min or Max.</param>
        /// <param name="member">The string member or view property.</param>
        public static NotSupportedException MixedAstralNotSupported(string provider, AggregateFunction function, string member)
            => new($"{provider} refused: {function} of the string {member} over a group holding both a character above U+FFFF "
                   + "and one in U+E000–U+FFFF. This provider orders strings by code point, which puts the first above the "
                   + "second where Ordinal (UTF-16) puts it below, and it does not answer in its own order. Read the rows and "
                   + "aggregate them in memory. (TASK-907)");

        /// <summary>TASK-907: a regular expression character class of U+E000–U+FFFF, written with the characters themselves, so PCRE (MongoDB), Lucene (Elasticsearch) and Cosmos DB read it alike.</summary>
        public const string HighBmpCharacters = "[\uE000-\uFFFF]";

        /// <summary>TASK-907: a regular expression character class of U+10000–U+10FFFF, written with the characters themselves (a surrogate pair each in C#, one code point in UTF-8).</summary>
        public const string AstralCharacters = "[\U00010000-\U0010FFFF]";

        private sealed class ExtremeComparer : IComparer<object?>
        {
            private readonly StringComparer _strings;
            public ExtremeComparer(StringComparer strings) => _strings = strings;

            public int Compare(object? x, object? y)
                => x is string a && y is string b ? _strings.Compare(a, b) : Comparer<object?>.Default.Compare(x, y);
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
