using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.ElasticSearch.Tests;

/// <summary>
/// TASK-613 / TASK-615, live: dequeue filters on <c>QueueName</c> (a <c>[Keyword]</c> field, which the translator
/// queried as <c>queueName.keyword</c>) and sorts on <c>Priority</c> / <c>EnqueuedAt</c> (sent as the CLR names, a 400).
/// Either alone kept a job from ever being picked up.
/// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public sealed class ElasticSearchJobQueueLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko615jobs" + Guid.NewGuid().ToString("N");

    public ElasticSearchJobQueueLiveTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    private static Task Refresh() => Http.PostAsync(Url!.TrimEnd('/') + "/_refresh", null);

    [Fact]
    public async Task A_job_enqueued_on_a_named_queue_is_dequeued_highest_priority_first()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }

        var queue = new ElasticSearchJobQueue(new Birko.Data.ElasticSearch.Stores.Settings { Location = Url, Name = _prefix });
        var low = new JobDescriptor { JobType = "Low", QueueName = "mail", Priority = 1 };
        var high = new JobDescriptor { JobType = "High", QueueName = "mail", Priority = 5 };
        await queue.EnqueueAsync(low);
        await queue.EnqueueAsync(high);
        await queue.EnqueueAsync(new JobDescriptor { JobType = "Other", QueueName = "reports", Priority = 9 });
        await Refresh();

        (await queue.DequeueAsync("mail"))!.JobType.Should().Be("High");
        // The dequeue that handed out High must not have claimed anything else on the way (TASK-616 measured it
        // leaving Low in Processing too, after returning nothing at all).
        (await queue.Store.ReadRealtimeAsync(low.Guid))!.Status.Should().Be((int)JobStatus.Pending);
        await Refresh();
        (await queue.DequeueAsync("mail"))!.JobType.Should().Be("Low");
        await Refresh();
        (await queue.DequeueAsync("mail")).Should().BeNull("both mail jobs are taken and the reports job is on another queue");
    }

    /// <summary>
    /// TASK-502 made a conflicted filtered update throw <c>StoreConcurrencyException</c>. For a claim, a conflict means
    /// another worker won the job, so dequeue treats it as a lost race. Racing dequeuers hand every job out exactly once
    /// and never throw.
    /// </summary>
    [Fact]
    public async Task Racing_dequeuers_hand_each_job_out_once_and_never_throw()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        var settings = new Birko.Data.ElasticSearch.Stores.Settings { Location = Url, Name = _prefix };
        var queue = new ElasticSearchJobQueue(settings);
        for (var i = 0; i < 8; i++) await queue.EnqueueAsync(new JobDescriptor { JobType = "J" + i, QueueName = "race" });
        await Refresh();

        var workers = System.Linq.Enumerable.Range(0, 4).Select(_ => new ElasticSearchJobQueue(settings)).ToList();
        var handed = new System.Collections.Concurrent.ConcurrentBag<string>();
        for (var round = 0; round < 6 && handed.Count < 8; round++)
        {
            await Task.WhenAll(workers.Select(async w =>
            {
                var job = await w.DequeueAsync("race");
                if (job != null) handed.Add(job.JobType);
            }));
            await Refresh();
        }

        handed.Should().OnlyHaveUniqueItems();
        handed.Should().HaveCount(8);
    }

    // ── TASK-664: a job a dead worker left in Processing is reclaimed after the claim timeout ──

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    private ElasticSearchJobQueue Task664Queue(RetryPolicy policy)
        => new(new Birko.Data.ElasticSearch.Stores.Settings { Location = Url, Name = _prefix + "r" + Guid.NewGuid().ToString("N")[..8] }, policy, Task664Timeout);

    [Fact]
    public async Task TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await Refresh();
        (await queue.DequeueAsync())!.Guid.Should().Be(id, "claimed by a worker that then dies");
        await Refresh();
        (await queue.DequeueAsync()).Should().BeNull("a job claimed just now is not reclaimed");

        await Task.Delay(Task664Timeout * 2);
        await Refresh();
        var again = await queue.DequeueAsync();
        again.Should().NotBeNull("a job held in Processing past the claim timeout is offered again");
        again!.Guid.Should().Be(id);
        again.AttemptCount.Should().Be(2);
        again.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task TASK_664_an_abandoned_job_with_no_attempts_left_is_buried()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });
        await Refresh();
        (await queue.DequeueAsync())!.Guid.Should().Be(id);

        await Task.Delay(Task664Timeout * 2);
        await Refresh();
        (await queue.DequeueAsync()).Should().BeNull("its one attempt is spent");
        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.LastError.Should().StartWith("Abandoned: held in Processing");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TASK_664_racing_workers_reclaim_an_abandoned_job_once()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return;
        }
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await Refresh();
        (await queue.DequeueAsync())!.Guid.Should().Be(id);

        await Task.Delay(Task664Timeout * 2);
        await Refresh();
        using var start = new System.Threading.ManualResetEventSlim(false);
        var workers = System.Linq.Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { start.Wait(); return await queue.DequeueAsync(); })).ToArray();
        start.Set();
        var got = await Task.WhenAll(workers);
        got.Count(j => j != null).Should().Be(1, "one worker wins the reclaim");
        (await queue.GetAsync(id))!.AttemptCount.Should().Be(2);
    }
}
