using System;
using System.Collections.Generic;
using System.Reflection;
using Birko.BackgroundJobs;
using Birko.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// Offline coverage for the Redis job queue (part of CR-H010). The connection is lazy, so the
/// descriptor hash round-trip and retry-backoff scoring can be exercised without a live Redis
/// server (the Lua dequeue path against a real server remains the documented infra gap).
/// </summary>
public class RedisJobQueueSerializationTests
{
    private static RedisJobQueue NewQueue() =>
        new(new RedisSettings("localhost") { KeyPrefix = "test:jobs" });

    private static readonly MethodInfo Serialize = typeof(RedisJobQueue)
        .GetMethod("SerializeDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo Deserialize = typeof(RedisJobQueue)
        .GetMethod("DeserializeDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo Score = typeof(RedisJobQueue)
        .GetMethod("GetQueueScore", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Threshold = typeof(RedisJobQueue)
        .GetMethod("GetDequeueThreshold", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static JobDescriptor RoundTrip(RedisJobQueue queue, JobDescriptor input)
    {
        var entries = (HashEntry[])Serialize.Invoke(queue, new object[] { input })!;
        return (JobDescriptor)Deserialize.Invoke(queue, new object[] { entries })!;
    }

    [Fact]
    public void SerializeDeserialize_PreservesAllFields()
    {
        var queue = NewQueue();
        var original = new JobDescriptor
        {
            Id = Guid.NewGuid(),
            JobType = "App.Jobs.Email",
            InputType = "App.Email",
            SerializedInput = "{\"to\":\"x@y.z\"}",
            QueueName = "emails",
            Priority = 7,
            MaxRetries = 5,
            Status = JobStatus.Scheduled,
            AttemptCount = 2,
            EnqueuedAt = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
            ScheduledAt = new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc),
            LastAttemptAt = new DateTime(2026, 7, 6, 10, 30, 0, DateTimeKind.Utc),
            LastError = "boom",
            Metadata = new Dictionary<string, string> { ["cid"] = "abc-123" }
        };

        var result = RoundTrip(queue, original);

        result.Id.Should().Be(original.Id);
        result.JobType.Should().Be(original.JobType);
        result.InputType.Should().Be(original.InputType);
        result.SerializedInput.Should().Be(original.SerializedInput);
        result.QueueName.Should().Be(original.QueueName);
        result.Priority.Should().Be(original.Priority);
        result.MaxRetries.Should().Be(original.MaxRetries);
        result.Status.Should().Be(JobStatus.Scheduled);
        result.AttemptCount.Should().Be(2);
        result.EnqueuedAt.Should().Be(original.EnqueuedAt);
        result.ScheduledAt.Should().Be(original.ScheduledAt);
        result.LastAttemptAt.Should().Be(original.LastAttemptAt);
        result.LastError.Should().Be("boom");
        result.Metadata.Should().ContainKey("cid").WhoseValue.Should().Be("abc-123");
    }

    [Fact]
    public void SerializeDeserialize_OmitsOptionalNullFields()
    {
        var queue = NewQueue();
        var minimal = new JobDescriptor { Id = Guid.NewGuid(), JobType = "t" };

        var result = RoundTrip(queue, minimal);

        result.ScheduledAt.Should().BeNull();
        result.LastError.Should().BeNull();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void RetryBackoff_ReschedulesAboveThreshold()
    {
        // A retried job scheduled at now + policy delay must NOT be immediately eligible.
        var policy = RetryPolicy.Default;
        var now = new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc);
        var reschedule = now.Add(policy.GetDelay(1));

        var score = (double)Score.Invoke(null, new object[] { 0, reschedule })!;
        var threshold = (double)Threshold.Invoke(null, new object[] { now })!;

        policy.GetDelay(1).Should().BeGreaterThan(TimeSpan.Zero);
        score.Should().BeGreaterThan(threshold, "a backed-off retry is not eligible until its delay elapses");
    }
}
