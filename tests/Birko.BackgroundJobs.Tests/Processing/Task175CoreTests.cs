using System.Collections.Concurrent;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing;

/// <summary>
/// TASK-175 — the low background-jobs findings in the core: the processor loop (SH-L001, SH-L010), the executor
/// (SH-L002, SH-L003), the recurring scheduler (SH-L004, SH-L005), the in-memory queue (SH-L006, SH-L007), the retry
/// policy (SH-L012), Delay at enqueue (SH-L013) and the dispatcher (SH-L014).
/// </summary>
public class Task175CoreTests
{
    private sealed class Executor(Func<JobDescriptor, CancellationToken, Task<JobResult>> run) : IJobExecutor
    {
        public Task<JobResult> ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default) => run(descriptor, cancellationToken);
    }

    private sealed class CountingPurgeQueue : IJobQueue
    {
        private readonly InMemoryJobQueue _inner = new(new SystemDateTimeProvider());
        public readonly ConcurrentQueue<TimeSpan> Purges = new();
        public Task<Guid> EnqueueAsync(JobDescriptor d, CancellationToken ct = default) => _inner.EnqueueAsync(d, ct);
        public Task<JobDescriptor?> DequeueAsync(string? q = null, CancellationToken ct = default) => _inner.DequeueAsync(q, ct);
        public Task CompleteAsync(Guid id, CancellationToken ct = default) => _inner.CompleteAsync(id, ct);
        public Task FailAsync(Guid id, string error, CancellationToken ct = default) => _inner.FailAsync(id, error, ct);
        public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) => _inner.CancelAsync(id, ct);
        public Task<JobDescriptor?> GetAsync(Guid id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
        public Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus s, int limit = 100, CancellationToken ct = default) => _inner.GetByStatusAsync(s, limit, ct);
        public Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken ct = default)
        {
            Purges.Enqueue(olderThan);
            return Task.FromResult(0);
        }
    }

    private static readonly Executor Succeeds = new((_, _) => Task.FromResult(JobResult.Succeeded(TimeSpan.Zero)));

    // ── SH-L001 ──

    [Fact]
    public async Task SH_L001_a_second_concurrent_run_is_refused_and_stop_reaches_the_running_loop()
    {
        var processor = new BackgroundJobProcessor(new InMemoryJobQueue(new SystemDateTimeProvider()), Succeeds,
            new JobQueueOptions { PollingInterval = TimeSpan.FromMilliseconds(10) });
        var first = processor.RunAsync();
        await Task.Delay(50);

        var second = () => processor.RunAsync();
        await second.Should().ThrowAsync<InvalidOperationException>();

        processor.Stop();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        // Once it has returned, the loop can be started again, and stopped again.
        var again = processor.RunAsync();
        await Task.Delay(30);
        processor.Stop();
        await again.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SH_L001_stop_with_no_loop_running_does_nothing()
    {
        var processor = new BackgroundJobProcessor(new InMemoryJobQueue(new SystemDateTimeProvider()), Succeeds);
        processor.Invoking(p => p.Stop()).Should().NotThrow();
    }

    // ── SH-L002 / SH-L003 ──

    public sealed class NotAJob
    {
        public static int Built;
        public NotAJob() => Built++;
        public Task ExecuteAsync(string input, JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    public sealed class Throws : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct)
            => throw new InvalidOperationException("file 3 of 40 rejected", new IOException("disk full"));
    }

    [Fact]
    public async Task SH_L002_a_type_that_is_not_an_IJob_of_the_input_is_refused_before_it_is_built()
    {
        NotAJob.Built = 0;
        var executor = new JobExecutor(Activator.CreateInstance!);
        var result = await executor.ExecuteAsync(new JobDescriptor
        {
            JobType = typeof(NotAJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            SerializedInput = "\"x\"",
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not implement IJob<String>");
        NotAJob.Built.Should().Be(0, "a type from storage is checked before the factory constructs it");
    }

    [Fact]
    public async Task SH_L002_a_parameterless_job_that_is_not_an_IJob_is_refused_before_it_is_built()
    {
        NotAJob.Built = 0;
        var result = await new JobExecutor(Activator.CreateInstance!).ExecuteAsync(new JobDescriptor { JobType = typeof(NotAJob).AssemblyQualifiedName! });
        result.Success.Should().BeFalse();
        NotAJob.Built.Should().Be(0);
    }

    [Fact]
    public async Task SH_L003_a_jobs_own_exception_is_reported_not_its_inner_one()
    {
        var result = await new JobExecutor(Activator.CreateInstance!).ExecuteAsync(new JobDescriptor { JobType = typeof(Throws).AssemblyQualifiedName! });
        result.Error.Should().Be("file 3 of 40 rejected");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // ── SH-L004 / SH-L005 ──

    private sealed class Tick : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void SH_L004_a_zero_or_negative_interval_is_refused(int ms)
    {
        var scheduler = new RecurringJobScheduler(new InMemoryJobQueue(new SystemDateTimeProvider()), new SystemDateTimeProvider());
        scheduler.Invoking(s => s.Register<Tick>("t", TimeSpan.FromMilliseconds(ms))).Should().Throw<ArgumentOutOfRangeException>();
        scheduler.Invoking(s => s.Register<Tick>(" ", TimeSpan.FromMinutes(1))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SH_L004_a_sub_second_interval_fires_at_its_own_rate()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());
        var scheduler = new RecurringJobScheduler(queue, new SystemDateTimeProvider());
        scheduler.Register<Tick>("fast", TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1250));
        await scheduler.RunAsync(cts.Token);

        var enqueued = (await queue.GetByStatusAsync(JobStatus.Pending, 1000)).Count;
        enqueued.Should().BeGreaterThanOrEqualTo(7, "a 100 ms job over ~1.2 s runs about ten times, not once a second");
    }

    [Fact]
    public void SH_L005_the_next_run_follows_the_schedule_not_the_observed_time()
    {
        var start = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromMinutes(5);

        // Observed 0.9 s late: the next run stays on the 5-minute grid.
        RecurringJobScheduler.NextOccurrence(start, interval, start.AddSeconds(0.9)).Should().Be(start.AddMinutes(5));
        // Stopped for 17 minutes: the missed runs are skipped, not replayed, and the grid is kept.
        RecurringJobScheduler.NextOccurrence(start, interval, start.AddMinutes(17)).Should().Be(start.AddMinutes(20));
    }

    // ── SH-L006 / SH-L007 ──

    [Fact]
    public async Task SH_L006_enqueueing_an_id_already_queued_is_refused_and_keeps_the_stored_job()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());
        var job = new JobDescriptor { JobType = "t" };
        await queue.EnqueueAsync(job);
        await queue.DequeueAsync();

        var again = () => queue.EnqueueAsync(new JobDescriptor { Guid = job.Guid, JobType = "t" });
        await again.Should().ThrowAsync<InvalidOperationException>();
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task SH_L007_a_returned_descriptor_is_a_snapshot()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());
        var job = new JobDescriptor { JobType = "t" };
        await queue.EnqueueAsync(job);
        job.Status = JobStatus.Dead; // the caller's object is not the queue's state

        var read = (await queue.GetAsync(job.Guid))!;
        read.Status.Should().Be(JobStatus.Pending);
        read.Status = JobStatus.Cancelled;
        read.Metadata["x"] = "y";
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Pending);
        (await queue.GetAsync(job.Guid))!.Metadata.Should().BeEmpty();

        var claimed = (await queue.DequeueAsync())!;
        claimed.Status = JobStatus.Pending;
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Processing);
        (await queue.GetByStatusAsync(JobStatus.Processing)).Single().Status = JobStatus.Dead;
        (await queue.GetAsync(job.Guid))!.Status.Should().Be(JobStatus.Processing);
    }

    // ── SH-L010 ──

    [Fact]
    public async Task SH_L010_the_processor_purges_only_when_a_purge_interval_is_set()
    {
        var off = new CountingPurgeQueue();
        var processor = new BackgroundJobProcessor(off, Succeeds, new JobQueueOptions { PollingInterval = TimeSpan.FromMilliseconds(10) });
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150))) await processor.RunAsync(cts.Token);
        off.Purges.Should().BeEmpty("nothing is deleted unless a host opts in");

        var on = new CountingPurgeQueue();
        processor = new BackgroundJobProcessor(on, Succeeds, new JobQueueOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(10),
            PurgeInterval = TimeSpan.FromMilliseconds(40),
            RetentionPeriod = TimeSpan.FromDays(3),
        });
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250))) await processor.RunAsync(cts.Token);
        on.Purges.Should().HaveCountGreaterThanOrEqualTo(2).And.OnlyContain(r => r == TimeSpan.FromDays(3));
    }

    // ── SH-L012 ──

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SH_L012_an_attempt_number_below_one_gets_the_full_base_delay(int attempt)
    {
        new RetryPolicy { BaseDelay = TimeSpan.FromSeconds(30) }.GetDelay(attempt).Should().Be(TimeSpan.FromSeconds(30));
    }

    // ── SH-L013 / SH-L014 ──

    [Fact]
    public async Task SH_L013_a_delay_on_an_enqueued_descriptor_is_honoured()
    {
        var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        var queue = new InMemoryJobQueue(clock);
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Delay = TimeSpan.FromHours(1) });

        (await queue.DequeueAsync()).Should().BeNull("the job is not due for an hour");
        var stored = (await queue.GetAsync(id))!;
        stored.Status.Should().Be(JobStatus.Scheduled);
        stored.ScheduledAt.Should().Be(clock.UtcNow.AddHours(1));

        clock.Advance(TimeSpan.FromHours(1));
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
    }

    public sealed class Typed : IJob<string>
    {
        public Task ExecuteAsync(string input, JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>A serializer that, unlike the default, accepts null: it writes "null", as the finding describes.</summary>
    private sealed class Permissive : Birko.BackgroundJobs.Serialization.IJobSerializer
    {
        public string Serialize(object input) => input == null ? "null" : System.Text.Json.JsonSerializer.Serialize(input);
        public object? Deserialize(string data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }

    [Fact]
    public async Task SH_L014_a_null_typed_input_is_refused_even_by_a_serializer_that_accepts_null()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());
        var dispatcher = new JobDispatcher(queue, new SystemDateTimeProvider(), new Permissive());
        await dispatcher.Invoking(d => d.EnqueueAsync<Typed, string>(null!)).Should().ThrowAsync<ArgumentNullException>();
        (await queue.GetByStatusAsync(JobStatus.Pending)).Should().BeEmpty("no job that can only fail is stored");
    }

    [Fact]
    public async Task SH_L014_a_null_typed_input_is_refused_at_the_call()
    {
        var dispatcher = new JobDispatcher(new InMemoryJobQueue(new SystemDateTimeProvider()), new SystemDateTimeProvider());
        await dispatcher.Invoking(d => d.EnqueueAsync<Typed, string>(null!)).Should().ThrowAsync<ArgumentNullException>();
        await dispatcher.Invoking(d => d.ScheduleAsync<Typed, string>(null!, TimeSpan.FromMinutes(1))).Should().ThrowAsync<ArgumentNullException>();
    }
}
