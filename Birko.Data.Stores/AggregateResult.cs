using System;
using System.Collections.Generic;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A single row from an aggregation query result.
    /// Dictionary-backed with typed convenience accessors.
    /// </summary>
    public sealed class AggregateResult
    {
        private readonly Dictionary<string, object?> _values;

        public AggregateResult(Dictionary<string, object?> values)
        {
            _values = values;
        }

        /// <summary>All key-value pairs in this result row.</summary>
        public IReadOnlyDictionary<string, object?> Values => _values;

        /// <summary>
        /// Gets a typed value by alias name. Returns default if not found or conversion fails.
        /// </summary>
        public TVal? GetValue<TVal>(string alias)
        {
            if (!_values.TryGetValue(alias, out var val) || val == null)
                return default;

            if (val is TVal typed)
                return typed;

            try
            {
                return (TVal?)Convert.ChangeType(val, typeof(TVal));
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Gets the bucket_time value if present (for time-bucketed queries).
        /// </summary>
        public DateTime? GetBucketTime() => GetValue<DateTime>("bucket_time");
    }
}
