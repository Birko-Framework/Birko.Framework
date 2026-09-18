using System;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Shared parser for human-readable time interval strings (e.g., "5 minutes", "1 hour", "00:15:00").
    /// Used by aggregation helpers across SQL, ElasticSearch, and LINQ fallback implementations.
    /// </summary>
    public static class TimeIntervalParser
    {
        /// <summary>
        /// Parses a time interval string into a <see cref="TimeSpan"/>.
        /// Supports TimeSpan format ("00:05:00", "1.00:00:00") and human-readable ("5 minutes", "1 hour", "30 s").
        /// Returns <see cref="TimeSpan.Zero"/> if the interval cannot be parsed.
        /// </summary>
        public static TimeSpan Parse(string interval)
        {
            if (string.IsNullOrWhiteSpace(interval))
            {
                return TimeSpan.Zero;
            }

            if (TimeSpan.TryParse(interval, out var ts))
            {
                return ts;
            }

            var parts = interval.Trim().Split(' ');
            if (parts.Length == 2 && double.TryParse(parts[0], out var value))
            {
                return parts[1].ToLowerInvariant() switch
                {
                    "second" or "seconds" or "s" => TimeSpan.FromSeconds(value),
                    "minute" or "minutes" or "m" or "min" or "mins" => TimeSpan.FromMinutes(value),
                    "hour" or "hours" or "h" or "hr" or "hrs" => TimeSpan.FromHours(value),
                    "day" or "days" or "d" => TimeSpan.FromDays(value),
                    _ => TimeSpan.Zero
                };
            }

            return TimeSpan.Zero;
        }

        /// <summary>
        /// Formats a time interval string into SQL-friendly format (e.g., "5 minutes", "1 hours").
        /// Handles both TimeSpan and human-readable input formats.
        /// </summary>
        public static string ToSqlInterval(string interval)
        {
            var ts = Parse(interval);
            if (ts > TimeSpan.Zero)
            {
                if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays} days";
                if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} hours";
                if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} minutes";
                return $"{(int)ts.TotalSeconds} seconds";
            }

            // Pass through as-is for human-readable format already in SQL-compatible form
            return interval;
        }
    }
}
