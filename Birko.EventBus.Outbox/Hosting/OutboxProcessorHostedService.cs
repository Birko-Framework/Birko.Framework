using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Outbox.Publishing;
using Microsoft.Extensions.Hosting;

namespace Birko.EventBus.Outbox.Hosting
{
    /// <summary>
    /// Hosted service that runs the <see cref="OutboxProcessor"/> in a background loop.
    /// Polls for pending entries at the configured interval and publishes them.
    /// </summary>
    public class OutboxProcessorHostedService : BackgroundService
    {
        private readonly OutboxProcessor _processor;
        private readonly OutboxOptions _options;

        public OutboxProcessorHostedService(OutboxProcessor processor, OutboxOptions options)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                    await _processor.CleanupAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Log and continue — processor should be resilient to transient failures
                }

                try
                {
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
