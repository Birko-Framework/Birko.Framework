using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing
{
    /// <summary>
    /// TASK-237 — <see cref="RecurringJobScheduler"/> kept <c>NextRunAt</c> in process memory, so N workers
    /// each concluded independently that a job was due and enqueued N copies of it, on every backend
    /// including the two that can express a lock — because nothing consumed <see cref="IJobLockProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every assertion here needs two schedulers.</b> A single-instance test proves nothing about a
    /// defect whose whole content is "the second instance also fires", which is why the pre-existing
    /// <see cref="RecurringJobSchedulerTests"/> were green throughout.
    /// </para>
    /// <para>
    /// Attribution is by <c>QueueName</c> rather than by giving each scheduler its own queue: the two share
    /// one queue exactly as deployed workers do, and the queue name says which of them enqueued. Removing
    /// the wiring makes both names appear, which is the red.
    /// </para>
    /// </remarks>
    public class RecurringJobSchedulerLeaderElectionTests
    {
        private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();

        private static string NewLockName() => "task237-" + Guid.NewGuid().ToString("N");

        /// <summary>The loop polls once a second, so leadership changes are never observable faster.</summary>
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

        private async Task<int> EnqueuedByAsync(InMemoryJobQueue queue, string queueName)
        {
            var pending = await queue.GetByStatusAsync(JobStatus.Pending, limit: 1000);
            return pending.Count(j => j.QueueName == queueName);
        }

        [Fact]
        public async Task Two_schedulers_sharing_a_lock_enqueue_one_copy_not_two()
        {
            var queue = new InMemoryJobQueue(_clock);
            var lockName = NewLockName();

            using var providerA = new InProcessJobLockProvider();
            using var providerB = new InProcessJobLockProvider();

            var a = new RecurringJobScheduler(queue, _clock, providerA, lockName);
            var b = new RecurringJobScheduler(queue, _clock, providerB, lockName);
            a.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
            b.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

            using var cts = new CancellationTokenSource();
            var runA = a.RunAsync(cts.Token);
            var runB = b.RunAsync(cts.Token);
            await Task.Delay(Tick * 3);
            cts.Cancel();
            await Task.WhenAll(SafeAsync(runA), SafeAsync(runB));

            var fromA = await EnqueuedByAsync(queue, "worker-a");
            var fromB = await EnqueuedByAsync(queue, "worker-b");

            (fromA > 0).Should().NotBe(fromB > 0,
                "exactly one of the two may enqueue — before this wiring both did, which is one duplicate " +
                "copy of every recurring job per extra worker");
            (fromA + fromB).Should().BeGreaterThan(0,
                "and the leader must actually schedule; two silent followers would pass the line above for " +
                "entirely the wrong reason");
        }

        [Fact]
        public async Task Without_a_lock_provider_both_schedulers_still_enqueue()
        {
            // The control for the test above, and the compatibility pin: absent a provider this is the
            // behaviour every existing consumer already has, so the feature is additive rather than a
            // silent change to anyone who does not opt in.
            var queue = new InMemoryJobQueue(_clock);

            var a = new RecurringJobScheduler(queue, _clock);
            var b = new RecurringJobScheduler(queue, _clock);
            a.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
            b.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

            using var cts = new CancellationTokenSource();
            var runA = a.RunAsync(cts.Token);
            var runB = b.RunAsync(cts.Token);
            await Task.Delay(Tick * 3);
            cts.Cancel();
            await Task.WhenAll(SafeAsync(runA), SafeAsync(runB));

            (await EnqueuedByAsync(queue, "worker-a")).Should().BeGreaterThan(0);
            (await EnqueuedByAsync(queue, "worker-b")).Should().BeGreaterThan(0);

            a.IsLeader.Should().BeTrue("an uncoordinated scheduler always leads");
        }

        [Fact]
        public async Task A_follower_takes_over_when_the_leader_stops()
        {
            // Leadership is re-attempted rather than decided once at startup: without that, the death of a
            // leader leaves nothing scheduling until every worker is restarted.
            var queue = new InMemoryJobQueue(_clock);
            var lockName = NewLockName();

            using var providerA = new InProcessJobLockProvider();
            using var providerB = new InProcessJobLockProvider();

            var a = new RecurringJobScheduler(queue, _clock, providerA, lockName);
            var b = new RecurringJobScheduler(
                queue, _clock, providerB, lockName, leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
            a.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");
            b.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-b");

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
            providerA.IsLocked.Should().BeFalse("RunAsync releases the lock as it exits, so handover does " +
                                                "not wait for a lease to expire or a connection to drop");

            await Task.Delay(Tick * 4);
            b.IsLeader.Should().BeTrue();
            (await EnqueuedByAsync(queue, "worker-b")).Should().BeGreaterThan(bBefore);

            ctsB.Cancel();
            await SafeAsync(runB);
        }

        [Fact]
        public async Task A_new_leader_does_not_replay_what_elapsed_while_it_followed()
        {
            // The easy bug to write by accident: a follower whose NextRunAt is untouched is overdue the
            // instant it takes over, so it fires every missed occurrence at once — duplicating exactly the
            // work the previous leader already did. A new leader restarts the schedule from now.
            var queue = new InMemoryJobQueue(_clock);
            var lockName = NewLockName();
            var interval = TimeSpan.FromSeconds(3);

            using var incumbent = new InProcessJobLockProvider();
            (await incumbent.TryAcquireAsync(lockName, TimeSpan.Zero)).Should().BeTrue();

            using var providerB = new InProcessJobLockProvider();
            var b = new RecurringJobScheduler(
                queue, _clock, providerB, lockName, leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
            b.Register<SuccessJob>("cleanup", interval, "worker-b");

            using var cts = new CancellationTokenSource();
            var run = b.RunAsync(cts.Token);

            // Follow for longer than the interval, so the registration-time NextRunAt is now in the past.
            await Task.Delay(Tick * 4);
            b.IsLeader.Should().BeFalse();

            await incumbent.ReleaseAsync(lockName);
            await Task.Delay(Tick * 2);
            b.IsLeader.Should().BeTrue("the follower must have taken over once the lock was free");

            (await EnqueuedByAsync(queue, "worker-b")).Should().Be(0,
                "the occurrences that elapsed while it followed belonged to the previous leader; replaying " +
                "them on takeover is the duplicate this task exists to remove");

            // ...and it must still be a scheduler afterwards, not a permanently silent one.
            await Task.Delay(interval + Tick * 2);
            (await EnqueuedByAsync(queue, "worker-b")).Should().BeGreaterThan(0,
                "rebasing delays the first run by one interval; it must not cancel it");

            cts.Cancel();
            await SafeAsync(run);
        }

        [Fact]
        public async Task A_lease_lost_mid_run_stops_the_scheduler_enqueueing()
        {
            // On an IsLeaseBased provider IsLocked can go false on its own — Redis clears it when a renewal
            // finds the key gone. A scheduler that cached its leadership would carry on enqueueing as the
            // second leader the lease was supposed to prevent.
            var queue = new InMemoryJobQueue(_clock);
            var lockName = NewLockName();

            using var provider = new InProcessJobLockProvider(leaseBased: true);
            var scheduler = new RecurringJobScheduler(
                queue, _clock, provider, lockName, leadershipRetryInterval: TimeSpan.FromHours(1));
            scheduler.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");

            using var cts = new CancellationTokenSource();
            var run = scheduler.RunAsync(cts.Token);

            await Task.Delay(Tick * 2);
            var whileLeading = await EnqueuedByAsync(queue, "worker-a");
            whileLeading.Should().BeGreaterThan(0);

            provider.SimulateLeaseLoss();
            await Task.Delay(Tick * 3);

            scheduler.IsLeader.Should().BeFalse();
            (await EnqueuedByAsync(queue, "worker-a")).Should().Be(whileLeading,
                "nothing may be enqueued after the lease is known lost");

            cts.Cancel();
            await SafeAsync(run);
        }

        [Fact]
        public async Task A_lock_backend_that_is_down_does_not_stop_the_follower_re_attempting()
        {
            // A follower that propagated the failure would leave the loop dead, so a transient outage of the
            // lock backend would permanently stop recurring work — a worse outcome than the duplication.
            var queue = new InMemoryJobQueue(_clock);

            using var provider = new InProcessJobLockProvider { FailAcquire = true };
            var scheduler = new RecurringJobScheduler(
                queue, _clock, provider, NewLockName(),
                leadershipRetryInterval: TimeSpan.FromMilliseconds(200));
            scheduler.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");

            using var cts = new CancellationTokenSource();
            var run = scheduler.RunAsync(cts.Token);

            await Task.Delay(Tick * 3);
            run.IsCompleted.Should().BeFalse("a failed knock is not a reason to stop scheduling for good");
            provider.AcquireAttempts.Should().BeGreaterThan(1, "it must keep knocking, not decide once");
            scheduler.IsLeader.Should().BeFalse();

            provider.FailAcquire = false;
            await Task.Delay(Tick * 3);
            scheduler.IsLeader.Should().BeTrue("recovery needs no restart");
            (await EnqueuedByAsync(queue, "worker-a")).Should().BeGreaterThan(0);

            cts.Cancel();
            await SafeAsync(run);
        }

        [Fact]
        public async Task Cancelling_a_coordinated_loop_completes_it_rather_than_faulting_it()
        {
            // RunAsync has always completed on cancellation rather than throwing — the pre-existing catch
            // around its Task.Delay says so, and RecurringJobSchedulerTests.RunAsync_StopsOnCancellation
            // awaits it bare. Adding a provider introduced a second await that takes the token, so opting
            // into leader election must not change how the loop ends.
            //
            // The provider is what cancels, because that is the only deterministic way into this path:
            // cancelling from outside is almost always observed by the loop's own Task.Delay instead, so a
            // test that merely cancelled a running loop would pass with or without the fix.
            var queue = new InMemoryJobQueue(_clock);
            using var cts = new CancellationTokenSource();
            using var provider = new InProcessJobLockProvider { CancelDuringAcquire = cts };
            var scheduler = new RecurringJobScheduler(queue, _clock, provider, NewLockName());
            scheduler.Register<SuccessJob>("cleanup", TimeSpan.FromMilliseconds(100), "worker-a");

            var act = () => scheduler.RunAsync(cts.Token);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void A_leadership_retry_interval_must_be_positive()
        {
            var queue = new InMemoryJobQueue(_clock);
            using var provider = new InProcessJobLockProvider();

            var act = () => new RecurringJobScheduler(
                queue, _clock, provider, "x", leadershipRetryInterval: TimeSpan.Zero);

            act.Should().Throw<ArgumentException>().WithParameterName("leadershipRetryInterval",
                "zero would knock on every tick, which costs SqlJobLockProvider a connection per second " +
                "per follower");
        }

        [Fact]
        public void A_lock_name_is_required_when_one_is_supplied_explicitly()
        {
            var queue = new InMemoryJobQueue(_clock);
            using var provider = new InProcessJobLockProvider();

            var act = () => new RecurringJobScheduler(queue, _clock, provider, "   ");

            act.Should().Throw<ArgumentException>().WithParameterName("lockName");
        }

        private static async Task SafeAsync(Task task)
        {
            try { await task; } catch (OperationCanceledException) { }
        }
    }
}
