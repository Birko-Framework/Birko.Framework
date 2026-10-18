using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Birko.BackgroundJobs.Hosting
{
    /// <summary>
    /// The shared lifecycle of the two bridges (TASK-538): the loop runs on its own token from <c>StartAsync</c>, and
    /// <c>StopAsync</c> waits for it to finish for as long as the host's shutdown token allows.
    /// </summary>
    /// <remarks>
    /// Symbio's bridges disposed their token source as soon as the shutdown token fired, while the loop could still be
    /// running on it, and never observed a faulted loop. Here the source is disposed only once the loop has ended, and a
    /// fault is logged when the loop ends.
    /// </remarks>
    public abstract class LoopHostedService : IHostedService
    {
        private readonly ILogger _logger;
        private CancellationTokenSource? _stopping;
        private Task? _loop;

        protected LoopHostedService(ILogger? logger) => _logger = logger ?? NullLogger.Instance;

        /// <summary>The loop; it returns once <paramref name="stoppingToken"/> is cancelled and its work has wound down.</summary>
        protected abstract Task RunAsync(CancellationToken stoppingToken);

        /// <summary>Asks the loop to stop, before the stopping token is cancelled.</summary>
        protected virtual void RequestStop()
        {
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _stopping = new CancellationTokenSource();
            var stopping = _stopping;
            _loop = Task.Run(() => RunAsync(stopping.Token), CancellationToken.None);
            _ = _loop.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "{Service} ended with an error", GetType().Name);
                stopping.Dispose();
            }, TaskScheduler.Default);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_loop == null || _stopping == null) return;
            RequestStop();
            try
            {
                _stopping.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The loop already ended and released its token.
            }
            // Graceful within the host's shutdown timeout: when it fires, StopAsync returns and the loop is left to end.
            await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>Runs <see cref="BackgroundJobProcessor"/> for the host's lifetime.</summary>
    /// <remarks>
    /// ⚠ Stopping the processor also cancels the token its running jobs were given, so "graceful" means that jobs observe
    /// the cancellation and the processor waits for them, up to the host's shutdown timeout.
    /// </remarks>
    public sealed class BackgroundJobProcessorHostedService : LoopHostedService
    {
        private readonly BackgroundJobProcessor _processor;

        public BackgroundJobProcessorHostedService(BackgroundJobProcessor processor, ILogger<BackgroundJobProcessorHostedService>? logger = null)
            : base(logger)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            var log = (ILogger?)logger ?? NullLogger.Instance;
            _processor.JobOutcomeNotRecorded += (job, ex) =>
                log.LogError(ex, "The outcome of job {JobId} ({JobType}) could not be recorded; it stays Processing", job.Guid, job.JobType);
        }

        protected override Task RunAsync(CancellationToken stoppingToken) => _processor.RunAsync(stoppingToken);

        protected override void RequestStop() => _processor.Stop();
    }

    /// <summary>
    /// Applies every <see cref="RecurringJobRegistration"/> to the <see cref="RecurringJobScheduler"/> and runs its loop for
    /// the host's lifetime.
    /// </summary>
    /// <remarks>
    /// <b>Lesson 3 (Symbio TASK-457): the loop always starts, also on a follower.</b> Leadership belongs to the scheduler,
    /// which re-attempts it every tick, and the loop is what re-attempts. Symbio once decided leadership here, once: a
    /// follower returned without starting the loop, and when the leading node died no process scheduled anything until a
    /// restart. There is no "am I the leader?" question at this layer, and there must not be one.
    /// </remarks>
    public sealed class RecurringJobSchedulerHostedService : LoopHostedService
    {
        private readonly RecurringJobScheduler _scheduler;
        private readonly IEnumerable<RecurringJobRegistration> _registrations;
        private readonly IJobLockProvider? _locks;
        private readonly ILogger _logger;

        public RecurringJobSchedulerHostedService(
            RecurringJobScheduler scheduler,
            IEnumerable<RecurringJobRegistration> registrations,
            IServiceProvider services,
            ILogger<RecurringJobSchedulerHostedService>? logger = null)
            : base(logger)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            _locks = services.GetService<IJobLockProvider>();
            _logger = (ILogger?)logger ?? NullLogger.Instance;
            _scheduler.EnqueueFailed += (name, ex) =>
                _logger.LogWarning(ex, "Enqueuing recurring job {Name} failed; it is retried on the next tick", name);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var registration in _registrations)
                registration.Apply(_scheduler);

            if (_locks == null)
                _logger.LogInformation(
                    "No IJobLockProvider is registered, so this process schedules recurring jobs. That is correct for one "
                    + "instance and wrong for two: register a lock provider before running a second node.");

            return base.StartAsync(cancellationToken);
        }

        protected override Task RunAsync(CancellationToken stoppingToken) => _scheduler.RunAsync(stoppingToken);
    }

    /// <summary>
    /// An <see cref="IJobExecutor"/> that resolves each job from its own DI scope and disposes the scope when the job ends
    /// (TASK-538). A job resolved from the root provider (DraCode passed <c>sp.GetRequiredService</c>) cannot take a scoped
    /// dependency under <c>ValidateScopes</c>, and every other dependency it resolves would live for the process.
    /// </summary>
    public sealed class ScopedJobExecutor : IJobExecutor
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly Birko.BackgroundJobs.Serialization.IJobSerializer? _serializer;

        public ScopedJobExecutor(IServiceScopeFactory scopes, Birko.BackgroundJobs.Serialization.IJobSerializer? serializer = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _serializer = serializer;
        }

        public async Task<JobResult> ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var executor = new JobExecutor(type => scope.ServiceProvider.GetRequiredService(type), _serializer);
            return await executor.ExecuteAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
    }
}
