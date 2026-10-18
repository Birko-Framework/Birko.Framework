using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.RavenDB;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.RavenDB.Tests;

/// <summary>
/// TASK-662 and TASK-158 on a live RavenDB: the attempt budget (MaxRetries null = the policy, 0 = no retry) and the
/// guarded transitions (complete / fail only a claimed job, cancel only a pending one) against the real server's native
/// filter update. Gated on <c>BIRKO_RAVEN_URL</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class JobQueueLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private IJobQueue? Queue()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_RAVEN_URL is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_RAVEN_URL not set.");
            return null;
        }
        return new RavenDBJobQueue(new Birko.Data.RavenDB.Stores.Settings(server, "birko_jobs_" + Guid.NewGuid().ToString("N")), Policy);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    public async Task The_attempt_budget_is_the_jobs_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        if (Queue() is not { } queue) return;
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        (await queue.GetAsync(id))!.MaxRetries.Should().Be(maxRetries);

        var claims = 0;
        for (var i = 0; i < 80 && claims < 10; i++)
        {
            var job = await queue.DequeueAsync();
            if (job == null)
            {
                if ((await queue.GetAsync(id))!.Status == JobStatus.Dead) break;
                await Task.Delay(50);
                continue;
            }
            claims++;
            await queue.FailAsync(job.Guid, "boom");
        }

        claims.Should().Be(attempts);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead);
    }

    [Fact]
    public async Task Transitions_act_only_on_the_status_they_belong_to()
    {
        if (Queue() is not { } queue) return;
        var pending = await queue.EnqueueAsync(new JobDescriptor { JobType = "p" });
        await queue.CompleteAsync(pending);
        (await queue.GetAsync(pending))!.Status.Should().Be(JobStatus.Pending, "an unclaimed job is not completed");
        (await queue.CancelAsync(pending)).Should().BeTrue();
        (await queue.CancelAsync(pending)).Should().BeFalse();

        var running = await queue.EnqueueAsync(new JobDescriptor { JobType = "r" });
        JobDescriptor? claimed = null;
        for (var i = 0; i < 40 && claimed == null; i++)
        {
            claimed = await queue.DequeueAsync();
            if (claimed == null) await Task.Delay(50);
        }
        claimed!.Guid.Should().Be(running);
        (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
        await queue.CompleteAsync(running);
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
    }
}
