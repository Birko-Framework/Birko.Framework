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
/// TASK-667, live Elasticsearch: a transition made right after the claim, before the index refreshes, lands. TASK-158
/// made Complete / Fail / Cancel guarded <c>update_by_query</c> calls, which match against the search index: a job
/// claimed under a second ago was still indexed Pending, the Processing guard matched nothing, and the outcome was
/// silently dropped. Gated on <c>BIRKO_ES_URL</c>; failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public sealed class Task667EsTransitionLiveTests(ITestOutputHelper output) : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly string _prefix = "birko667jobs" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    private static Task Refresh() => Http.PostAsync(Url!.TrimEnd('/') + "/_refresh", null);

    private ElasticSearchJobQueue? Queue()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
            return null;
        }
        return new ElasticSearchJobQueue(new Birko.Data.ElasticSearch.Stores.Settings { Location = Url, Name = _prefix },
            new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMinutes(1), UseExponentialBackoff = false });
    }

    private async Task<Guid> ClaimedAsync(ElasticSearchJobQueue queue)
    {
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await Refresh(); // the claim's search must see the job; nothing after this refreshes
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        return id;
    }

    [Fact]
    public async Task A_job_completed_right_after_its_claim_is_completed()
    {
        if (Queue() is not { } queue) return;
        var id = await ClaimedAsync(queue);
        await queue.CompleteAsync(id);
        var job = (await queue.GetAsync(id))!;
        job.Status.Should().Be(JobStatus.Completed, "a job that finished within the refresh interval is recorded");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_job_failed_right_after_its_claim_is_rescheduled()
    {
        if (Queue() is not { } queue) return;
        var id = await ClaimedAsync(queue);
        await queue.FailAsync(id, "boom");
        var job = (await queue.GetAsync(id))!;
        job.Status.Should().Be(JobStatus.Scheduled);
        job.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task A_job_cancelled_right_after_its_claim_is_not_cancelled()
    {
        if (Queue() is not { } queue) return;
        var id = await ClaimedAsync(queue);
        (await queue.CancelAsync(id)).Should().BeFalse("it is running; the stale index still says Pending");
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Processing);
    }
}
