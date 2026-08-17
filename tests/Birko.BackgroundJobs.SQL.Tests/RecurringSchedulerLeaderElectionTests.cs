using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.BackgroundJobs.SQL;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// TASK-237 — the leader-election half of <see cref="RecurringJobScheduler"/> proven on a <b>session</b>
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// Sibling of the Redis suite, and separate from it on purpose: the two provider kinds fail in opposite
/// directions — a stuck session lock blocks handover forever, while an expired lease lets two leaders
/// coexist — so verifying one says nothing about the other.
/// </para>
/// <para>
/// PostgreSQL rather than the SQLite this project otherwise uses, because <c>SqlJobLockProvider</c> returns
/// <c>false</c> on SQLite by design: there is no portable cross-connection advisory lock. Under SQLite both
/// schedulers would be followers and "one enqueue, not two" would pass while nothing was scheduled at all.
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).
/// </para>
/// </remarks>
public class RecurringSchedulerLeaderElectionTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    private static bool Server => !string.IsNullOrWhiteSpace(Host);

    private static PasswordSettings Settings() =>
        new PostgreSqlSettings(Host!, Database, User, Password) { Port = Port };

    private static string NewLockName() => "task237-" + Guid.NewGuid().ToString("N");

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
    public async Task Two_schedulers_on_one_advisory_lock_enqueue_one_copy_not_two()
    {
        if (!Server) return;

        var queue = new InMemoryJobQueue(_clock);
        var lockName = NewLockName();

        await using var providerA = new SqlJobLockProvider<PostgreSQLConnector>(Settings());
        await using var providerB = new SqlJobLockProvider<PostgreSQLConnector>(Settings());

        var a = new RecurringJobScheduler(queue, _clock, providerA, lockName);
        var b = new RecurringJobScheduler(queue, _clock, providerB, lockName);
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

        (fromA > 0).Should().NotBe(fromB > 0, "only the holder of the advisory lock may schedule");
        (fromA + fromB).Should().BeGreaterThan(0,
            "and it must actually schedule — on a dialect without advisory locks both would be followers " +
            "and this test would otherwise pass while doing nothing");
    }

    [Fact]
    public async Task A_stopped_leader_releases_so_the_follower_takes_over()
    {
        if (!Server) return;

        // The session-lock failure mode is the opposite of the lease's: nothing expires, so a lock that is
        // not released explicitly is held until the connection drops. This asserts the scheduler's release
        // on exit — with CancellationToken.None, since the loop exits precisely because its own token was
        // cancelled and ReleaseAsync would otherwise refuse.
        var queue = new InMemoryJobQueue(_clock);
        var lockName = NewLockName();

        await using var providerA = new SqlJobLockProvider<PostgreSQLConnector>(Settings());
        await using var providerB = new SqlJobLockProvider<PostgreSQLConnector>(Settings());

        var a = new RecurringJobScheduler(queue, _clock, providerA, lockName);
        var b = new RecurringJobScheduler(
            queue, _clock, providerB, lockName, leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
        a.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
        b.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();
        var runA = a.RunAsync(ctsA.Token);
        var runB = b.RunAsync(ctsB.Token);

        await Task.Delay(Tick * 2);
        a.IsLeader.Should().BeTrue();
        b.IsLeader.Should().BeFalse();
        var bBefore = await EnqueuedByAsync(queue, "worker-b");

        ctsA.Cancel();
        await SafeAsync(runA);
        providerA.IsLocked.Should().BeFalse();

        await Task.Delay(Tick * 3);
        b.IsLeader.Should().BeTrue("a session lock that is never released leaves nothing scheduling at all");
        (await EnqueuedByAsync(queue, "worker-b")).Should().BeGreaterThan(bBefore);

        ctsB.Cancel();
        await SafeAsync(runB);
    }

    [Fact]
    public async Task A_session_provider_is_asked_for_a_session_lock_not_a_lease()
    {
        if (!Server) return;

        // SqlJobLockProvider throws on any non-null leaseDuration rather than silently ignoring a bound it
        // cannot enforce (TASK-232). The scheduler must therefore pass null — this is the pin that says so,
        // because getting it wrong would make every SQL-coordinated scheduler throw on its first tick.
        var queue = new InMemoryJobQueue(_clock);
        await using var provider = new SqlJobLockProvider<PostgreSQLConnector>(Settings());

        var scheduler = new RecurringJobScheduler(queue, _clock, provider, NewLockName());
        scheduler.Register<RecurringProbeJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");

        using var cts = new CancellationTokenSource();
        var run = scheduler.RunAsync(cts.Token);
        await Task.Delay(Tick * 2);

        run.IsFaulted.Should().BeFalse("a lease duration would have been refused by this provider");
        scheduler.IsLeader.Should().BeTrue();

        cts.Cancel();
        await SafeAsync(run);
    }
}

/// <summary>Placeholder job type: this suite asserts on what is enqueued, never on execution.</summary>
public class RecurringProbeJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
