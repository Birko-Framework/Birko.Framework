using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing;

/// <summary>
/// TASK-158 — the processor and scheduler loops: SH-M007 (one failed enqueue killed the scheduler), SH-M008 (a job that
/// succeeded was recorded as failed when the processor stopped mid-completion), SH-M009 (a failure to record an outcome
/// faulted a task nobody observed) and SH-M010 (Stop() threw out of RunAsync while it waited for a slot).
/// </summary>
public class Task158LoopTests
{
    /// <summary>An <see cref="InMemoryJobQueue"/> whose operations can be made to throw or observe the token.</summary>
    private sealed class Faulty : IJobQueue
    {
        public readonly InMemoryJobQueue Inner = new(new SystemDateTimeProvider());
        public Func<CancellationToken, Exception?>? OnComplete;
        public Func<Exception?>? OnFail;
        public Func<Exception?>? OnEnqueue;

        public Task<Guid> EnqueueAsync(JobDescriptor d, CancellationToken ct = default)
            => OnEnqueue?.Invoke() is { } e ? Task.FromException<Guid>(e) : Inner.EnqueueAsync(d, ct);
        public Task<JobDescriptor?> DequeueAsync(string? q = null, CancellationToken ct = default) => Inner.DequeueAsync(q, ct);
        public Task CompleteAsync(Guid id, CancellationToken ct = default)
            => OnComplete?.Invoke(ct) is { } e ? Task.FromException(e) : Inner.CompleteAsync(id, ct);
        public Task FailAsync(Guid id, string error, CancellationToken ct = default)
            => OnFail?.Invoke() is { } e ? Task.FromException(e) : Inner.FailAsync(id, error, ct);
        public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) => Inner.CancelAsync(id, ct);
        public Task<JobDescriptor?> GetAsync(Guid id, CancellationToken ct = default) => Inner.GetAsync(id, ct);
        public Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus s, int limit = 100, CancellationToken ct = default) => Inner.GetByStatusAsync(s, limit, ct);
        public Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken ct = default) => Inner.PurgeAsync(olderThan, ct);
    }

    private sealed class Executor(Func<JobDescriptor, CancellationToken, Task<JobResult>> run) : IJobExecutor
    {
        public Task<JobResult> ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default) => run(descriptor, cancellationToken);
    }

    private static readonly JobQueueOptions Fast = new() { PollingInterval = TimeSpan.FromMilliseconds(10), MaxConcurrency = 1 };

    private static async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task SH_M008_a_job_that_succeeded_is_recorded_complete_even_if_the_processor_stops_meanwhile()
    {
        var queue = new Faulty { OnComplete = ct => ct.IsCancellationRequested ? new OperationCanceledException(ct) : null };
        BackgroundJobProcessor? processor = null;
        processor = new BackgroundJobProcessor(queue, new Executor((_, _) =>
        {
            processor!.Stop(); // shutdown arrives while the job finishes
            return Task.FromResult(JobResult.Succeeded(TimeSpan.Zero));
        }), Fast);
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "x" });

        await processor.RunAsync();

        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task SH_M009_a_failure_to_record_an_outcome_is_reported_not_lost()
    {
        var queue = new Faulty { OnFail = () => new InvalidOperationException("db down") };
        var reported = new List<Exception>();
        var processor = new BackgroundJobProcessor(queue, new Executor((_, _) => Task.FromResult(JobResult.Failed(TimeSpan.Zero, "boom"))), Fast);
        processor.JobOutcomeNotRecorded += (_, ex) => reported.Add(ex);
        await queue.EnqueueAsync(new JobDescriptor { JobType = "x" });

        var run = processor.RunAsync();
        await Eventually(() => reported.Count > 0);
        processor.Stop();
        await run;

        reported.Should().ContainSingle().Which.Message.Should().Be("db down");
    }

    [Fact]
    public async Task SH_M010_stopping_while_waiting_for_a_slot_returns_cleanly()
    {
        var queue = new Faulty();
        var started = new TaskCompletionSource();
        var processor = new BackgroundJobProcessor(queue, new Executor(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return JobResult.Succeeded(TimeSpan.Zero);
        }), Fast);
        await queue.EnqueueAsync(new JobDescriptor { JobType = "a" });
        await queue.EnqueueAsync(new JobDescriptor { JobType = "b" });

        var run = processor.RunAsync();
        await started.Task;
        await Task.Delay(50); // the loop now waits for the single slot
        processor.Stop();

        var act = async () => await run;
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SH_M007_a_failed_enqueue_is_reported_and_the_scheduler_keeps_running()
    {
        var failures = 1;
        var queue = new Faulty { OnEnqueue = () => Interlocked.Decrement(ref failures) >= 0 ? new TimeoutException("blip") : null };
        var scheduler = new RecurringJobScheduler(queue, new SystemDateTimeProvider());
        var reported = new List<(string, Exception)>();
        scheduler.EnqueueFailed += (name, ex) => reported.Add((name, ex));
        scheduler.Register<Birko.BackgroundJobs.Tests.SuccessJob>("tick", TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();

        var run = scheduler.RunAsync(cts.Token);
        await Eventually(() => queue.Inner.GetByStatusAsync(JobStatus.Pending).Result.Count > 0);
        cts.Cancel();
        await run;

        reported.Should().ContainSingle().Which.Item1.Should().Be("tick");
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_break_the_loop()
    {
        var queue = new Faulty { OnFail = () => new InvalidOperationException("db down") };
        var processor = new BackgroundJobProcessor(queue, new Executor((_, _) => Task.FromResult(JobResult.Failed(TimeSpan.Zero, "boom"))), Fast);
        var calls = 0;
        processor.JobOutcomeNotRecorded += (_, _) => { calls++; throw new Exception("subscriber"); };
        await queue.EnqueueAsync(new JobDescriptor { JobType = "x" });

        var run = processor.RunAsync();
        await Eventually(() => calls > 0);
        processor.Stop();

        var act = async () => await run;
        await act.Should().NotThrowAsync();
    }
}

public class Task158SerializationTests
{
    [Fact]
    public void SH_M004_metadata_stored_as_json_null_reads_as_empty()
        => Birko.BackgroundJobs.Models.JobSerializationHelper.DeserializeMetadata("null").Should().BeEmpty();
}
