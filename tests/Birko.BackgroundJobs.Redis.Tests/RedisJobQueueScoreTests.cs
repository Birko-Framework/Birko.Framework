using FluentAssertions;
using System;
using System.Reflection;
using Xunit;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// Regression tests for CR-C02: the dequeue threshold used raw ticks while the sorted-set score
/// scaled time by 1e4, so the threshold was ~4 orders of magnitude larger than any score and every
/// future-scheduled job (including retry backoff) was dequeued immediately. The score is now
/// time-based (eligibility correct) with a bounded priority tiebreaker that cannot pull a
/// future-scheduled job across the threshold. These invariants are pure math and tested without a
/// live Redis server (the Lua dequeue path itself is the documented infra gap).
/// </summary>
public class RedisJobQueueScoreTests
{
    private static readonly MethodInfo Score = typeof(RedisJobQueue)
        .GetMethod("GetQueueScore", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Threshold = typeof(RedisJobQueue)
        .GetMethod("GetDequeueThreshold", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static double GetScore(int priority, DateTime at) => (double)Score.Invoke(null, new object[] { priority, at })!;
    private static double GetThreshold(DateTime now) => (double)Threshold.Invoke(null, new object[] { now })!;

    [Fact]
    public void DueJob_IsEligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        GetScore(0, now).Should().BeLessThanOrEqualTo(GetThreshold(now));
    }

    [Fact]
    public void FutureJob_IsNotEligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        var future = now.AddSeconds(30); // e.g. a retry backoff delay
        GetScore(0, future).Should().BeGreaterThan(GetThreshold(now));
    }

    // The key CR-C02 property: a HIGH-priority future job must still wait — the priority tiebreaker
    // is bounded so it can never cross the time-eligibility threshold.
    [Fact]
    public void HighPriorityFutureJob_StillNotEligible()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        var future = now.AddSeconds(30);
        GetScore(999, future).Should().BeGreaterThan(GetThreshold(now));
    }

    [Fact]
    public void HigherPriority_DequeuesFirst_AmongReadyJobs()
    {
        var now = new DateTime(2026, 07, 06, 12, 0, 0, DateTimeKind.Utc);
        // Lower score = dequeued first; higher priority must score lower.
        GetScore(10, now).Should().BeLessThan(GetScore(0, now));
        GetScore(10, now).Should().BeLessThanOrEqualTo(GetThreshold(now));
    }
}
