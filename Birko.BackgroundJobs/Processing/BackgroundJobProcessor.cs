using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// Long-running background processor that polls the job queue and executes jobs.
    /// Designed to be hosted as an IHostedService or run standalone.
    /// </summary>
    public class BackgroundJobProcessor
    {
        private readonly IJobQueue _queue;
        private readonly IJobExecutor _executor;
        private readonly JobQueueOptions _options;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private CancellationTokenSource? _cts;

        public BackgroundJobProcessor(IJobQueue queue, IJobExecutor executor, JobQueueOptions? options = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _options = options ?? new JobQueueOptions();
            _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        }

        /// <summary>
        /// Starts the processor loop. Blocks until the cancellation token is triggered.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = new List<Task>();

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // Clean up completed tasks
                    tasks.RemoveAll(t => t.IsCompleted);

                    await _concurrencySemaphore.WaitAsync(_cts.Token).ConfigureAwait(false);

                    JobDescriptor? descriptor;
                    try
                    {
                        descriptor = await _queue.DequeueAsync(_options.DefaultQueueName, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _concurrencySemaphore.Release();
                        break;
                    }

                    if (descriptor == null)
                    {
                        _concurrencySemaphore.Release();
                        try
                        {
                            await Task.Delay(_options.PollingInterval, _cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        continue;
                    }

                    var task = ProcessJobAsync(descriptor, _cts.Token);
                    tasks.Add(task);
                }
            }
            finally
            {
                // Wait for in-flight jobs to complete
                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Stops the processor gracefully.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task ProcessJobAsync(JobDescriptor descriptor, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.JobTimeout);

                var result = await _executor.ExecuteAsync(descriptor, timeoutCts.Token).ConfigureAwait(false);

                if (result.Success)
                {
                    await _queue.CompleteAsync(descriptor.Id, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _queue.FailAsync(descriptor.Id, result.Error ?? "Unknown error", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown — re-enqueue
                await _queue.FailAsync(descriptor.Id, "Job cancelled due to processor shutdown", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await _queue.FailAsync(descriptor.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }
    }
}
