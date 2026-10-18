using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing;

public class Task664ReclaimTests
{
    private readonly TestDateTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private InMemoryJobQueue Task664Queue(RetryPolicy policy) => new(_clock, policy, Task664Timeout);

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout()
    {
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        (await queue.DequeueAsync())!.Guid.Should().Be(id, "claimed by a worker that then dies");
        (await queue.DequeueAsync()).Should().BeNull("a job claimed just now is not reclaimed");
        _clock.Advance(Task664Timeout * 2);
        var again = await queue.DequeueAsync();
        again.Should().NotBeNull("a job held in Processing past the claim timeout is offered again");
        again!.Guid.Should().Be(id);
        again.AttemptCount.Should().Be(2);
        again.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task TASK_664_an_abandoned_job_with_no_attempts_left_is_buried()
    {
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        _clock.Advance(Task664Timeout * 2);
        (await queue.DequeueAsync()).Should().BeNull("its one attempt is spent");
        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.LastError.Should().StartWith("Abandoned: held in Processing");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TASK_664_racing_workers_reclaim_an_abandoned_job_once()
    {
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        _clock.Advance(Task664Timeout * 2);
        using var start = new System.Threading.ManualResetEventSlim(false);
        var workers = System.Linq.Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { start.Wait(); return await queue.DequeueAsync(); })).ToArray();
        start.Set();
        var got = await Task.WhenAll(workers);
        got.Count(j => j != null).Should().Be(1, "one worker wins the reclaim");
        (await queue.GetAsync(id))!.AttemptCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_claim_timeout_that_is_not_positive_is_refused(int seconds)
    {
        var act = () => new InMemoryJobQueue(_clock, claimTimeout: TimeSpan.FromSeconds(seconds));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_processor_timeout_and_the_claim_timeout_share_one_default()
    {
        new JobQueueOptions().JobTimeout.Should().Be(JobReclaim.DefaultClaimTimeout).And.Be(TimeSpan.FromMinutes(30));
        JobReclaim.AbandonedError(TimeSpan.FromMinutes(30), 3)
            .Should().Be("Abandoned: held in Processing for longer than 00:30:00 after 3 attempt(s), and the attempt budget is spent.");
    }
}
