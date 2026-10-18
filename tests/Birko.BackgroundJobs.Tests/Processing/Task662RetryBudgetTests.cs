using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing;

public class Task662RetryBudgetTests
{
    // ── TASK-662: MaxRetries null = the policy (2 here), 0 = no retry, n = n attempts ──

    private static readonly RetryPolicy Task662Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public async Task TASK_662_a_jobs_attempt_budget_is_its_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider(), Task662Policy);
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        (await queue.GetAsync(id))!.MaxRetries.Should().Be(maxRetries, "null is stored as null, not as a default");

        var claims = 0;
        for (var i = 0; i < 40 && claims < 10; i++)
        {
            var job = await queue.DequeueAsync();
            if (job == null)
            {
                if ((await queue.GetAsync(id))!.Status == JobStatus.Dead) break;
                await Task.Delay(25);
                continue;
            }
            claims++;
            await queue.FailAsync(job.Guid, "boom");
        }

        claims.Should().Be(attempts);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead);
    }

    [Fact]
    public void The_policy_answers_for_a_job_that_sets_no_budget()
    {
        new RetryPolicy { MaxRetries = 5 }.MaxRetriesFor(null).Should().Be(5);
        new RetryPolicy { MaxRetries = 5 }.MaxRetriesFor(0).Should().Be(0);
        new RetryPolicy { MaxRetries = 5 }.MaxRetriesFor(2).Should().Be(2);
    }
}
