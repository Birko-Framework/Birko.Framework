using System;
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
}
