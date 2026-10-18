using System.Collections.Concurrent;
using System.Linq;
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
        dequeued!.Guid.Should().Be(id);
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
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
    }

    [Fact]
    public async Task Dequeue_OrdersByPriorityThenFifo()
    {
        var queue = NewQueue();
        var low = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Priority = 0 });
        _clock.Advance(TimeSpan.FromSeconds(1));
        var high = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Priority = 10 });

        (await queue.DequeueAsync())!.Guid.Should().Be(high, "higher priority first");
        (await queue.DequeueAsync())!.Guid.Should().Be(low);
    }

    [Fact]
    public async Task PurgeAsync_RemovesTerminalJobsOlderThanCutoff()
    {
        var queue = NewQueue();
        var doneId = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await queue.DequeueAsync(); // only a claimed job is completed (TASK-158)
        await queue.CompleteAsync(doneId);

        _clock.Advance(TimeSpan.FromDays(2));
        var purged = await queue.PurgeAsync(TimeSpan.FromDays(1));

        purged.Should().Be(1);
        (await queue.GetAsync(doneId)).Should().BeNull();
    }

    // ── TASK-158 (SH-M021): two queues on one file share it ──

    [Fact]
    public async Task Two_queues_on_one_file_do_not_overwrite_each_others_jobs()
    {
        var a = NewQueue();
        var b = NewQueue();
        await a.EnqueueAsync(new JobDescriptor { JobType = "j1" });
        await b.EnqueueAsync(new JobDescriptor { JobType = "j2" });
        await a.EnqueueAsync(new JobDescriptor { JobType = "j3" });

        // A store of its own reads the file as another process would.
        var fresh = new Birko.Data.XML.Stores.AsyncXmlStore<Birko.BackgroundJobs.XML.Models.XmlJobDescriptorModel>();
        fresh.SetSettings(new Birko.Configuration.Settings(_location, "jobs"));
        var onDisk = await new XmlJobQueue(fresh, _clock).GetByStatusAsync(JobStatus.Pending);

        onDisk.Select(j => j.JobType).Should().BeEquivalentTo(new[] { "j1", "j2", "j3" });
    }

    [Fact]
    public async Task Two_queues_on_one_file_never_claim_the_same_job()
    {
        var a = NewQueue();
        var b = NewQueue();
        for (var i = 0; i < 20; i++) await a.EnqueueAsync(new JobDescriptor { JobType = "j" + i });

        var claimed = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(w => Task.Run(async () =>
        {
            var q = w % 2 == 0 ? a : b;
            while (await q.DequeueAsync() is { } job) claimed.Add(job.Guid);
        })));

        claimed.Should().HaveCount(20).And.OnlyHaveUniqueItems();
    }


    // ── TASK-662: MaxRetries null = the policy (2 here), 0 = no retry, n = n attempts ──

    private static readonly RetryPolicy Task662Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public async Task TASK_662_a_jobs_attempt_budget_is_its_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        var queue = new XmlJobQueue(new Birko.Configuration.Settings(_location, "jobs"), _clock, Task662Policy);
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
}
