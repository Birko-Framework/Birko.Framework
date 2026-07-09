using System;
using System.IO;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.XML;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.XML.Tests;

/// <summary>
/// Coverage for the XML-file job queue (CR-H012): the whole public surface plus the metadata XML
/// round-trip (the SerializableMetadata wrapper exists because System.Xml.Serialization has no
/// Dictionary support) and the FailAsync retry-vs-dead boundary. Backed by a temp directory.
/// </summary>
public class XmlJobQueueTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;
    private readonly TestDateTimeProvider _clock;

    public XmlJobQueueTests()
    {
        // Relative location: PathValidator rejects absolute Windows paths (the drive-letter ':').
        _location = $"birko-bjxml-{Guid.NewGuid():N}";
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
        _clock = new TestDateTimeProvider(new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private XmlJobQueue NewQueue() =>
        new(new Birko.Configuration.Settings(_location, "jobs"), _clock);

    [Fact]
    public async Task Enqueue_Dequeue_Complete_RoundTrips()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var dequeued = await queue.DequeueAsync();
        dequeued!.Id.Should().Be(id);
        dequeued.Status.Should().Be(JobStatus.Processing);

        await queue.CompleteAsync(id);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task Enqueue_PreservesMetadataThroughXmlWrapper()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor
        {
            JobType = "t",
            Metadata = { ["cid"] = "abc-123", ["tenant"] = "acme" }
        });

        var job = await queue.GetAsync(id);

        job!.Metadata.Should().ContainKey("cid").WhoseValue.Should().Be("abc-123");
        job.Metadata.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
    }

    [Fact]
    public async Task FailAsync_WithRetriesRemaining_ReschedulesWithBackoff()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 5 });
        await queue.DequeueAsync(); // AttemptCount -> 1

        await queue.FailAsync(id, "transient");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
        job.ScheduledAt!.Value.Should().BeAfter(_clock.UtcNow);
    }

    [Fact]
    public async Task FailAsync_OnRetryExhaustion_SetsDead()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });
        await queue.DequeueAsync(); // AttemptCount -> 1 (>= MaxRetries)

        await queue.FailAsync(id, "fatal");

        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead);
    }

    [Fact]
    public async Task Dequeue_DoesNotReturnFutureScheduledJob()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 3 });
        await queue.DequeueAsync();
        await queue.FailAsync(id, "retry"); // now Scheduled at future time

        (await queue.DequeueAsync()).Should().BeNull("the retry is scheduled in the future");

        _clock.Advance(TimeSpan.FromHours(1)); // let the backoff elapse
        (await queue.DequeueAsync())!.Id.Should().Be(id);
    }

    [Fact]
    public async Task Dequeue_OrdersByPriorityThenFifo()
    {
        var queue = NewQueue();
        var low = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Priority = 0 });
        _clock.Advance(TimeSpan.FromSeconds(1));
        var high = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Priority = 10 });

        (await queue.DequeueAsync())!.Id.Should().Be(high, "higher priority first");
        (await queue.DequeueAsync())!.Id.Should().Be(low);
    }

    [Fact]
    public async Task PurgeAsync_RemovesTerminalJobsOlderThanCutoff()
    {
        var queue = NewQueue();
        var doneId = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await queue.CompleteAsync(doneId);

        _clock.Advance(TimeSpan.FromDays(2));
        var purged = await queue.PurgeAsync(TimeSpan.FromDays(1));

        purged.Should().Be(1);
        (await queue.GetAsync(doneId)).Should().BeNull();
    }
}
