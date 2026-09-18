using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores
{
    /// <summary>
    /// CR-H097: AggregateHelper.ApplyTimeBucket wrote the truncated bucket value back onto each source
    /// entity via SetValue, so a read-only time-bucket aggregation permanently corrupted the caller's
    /// stored timestamps. The bucket is now computed on-the-fly without mutation.
    /// </summary>
    public class AggregateHelperTimeBucketTests
    {
        private class Reading : AbstractModel
        {
            public DateTime Timestamp { get; set; }
            public double Value { get; set; }
        }

        private static AggregateQuery<Reading> HourlyCount() => new()
        {
            TimeBucketInterval = "1 hour",
            TimeColumn = nameof(Reading.Timestamp),
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, nameof(Reading.Guid)) },
        };

        [Fact]
        public void TimeBucketAggregation_DoesNotMutateSourceTimestamps()
        {
            var t1 = new DateTime(2026, 1, 1, 10, 15, 0, DateTimeKind.Utc);
            var t2 = new DateTime(2026, 1, 1, 10, 45, 0, DateTimeKind.Utc);
            var t3 = new DateTime(2026, 1, 1, 11, 5, 0, DateTimeKind.Utc);

            var readings = new List<Reading>
            {
                new() { Guid = Guid.NewGuid(), Timestamp = t1, Value = 1 },
                new() { Guid = Guid.NewGuid(), Timestamp = t2, Value = 2 },
                new() { Guid = Guid.NewGuid(), Timestamp = t3, Value = 3 },
            };

            _ = AggregateHelper.LinqAggregate(readings, HourlyCount());

            // The source entities must be untouched — a read-only aggregation must not corrupt data.
            readings[0].Timestamp.Should().Be(t1);
            readings[1].Timestamp.Should().Be(t2);
            readings[2].Timestamp.Should().Be(t3);
        }

        [Fact]
        public void TimeBucketAggregation_StillGroupsByBucket()
        {
            var readings = new List<Reading>
            {
                new() { Guid = Guid.NewGuid(), Timestamp = new DateTime(2026, 1, 1, 10, 15, 0, DateTimeKind.Utc) },
                new() { Guid = Guid.NewGuid(), Timestamp = new DateTime(2026, 1, 1, 10, 45, 0, DateTimeKind.Utc) },
                new() { Guid = Guid.NewGuid(), Timestamp = new DateTime(2026, 1, 1, 11, 5, 0, DateTimeKind.Utc) },
            };

            var results = AggregateHelper.LinqAggregate(readings, HourlyCount());

            // Two hourly buckets: 10:00 (2 rows) and 11:00 (1 row).
            results.Should().HaveCount(2);
            var buckets = results
                .Select(r => (DateTime)r.Values["bucket_time"]!)
                .OrderBy(d => d)
                .ToList();
            buckets[0].Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
            buckets[1].Should().Be(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));
        }
    }
}
