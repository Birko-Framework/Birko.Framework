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
/// TASK-662, TASK-158 and TASK-663 on a live Redis: the attempt budget (MaxRetries null = the policy, 0 = no retry) and the
/// guarded transitions (complete / fail only a claimed job, cancel only a pending one) against the real server's native
/// filter update. Gated on <c>BIRKO_REDIS_HOST</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class JobQueueLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private IJobQueue? Queue()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
            return null;
        }
        return new RedisJobQueue(new RedisSettings(server) { KeyPrefix = "birko:jobs:" + Guid.NewGuid().ToString("N") }, Policy);
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
        await queue.FailAsync(pending, "x");
        (await queue.GetAsync(pending))!.Status.Should().Be(JobStatus.Pending, "an unclaimed job is neither completed nor failed");
        (await queue.CancelAsync(pending)).Should().BeTrue();
        (await queue.CancelAsync(pending)).Should().BeFalse();

        var running = await queue.EnqueueAsync(new JobDescriptor { JobType = "r" });
        (await queue.DequeueAsync())!.Guid.Should().Be(running);
        (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
        await queue.CompleteAsync(running);
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
        (await queue.CancelAsync(running)).Should().BeFalse();
    }
}
