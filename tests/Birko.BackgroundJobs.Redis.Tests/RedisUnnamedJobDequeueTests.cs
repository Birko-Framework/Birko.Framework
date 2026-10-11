using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// FIELD-028 (TASK-561) — <c>RedisJobQueue</c> stores a job whose <c>QueueName</c> is null under the literal
/// <c>{prefix}:queue:default</c> key, and a named <c>DequeueAsync(queueName)</c> read only <c>queue:{queueName}</c>.
/// <c>JobDispatcher</c> leaves <c>QueueName</c> null and <c>BackgroundJobProcessor</c> dequeues
/// <c>JobQueueOptions.DefaultQueueName</c>, so with <c>DefaultQueueName = "work"</c> no dispatcher-enqueued job ever
/// ran on Redis: it sat <c>Pending</c> forever. Every sibling backend admits an unnamed job on any named dequeue
/// (<c>queueName == null || j.QueueName == null || j.QueueName == queueName</c>); Redis now does too.
///
/// <para>Observed state: the job each dequeue returns and its stored status. The claim is a Lua script, so these run
/// against a real server — gated on <c>BIRKO_REDIS_HOST</c>, a reported skip (or, under <c>BIRKO_REQUIRE_LIVE</c>, a
/// failure) when absent.</para>
/// </summary>
public class RedisUnnamedJobDequeueTests
{
    private const string HostEnv = "BIRKO_REDIS_HOST";

    private readonly ITestOutputHelper _output;

    public RedisUnnamedJobDequeueTests(ITestOutputHelper output) => _output = output;

    private RedisJobQueue? LiveQueue()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host))
        {
            var message = $"SKIPPED: no live Redis. Set {HostEnv} to exercise this test; "
                        + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            {
                throw new InvalidOperationException(message);
            }
            return null;
        }

        return new RedisJobQueue(new RedisSettings(host, 6379, string.Empty, 0, false)
        {
            KeyPrefix = "birko:task561:" + Guid.NewGuid().ToString("N"),
        });
    }

    private static JobDescriptor Job(string? queue, int priority = 0, DateTime? enqueuedAt = null) => new()
    {
        JobType = "T",
        QueueName = queue,
        Priority = priority,
        EnqueuedAt = enqueuedAt ?? DateTime.UtcNow.AddSeconds(-10),
    };

    [Fact]
    public async Task A_named_dequeue_serves_an_unnamed_job()
    {
        using var queue = LiveQueue();
        if (queue == null) return;
        var job = Job(queue: null);
        await queue.EnqueueAsync(job);

        var claimed = await queue.DequeueAsync("work");

        claimed.Should().NotBeNull("an unnamed job is admitted by any named dequeue, as on every sibling backend");
        claimed!.Guid.Should().Be(job.Guid);
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Processing);
        (await queue.DequeueAsync("default")).Should().BeNull("a claimed job is claimed once");
    }

    [Fact]
    public async Task A_named_dequeue_takes_the_better_scored_of_its_own_and_an_unnamed_job()
    {
        using var queue = LiveQueue();
        if (queue == null) return;
        // Redis scores by due time with priority as a bounded tie-break, so the same time lets priority decide.
        var at = DateTime.UtcNow.AddSeconds(-5);
        var named = Job("work", priority: 0, enqueuedAt: at);
        var unnamedUrgent = Job(null, priority: 10, enqueuedAt: at);
        await queue.EnqueueAsync(named);
        await queue.EnqueueAsync(unnamedUrgent);

        (await queue.DequeueAsync("work"))!.Guid.Should().Be(unnamedUrgent.Guid, "the better score wins across both keys");
        (await queue.DequeueAsync("work"))!.Guid.Should().Be(named.Guid);
        (await queue.DequeueAsync("work")).Should().BeNull();
    }

    [Fact]
    public async Task An_older_named_job_is_taken_before_a_newer_unnamed_one()
    {
        using var queue = LiveQueue();
        if (queue == null) return;
        var named = Job("work", enqueuedAt: DateTime.UtcNow.AddSeconds(-20));
        var unnamed = Job(null, enqueuedAt: DateTime.UtcNow.AddSeconds(-2));
        await queue.EnqueueAsync(unnamed);
        await queue.EnqueueAsync(named);

        (await queue.DequeueAsync("work"))!.Guid.Should().Be(named.Guid, "the unnamed candidate wins only on a better score");
        (await queue.DequeueAsync("work"))!.Guid.Should().Be(unnamed.Guid);
    }

    [Fact]
    public async Task An_unnamed_job_behind_explicitly_default_named_jobs_is_still_served()
    {
        // Unnamed jobs share the `default` key with jobs explicitly named "default"; a "work" dequeue must step past
        // those to the unnamed one rather than give up at the head of the key.
        using var queue = LiveQueue();
        if (queue == null) return;
        var older = DateTime.UtcNow.AddSeconds(-30);
        await queue.EnqueueAsync(Job("default", enqueuedAt: older));
        await queue.EnqueueAsync(Job("default", enqueuedAt: older.AddSeconds(1)));
        var unnamed = Job(null, enqueuedAt: older.AddSeconds(2));
        await queue.EnqueueAsync(unnamed);

        (await queue.DequeueAsync("work"))!.Guid.Should().Be(unnamed.Guid);
    }

    [Fact]
    public async Task A_job_explicitly_named_default_is_not_served_to_another_queue()
    {
        // Contract pin: a named job stays on its own queue (the documented contract on every backend).
        using var queue = LiveQueue();
        if (queue == null) return;
        var job = Job("default");
        await queue.EnqueueAsync(job);

        (await queue.DequeueAsync("work")).Should().BeNull();
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Pending);
        (await queue.DequeueAsync("default"))!.Guid.Should().Be(job.Guid);
    }

    [Fact]
    public async Task A_job_named_for_another_queue_stays_invisible()
    {
        // Contract pin.
        using var queue = LiveQueue();
        if (queue == null) return;
        await queue.EnqueueAsync(Job("reports"));

        (await queue.DequeueAsync("work")).Should().BeNull();
    }

    [Fact]
    public async Task The_default_queue_still_serves_unnamed_jobs()
    {
        // Contract pin: the shipped configuration, where both keys coincide.
        using var queue = LiveQueue();
        if (queue == null) return;
        var job = Job(null);
        await queue.EnqueueAsync(job);

        (await queue.DequeueAsync("default"))!.Guid.Should().Be(job.Guid);
    }

    [Fact]
    public async Task A_future_unnamed_job_is_not_served_early()
    {
        // Contract pin: the due-time threshold applies to the unnamed key too.
        using var queue = LiveQueue();
        if (queue == null) return;
        var job = Job(null);
        job.ScheduledAt = DateTime.UtcNow.AddHours(1);
        await queue.EnqueueAsync(job);

        (await queue.DequeueAsync("work")).Should().BeNull();
    }
}
