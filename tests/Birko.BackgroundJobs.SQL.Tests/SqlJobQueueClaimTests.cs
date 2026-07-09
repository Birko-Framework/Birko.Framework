using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.BackgroundJobs.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// Regression for CR-H011: DequeueAsync used to read the top candidate then, in a separate
/// UpdateAsync, flip it to Processing — with no guard on the gap, so two concurrent workers could
/// claim the same job. DequeueAsync now claims atomically (conditional native UPDATE + claim
/// token). Backed by a real (offline) SQLite database.
/// </summary>
public class SqlJobQueueClaimTests : IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public SqlJobQueueClaimTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-bjsql-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "jobs.db");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private SqlJobQueue<SqLiteConnector> NewQueue()
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        store.SetSettings(_settings);
        return new SqlJobQueue<SqLiteConnector>(store);
    }

    [Fact]
    public async Task Dequeue_ReturnsEnqueuedJob()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var dequeued = await queue.DequeueAsync();

        dequeued.Should().NotBeNull();
        dequeued!.Id.Should().Be(id);
        dequeued.Status.Should().Be(JobStatus.Processing);
        dequeued.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentDequeue_NeverClaimsSameJobTwice()
    {
        var queue = NewQueue();
        const int jobCount = 20;
        for (var i = 0; i < jobCount; i++)
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Priority = i });

        var claimed = new ConcurrentBag<Guid>();
        var workers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                var job = await queue.DequeueAsync();
                if (job == null) break;
                claimed.Add(job.Id);
            }
        }));
        await Task.WhenAll(workers);

        var ids = claimed.ToList();
        ids.Should().HaveCount(jobCount, "every job is claimed exactly once");
        ids.Distinct().Should().HaveCount(jobCount, "no job is claimed by two workers");
    }

    [Fact]
    public async Task Dequeue_AfterAllClaimed_ReturnsNull()
    {
        var queue = NewQueue();
        await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        (await queue.DequeueAsync()).Should().NotBeNull();
        (await queue.DequeueAsync()).Should().BeNull("the only job is already Processing");
    }
}
