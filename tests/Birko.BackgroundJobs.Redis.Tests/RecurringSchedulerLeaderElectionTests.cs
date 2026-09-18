using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.BackgroundJobs.Redis;
using Birko.Redis;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// TASK-237 — the leader-election half of <see cref="RecurringJobScheduler"/> proven on a <b>lease</b>
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// The offline proof in <c>Birko.BackgroundJobs.Tests</c> runs against an in-process lock double, which
/// cannot show what a real lease does. This suite and its PostgreSQL sibling exist because the two provider
/// kinds fail in <i>opposite</i> directions: a stuck session lock blocks handover forever, while an expired
/// lease lets two leaders coexist. A fix verified on one says nothing about the other.
/// </para>
/// <para>
/// Gated on <c>BIRKO_REDIS_HOST</c> — the behaviour under test is what a real server does with
/// <c>SET NX</c> and a renewed expiry, so there is nothing here that could honestly run offline.
/// </para>
/// </remarks>
public class RecurringSchedulerLeaderElectionTests
{
    private const string HostEnv = "BIRKO_REDIS_HOST";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();

    private static RedisSettings? LiveSettings()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host)) return null;
        return new RedisSettings(host, 6379, string.Empty, 0, false)
        {
            KeyPrefix = "birko:task237:" + Guid.NewGuid().ToString("N"),
        };
    }

    private static async Task<int> EnqueuedByAsync(InMemoryJobQueue queue, string queueName)
    {
        var pending = await queue.GetByStatusAsync(JobStatus.Pending, limit: 1000);
        return pending.Count(j => j.QueueName == queueName);
    }

    private static async Task SafeAsync(Task task)
    {
        try { await task; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Two_schedulers_on_one_redis_lock_enqueue_one_copy_not_two()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        var queue = new InMemoryJobQueue(_clock);
        await using var providerA = new RedisJobLockProvider(settings);
        await using var providerB = new RedisJobLockProvider(settings);

        var a = new RecurringJobScheduler(queue, _clock, providerA);
        var b = new RecurringJobScheduler(queue, _clock, providerB);
        a.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
        b.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

        using var cts = new CancellationTokenSource();
        var runA = a.RunAsync(cts.Token);
        var runB = b.RunAsync(cts.Token);
        await Task.Delay(Tick * 3);
        cts.Cancel();
        await Task.WhenAll(SafeAsync(runA), SafeAsync(runB));

        var fromA = await EnqueuedByAsync(queue, "worker-a");
        var fromB = await EnqueuedByAsync(queue, "worker-b");

        (fromA > 0).Should().NotBe(fromB > 0, "only the holder of the Redis lock may schedule");
        (fromA + fromB).Should().BeGreaterThan(0, "and it must actually schedule");
    }

    [Fact]
    public async Task The_lease_outlives_the_work_so_a_long_running_leader_is_not_displaced()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        // The lease-specific risk: the leader's key expires while it is still leading and the follower
        // walks in, producing exactly the duplication leader election was meant to remove. The heartbeat in
        // RedisJobLockProvider is what prevents it (TASK-232) — this asserts the scheduler benefits from it
        // across a run several times longer than the default lease would be if it were never renewed.
        var queue = new InMemoryJobQueue(_clock);
        await using var providerA = new RedisJobLockProvider(settings);
        await using var providerB = new RedisJobLockProvider(settings);

        var a = new RecurringJobScheduler(queue, _clock, providerA);
        var b = new RecurringJobScheduler(
            queue, _clock, providerB, leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
        a.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
        b.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

        using var cts = new CancellationTokenSource();
        var runA = a.RunAsync(cts.Token);
        var runB = b.RunAsync(cts.Token);
        await Task.Delay(Tick * 8);

        a.IsLeader.Should().BeTrue("the heartbeat must have kept the lease alive under a working leader");
        b.IsLeader.Should().BeFalse();
        (await EnqueuedByAsync(queue, "worker-b")).Should().Be(0,
            "a follower that knocked every 200ms across 8s must never have got in");

        cts.Cancel();
        await Task.WhenAll(SafeAsync(runA), SafeAsync(runB));
    }

    [Fact]
    public async Task A_stopped_leader_hands_over_without_waiting_for_the_lease_to_expire()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        // A session provider's backend frees the lock when the holder dies; Redis cannot, so the scheduler's
        // explicit release on exit is the only thing that makes handover fast rather than lease-length.
        var queue = new InMemoryJobQueue(_clock);
        await using var providerA = new RedisJobLockProvider(settings);
        await using var providerB = new RedisJobLockProvider(settings);

        var a = new RecurringJobScheduler(queue, _clock, providerA);
        var b = new RecurringJobScheduler(
            queue, _clock, providerB, leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
        a.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
        b.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();
        var runA = a.RunAsync(ctsA.Token);
        var runB = b.RunAsync(ctsB.Token);

        await Task.Delay(Tick * 2);
        a.IsLeader.Should().BeTrue();
        b.IsLeader.Should().BeFalse();

        ctsA.Cancel();
        await SafeAsync(runA);
        providerA.IsLocked.Should().BeFalse(
            "the release must happen even though the loop exited by cancellation — ReleaseAsync opens with " +
            "ThrowIfCancellationRequested, so passing the loop's own token would skip it every time");

        await Task.Delay(Tick * 3);
        b.IsLeader.Should().BeTrue(
            "handover took seconds, not the 30s the default lease would have taken to expire");

        ctsB.Cancel();
        await SafeAsync(runB);
    }
}

/// <summary>Placeholder job type: this suite asserts on what is enqueued, never on execution.</summary>
public class RecurringProbeJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
