using System.Collections.Concurrent;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.CosmosDB;
using Birko.BackgroundJobs.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.CosmosDB.Tests;

/// <summary>
/// TASK-661 on a live Cosmos DB (the emulator): claims and transitions are conditional writes (ETag), so concurrent
/// dequeuers claim each job once, Cancel acts only on a pending job, and Complete / Fail only on a claimed one; plus
/// TASK-662's attempt budget against the real server. Gated on <c>BIRKO_COSMOS_CONNECTION</c>
/// (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the emulator); failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class JobQueueLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private (CosmosDBJobQueue Queue, AsyncCosmosDBStore<CosmosJobDescriptorModel> Store)? Open()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_COSMOS_CONNECTION is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_COSMOS_CONNECTION not set.");
            return null;
        }
        var settings = new Settings();
        if (string.Equals(Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE"), "Gateway", StringComparison.OrdinalIgnoreCase))
            settings.ConnectionMode = ConnectionMode.Gateway;
        var store = new AsyncCosmosDBStore<CosmosJobDescriptorModel>(conn, "birko_jobs_" + Guid.NewGuid().ToString("N"), null, settings);
        return (new CosmosDBJobQueue(store, Policy), store);
    }

    private static async Task DropAsync(AsyncCosmosDBStore<CosmosJobDescriptorModel> store)
    {
        try { if (store.Container != null) await store.Container.Database.DeleteAsync(); } catch { }
    }

    [Fact]
    public async Task Concurrent_dequeuers_claim_each_job_once()
    {
        if (Open() is not { } live) return;
        var (queue, store) = live;
        try
        {
            const int jobs = 6;
            for (var i = 0; i < jobs; i++) await queue.EnqueueAsync(new JobDescriptor { JobType = "j" + i });

            var claims = new ConcurrentBag<Guid>();
            using var start = new ManualResetEventSlim(false);
            var workers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                for (var k = 0; k < 4; k++)
                {
                    var job = await queue.DequeueAsync();
                    if (job != null) claims.Add(job.Guid);
                }
            })).ToArray();
            start.Set();
            await Task.WhenAll(workers);

            output.WriteLine($"claims {claims.Count}, distinct {claims.Distinct().Count()}");
            claims.Should().OnlyHaveUniqueItems("a job is claimed by one worker");
            claims.Should().HaveCount(jobs);
            foreach (var id in claims) (await queue.GetAsync(id))!.AttemptCount.Should().Be(1);
        }
        finally { await DropAsync(store); }
    }

    [Fact]
    public async Task Transitions_act_only_on_the_status_they_belong_to()
    {
        if (Open() is not { } live) return;
        var (queue, store) = live;
        try
        {
            var pending = await queue.EnqueueAsync(new JobDescriptor { JobType = "p" });
            await queue.CompleteAsync(pending);
            await queue.FailAsync(pending, "x");
            (await queue.GetAsync(pending))!.Status.Should().Be(JobStatus.Pending, "an unclaimed job is neither completed nor failed");
            (await queue.CancelAsync(pending)).Should().BeTrue();
            (await queue.CancelAsync(pending)).Should().BeFalse("it is already cancelled");

            var running = await queue.EnqueueAsync(new JobDescriptor { JobType = "r" });
            (await queue.DequeueAsync())!.Guid.Should().Be(running);
            (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
            (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Processing);
            await queue.CompleteAsync(running);
            (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
            (await queue.CancelAsync(running)).Should().BeFalse("a completed job is not cancelled");
            (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
        }
        finally { await DropAsync(store); }
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    public async Task The_attempt_budget_is_the_jobs_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        if (Open() is not { } live) return;
        var (queue, store) = live;
        try
        {
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
        finally { await DropAsync(store); }
    }
}
