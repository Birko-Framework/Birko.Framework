using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.RavenDB;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.RavenDB.Tests;

/// <summary>
/// TASK-665 on a live RavenDB: claims and transitions are compare-and-sets on the document (loaded by id, written with
/// its change vector), not a query over a possibly stale index followed by an unconditional write. Gated on
/// <c>BIRKO_RAVEN_URL</c>; failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class Task665RavenClaimLiveTests(ITestOutputHelper output)
{
    private RavenDBJobQueue? Queue()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_RAVEN_URL is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_RAVEN_URL not set.");
            return null;
        }
        return new RavenDBJobQueue(new Birko.Data.RavenDB.Stores.Settings(server, "birko_jobs_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task A_job_claimed_a_moment_ago_is_not_dequeued_again()
    {
        if (Queue() is not { } queue) return;
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        (await queue.DequeueAsync()).Should().BeNull("the job is Processing; a stale index must not hand it out again");
        (await queue.GetAsync(id))!.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_dequeuers_claim_each_job_once()
    {
        if (Queue() is not { } queue) return;
        const int jobs = 6;
        for (var i = 0; i < jobs; i++) await queue.EnqueueAsync(new JobDescriptor { JobType = "j" + i });

        var claims = new ConcurrentBag<Guid>();
        using var start = new ManualResetEventSlim(false);
        var workers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            for (var k = 0; k < 4; k++)
            {
                if (await queue.DequeueAsync() is { } job) claims.Add(job.Guid);
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(workers);

        output.WriteLine($"claims {claims.Count}, distinct {claims.Distinct().Count()}");
        claims.Should().OnlyHaveUniqueItems("a job is claimed by one worker");
        foreach (var id in claims.Distinct()) (await queue.GetAsync(id))!.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task A_cancel_racing_a_claim_never_lets_both_win()
    {
        if (Queue() is not { } queue) return;
        const int jobs = 20;
        var ids = new Guid[jobs];
        for (var i = 0; i < jobs; i++) ids[i] = await queue.EnqueueAsync(new JobDescriptor { JobType = "j" });

        var claimed = new ConcurrentBag<Guid>();
        var cancelled = new ConcurrentBag<Guid>();
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                for (var k = 0; k < jobs; k++)
                {
                    if (await queue.DequeueAsync() is { } j) claimed.Add(j.Guid);
                }
            }))
            .Concat(new[] { Task.Run(async () =>
            {
                start.Wait();
                foreach (var id in ids)
                {
                    if (await queue.CancelAsync(id)) cancelled.Add(id);
                }
            }) }).ToArray();
        start.Set();
        await Task.WhenAll(tasks);

        output.WriteLine($"claimed {claimed.Count}, cancelled {cancelled.Count}");
        claimed.Should().OnlyHaveUniqueItems();
        claimed.Intersect(cancelled).Should().BeEmpty("a job is either claimed or cancelled");
        foreach (var id in ids)
            (await queue.GetAsync(id))!.Status.Should().Be(claimed.Contains(id) ? JobStatus.Processing : JobStatus.Cancelled);
    }
}
