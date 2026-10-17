using System;
using System.Linq;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Birko.BackgroundJobs.Hosting
{
    /// <summary>Settings of <see cref="BackgroundJobsHostingExtensions.AddBackgroundJobs"/>.</summary>
    public sealed class BackgroundJobsHostingOptions
    {
        /// <summary>Concurrency, polling, queue name and job timeout of the processor.</summary>
        public JobQueueOptions Queue { get; set; } = new();

        /// <summary>
        /// The leadership lock every process of this application contends for, when an <see cref="IJobLockProvider"/> is
        /// registered. ⚠ Every node of one application must use the same name, across versions: two names during a rolling
        /// deploy are two leaders, each correctly holding its own lock.
        /// </summary>
        public string RecurringLockName { get; set; } = RecurringJobScheduler.DefaultLockName;

        /// <summary>How often a follower re-attempts leadership; the scheduler's default when null.</summary>
        public TimeSpan? LeadershipRetryInterval { get; set; }
    }

    /// <summary>A recurring job declared in DI, applied to the running scheduler when the host starts.</summary>
    public sealed record RecurringJobRegistration(string Name, Action<RecurringJobScheduler> Apply);

    /// <summary>
    /// Wires Birko.BackgroundJobs into a .NET host (TASK-538): the queue, executor, dispatcher, processor and recurring
    /// scheduler as singletons, and the two hosted services that run them. Backported from Symbio's
    /// <c>JobExtensions</c>, which carries three lessons, each pinned by a test.
    /// </summary>
    public static class BackgroundJobsHostingExtensions
    {
        /// <summary>
        /// Registers the background-job services and their hosted services. Idempotent: a second call (another module's)
        /// does nothing. A service registered before the call — a durable <see cref="IJobQueue"/>, an
        /// <see cref="IDateTimeProvider"/>, an <see cref="IJobExecutor"/> — is kept; otherwise an in-memory queue and the
        /// system clock are used, and each job is resolved from its own DI scope.
        /// </summary>
        /// <remarks>
        /// <b>Lesson 1 (Symbio TASK-383):</b> the idempotency guard keys on <see cref="BackgroundJobProcessor"/>, never on
        /// <see cref="IJobQueue"/>. A host registers its durable queue before the modules load, so a guard on the queue
        /// returned early on the first call and left a queue with no processor: every job silently never ran.
        /// </remarks>
        public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, Action<BackgroundJobsHostingOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (services.Any(d => d.ServiceType == typeof(BackgroundJobProcessor)))
                return services;

            var options = new BackgroundJobsHostingOptions();
            configure?.Invoke(options);

            services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
            services.TryAddSingleton<IJobQueue>(sp => new InMemoryJobQueue(sp.GetRequiredService<IDateTimeProvider>()));
            services.TryAddSingleton<IJobExecutor>(sp => new ScopedJobExecutor(sp.GetRequiredService<IServiceScopeFactory>()));
            services.TryAddSingleton(sp => new JobDispatcher(sp.GetRequiredService<IJobQueue>(), sp.GetRequiredService<IDateTimeProvider>()));
            services.AddSingleton(sp => new BackgroundJobProcessor(sp.GetRequiredService<IJobQueue>(), sp.GetRequiredService<IJobExecutor>(), options.Queue));
            services.TryAddSingleton(sp => new RecurringJobScheduler(
                sp.GetRequiredService<IJobQueue>(),
                sp.GetRequiredService<IDateTimeProvider>(),
                sp.GetService<IJobLockProvider>(),
                options.RecurringLockName,
                options.LeadershipRetryInterval));

            services.AddHostedService<BackgroundJobProcessorHostedService>();
            services.AddHostedService<RecurringJobSchedulerHostedService>();
            return services;
        }

        /// <summary>
        /// Declares a recurring job: <typeparamref name="TJob"/> is registered (transient) and the schedule is applied to
        /// the scheduler the host runs, when it starts.
        /// </summary>
        /// <remarks>
        /// <b>Lesson 2 (Symbio TASK-457):</b> the schedule must reach the scheduler instance that actually runs.
        /// Registering on a scheduler resolved from <c>services.BuildServiceProvider()</c> during configuration registers
        /// on a throwaway instance, and the schedule is silently lost. Order does not matter: this may come before or
        /// after <see cref="AddBackgroundJobs"/>.
        /// </remarks>
        public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string name, TimeSpan interval, string? queueName = null)
            where TJob : class, IJob
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "A recurring job needs a positive interval.");

            services.TryAddTransient<TJob>();
            services.AddSingleton(new RecurringJobRegistration(name, scheduler => scheduler.Register<TJob>(name, interval, queueName)));
            return services;
        }
    }
}
