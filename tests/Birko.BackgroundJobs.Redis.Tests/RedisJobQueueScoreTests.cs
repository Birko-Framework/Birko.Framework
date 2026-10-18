using FluentAssertions;
using System;
using System.Reflection;
using Xunit;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// Regression tests for CR-C02: the dequeue threshold used raw ticks while the sorted-set score
/// scaled time by 1e4, so the threshold was ~4 orders of magnitude larger than any score and every
/// future-scheduled job (including retry backoff) was dequeued immediately. Since TASK-663 the delayed-set
/// score is the due time alone (priority ranks due jobs in the ready set, asserted live in
/// <c>Task663RedisAtomicityLiveTests</c>), so these invariants are pure math on that score.
/// </summary>
public class RedisJobQueueScoreTests
{
    private static readonly MethodInfo Score = typeof(RedisJobQueue)
        .GetMethod("GetDueScore", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Threshold = typeof(RedisJobQueue)
        .GetMethod("GetDequeueThreshold", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static double GetScore(DateTime at) => (double)Score.Invoke(null, new object[] { at })!;
    private static double GetThreshold(DateTime now) => (double)Threshold.Invoke(null, new object[] { now })!;

    [Fact]
    public void DueJob_IsEligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        GetScore(now).Should().BeLessThanOrEqualTo(GetThreshold(now));
    }

    [Fact]
    public void FutureJob_IsNotEligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        var future = now.AddSeconds(30); // e.g. a retry backoff delay
        GetScore(future).Should().BeGreaterThan(GetThreshold(now));
    }

    [Fact]
    public void A_millisecond_in_the_future_is_not_yet_eligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        GetScore(now.AddMilliseconds(1)).Should().BeGreaterThan(GetThreshold(now));
    }
}
