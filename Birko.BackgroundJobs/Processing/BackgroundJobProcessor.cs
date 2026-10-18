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

                    // SH-M010: a stop while waiting for a slot (the normal state at MaxConcurrency) ends the loop like
                    // every other wait in it; it used to throw out of RunAsync.
                    try
                    {
                        await _concurrencySemaphore.WaitAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

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
        /// Raised when a job's outcome (completed, failed) could not be written to the queue (TASK-158, SH-M009). The job
        /// then stays Processing in the store. A throwing handler is ignored.
        /// </summary>
        public event Action<JobDescriptor, Exception>? JobOutcomeNotRecorded;

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

                // SH-M008: the job has run, so its outcome is written whatever happens to the processor now. With the
                // processor's token, a Stop() during CompleteAsync threw before anything was written and the catch below
                // recorded a job that succeeded as failed.
                if (result.Success)
                    await RecordAsync(descriptor, () => _queue.CompleteAsync(descriptor.Guid, CancellationToken.None)).ConfigureAwait(false);
                else
                    await RecordAsync(descriptor, () => _queue.FailAsync(descriptor.Guid, result.Error ?? "Unknown error", CancellationToken.None)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown: this is recorded as a *failed attempt* (it consumes a retry, and a
                // last-attempt job goes to Dead). A true no-retry requeue would need a dedicated
                // IJobQueue.RequeueAsync — the current EnqueueAsync is an insert (CreateAsync) across the
                // backends, so re-enqueuing the same descriptor id would PK-conflict; deferred (CR-L016).
                await RecordAsync(descriptor, () => _queue.FailAsync(descriptor.Guid, "Job cancelled due to processor shutdown", CancellationToken.None)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RecordAsync(descriptor, () => _queue.FailAsync(descriptor.Guid, ex.Message, CancellationToken.None)).ConfigureAwait(false);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        // SH-M009: a failure to write the outcome is reported, never left to fault a task the loop drops unobserved.
        private async Task RecordAsync(JobDescriptor descriptor, Func<Task> write)
        {
            try
            {
                await write().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    JobOutcomeNotRecorded?.Invoke(descriptor, ex);
                }
                catch
                {
                    // A diagnostic channel must survive its own subscriber (rule 25).
                }
            }
        }
    }
}
