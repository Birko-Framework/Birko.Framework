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
/// TASK-662 and TASK-158 on a live Redis: the attempt budget (MaxRetries null = the policy, 0 = no retry) and the
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

    // The guarded transitions on Redis are TASK-663 (atomic Redis transitions); its test lands there.
}
