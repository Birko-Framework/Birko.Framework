using System.Diagnostics;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Hosting;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.BackgroundJobs.Hosting.Tests;

/// <summary>
/// TASK-538 — <c>AddBackgroundJobs</c>, <c>AddRecurringJob</c> and the hosted services, with Symbio's three lessons as
/// tests: the idempotency guard keys on the processor, a recurring job reaches the scheduler that runs, and a follower
/// still runs its loop.
/// </summary>
public class BackgroundJobsHostingTests
{
    // ── jobs ──

    public sealed class Counter
    {
        private int _runs;
        public int Runs => Volatile.Read(ref _runs);
        public void Hit() => Interlocked.Increment(ref _runs);
    }

    public sealed class CountingJob(Counter counter) : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            counter.Hit();
            return Task.CompletedTask;
        }
    }

    public sealed class ScopedDependency : IDisposable
    {
        public static int Disposed;
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    public sealed class ScopedJob(ScopedDependency dependency, Counter counter) : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _ = dependency;
            counter.Hit();
            return Task.CompletedTask;
        }
    }

    public sealed class StubbornJob : IJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None); // ignores cancellation
    }

    private sealed class SwitchLock : IJobLockProvider
    {
        public volatile bool Grant;
        private bool _held;
        public bool IsLocked => _held;
        public bool IsLeaseBased => false;
        public Task<bool> TryAcquireAsync(string lockName, TimeSpan acquireTimeout, TimeSpan? leaseDuration = null, CancellationToken cancellationToken = default)
        {
            _held = Grant;
            return Task.FromResult(_held);
        }
        public Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default) { _held = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private static readonly JobQueueOptions Fast = new() { PollingInterval = TimeSpan.FromMilliseconds(20), MaxConcurrency = 2 };

    private static async Task<IServiceProvider> Started(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        return provider;
    }

    private static async Task Stop(IServiceProvider provider, CancellationToken shutdown = default)
    {
        foreach (var hosted in provider.GetServices<IHostedService>().Reverse())
            await hosted.StopAsync(shutdown);
    }

    private static async Task Eventually(Func<bool> condition, TimeSpan? within = null)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > (within ?? TimeSpan.FromSeconds(10))) throw new TimeoutException("condition not met");
            await Task.Delay(20);
        }
    }

    // ── Lesson 1: idempotent, keyed on the processor ──

    [Fact]
    public void A_durable_queue_registered_first_still_gets_a_processor_and_a_second_call_adds_nothing()
    {
        var durable = new InMemoryJobQueue(new SystemDateTimeProvider());
        var services = new ServiceCollection();
        services.AddSingleton<IJobQueue>(durable);

        services.AddBackgroundJobs();
        services.AddBackgroundJobs();

        services.Count(d => d.ServiceType == typeof(BackgroundJobProcessor)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IJobQueue)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(2);
        services.BuildServiceProvider().GetRequiredService<IJobQueue>().Should().BeSameAs(durable);
    }

    // ── SH-L009 (TASK-175): the default queue uses the options' retry policy ──

    [Fact]
    public async Task The_default_queue_takes_its_retry_policy_from_the_options()
    {
        var services = new ServiceCollection();
        services.AddBackgroundJobs(o => o.Queue.RetryPolicy = new RetryPolicy { MaxRetries = 1, BaseDelay = TimeSpan.Zero });
        var queue = services.BuildServiceProvider().GetRequiredService<IJobQueue>();

        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = null });
        await queue.DequeueAsync();
        await queue.FailAsync(id, "boom");

        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead, "the options allow one attempt; the default policy allowed three");
    }

    // ── a job runs end to end, from its own scope ──

    [Fact]
    public async Task An_enqueued_job_runs_and_a_scoped_dependency_is_resolved_and_disposed_per_job()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddScoped<ScopedDependency>();
        services.AddTransient<ScopedJob>();
        services.AddBackgroundJobs(o => o.Queue = Fast);
        var provider = await Started(services);
        var disposedBefore = ScopedDependency.Disposed;

        await provider.GetRequiredService<JobDispatcher>().EnqueueAsync<ScopedJob>();
        await Eventually(() => provider.GetRequiredService<Counter>().Runs == 1);
        await Eventually(() => ScopedDependency.Disposed > disposedBefore);

        await Stop(provider);
    }

    // ── Lesson 2: a recurring job reaches the scheduler that runs, whatever the call order ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_recurring_job_declared_in_di_is_scheduled_by_the_running_scheduler(bool declaredFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        if (declaredFirst) services.AddRecurringJob<CountingJob>("count", TimeSpan.FromMilliseconds(50));
        services.AddBackgroundJobs(o => o.Queue = Fast);
        if (!declaredFirst) services.AddRecurringJob<CountingJob>("count", TimeSpan.FromMilliseconds(50));

        var provider = await Started(services);
        await Eventually(() => provider.GetRequiredService<Counter>().Runs >= 1);
        await Stop(provider);
    }

    // ── Lesson 3: a follower runs its loop and takes over ──

    [Fact]
    public async Task A_follower_runs_its_loop_and_takes_over_when_the_lock_frees()
    {
        var lockProvider = new SwitchLock { Grant = false };
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<IJobLockProvider>(lockProvider);
        services.AddRecurringJob<CountingJob>("count", TimeSpan.FromMilliseconds(50));
        services.AddBackgroundJobs(o =>
        {
            o.Queue = Fast;
            o.LeadershipRetryInterval = TimeSpan.FromMilliseconds(50);
            o.RecurringLockName = "app:recurring";
        });

        var provider = await Started(services);
        await Task.Delay(400);
        provider.GetRequiredService<Counter>().Runs.Should().Be(0, "a follower enqueues nothing");
        provider.GetRequiredService<RecurringJobScheduler>().IsLeader.Should().BeFalse();

        lockProvider.Grant = true; // the leader died
        await Eventually(() => provider.GetRequiredService<Counter>().Runs >= 1);
        provider.GetRequiredService<RecurringJobScheduler>().IsLeader.Should().BeTrue();
        await Stop(provider);
    }

    // ── graceful stop honours the host's shutdown token ──

    [Fact]
    public async Task Stopping_waits_for_running_jobs_only_until_the_host_gives_up()
    {
        var services = new ServiceCollection();
        services.AddTransient<StubbornJob>();
        services.AddBackgroundJobs(o => o.Queue = Fast);
        var provider = await Started(services);
        await provider.GetRequiredService<JobDispatcher>().EnqueueAsync<StubbornJob>();
        await Task.Delay(200); // let the processor pick it up

        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        await Stop(provider, shutdown.Token);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the job ignores cancellation, so only the shutdown token can end the wait");
    }

    [Fact]
    public async Task Stopping_an_idle_processor_returns_promptly()
    {
        var services = new ServiceCollection();
        services.AddBackgroundJobs(o => o.Queue = Fast);
        var provider = await Started(services);
        var watch = Stopwatch.StartNew();
        await Stop(provider, CancellationToken.None);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    // ── options and arguments ──

    [Fact]
    public void A_registered_executor_and_clock_are_kept()
    {
        var clock = new SystemDateTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IDateTimeProvider>(clock);
        services.AddBackgroundJobs();
        services.BuildServiceProvider().GetRequiredService<IDateTimeProvider>().Should().BeSameAs(clock);
    }

    [Fact]
    public void A_recurring_job_needs_a_name_and_a_positive_interval()
    {
        var services = new ServiceCollection();
        ((Action)(() => services.AddRecurringJob<CountingJob>(" ", TimeSpan.FromSeconds(1)))).Should().Throw<ArgumentException>();
        ((Action)(() => services.AddRecurringJob<CountingJob>("x", TimeSpan.Zero))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
