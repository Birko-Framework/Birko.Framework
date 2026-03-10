using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Rate limiting middleware for SSE connections
    /// </summary>
    public class SseRateLimitMiddleware : ISseMiddleware
    {
        private readonly ILogger<SseRateLimitMiddleware> _logger;
        private readonly Dictionary<string, List<DateTime>> _connectionHistory = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly int _maxConnectionsPerMinute;
        private readonly TimeSpan _window;

        /// <summary>
        /// Initializes a new instance of the SseRateLimitMiddleware class
        /// </summary>
        public SseRateLimitMiddleware(
            ILogger<SseRateLimitMiddleware> logger,
            int maxConnectionsPerMinute = 60)
        {
            _logger = logger;
            _maxConnectionsPerMinute = maxConnectionsPerMinute;
            _window = TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Creates a new instance of the middleware
        /// </summary>
        public static SseRateLimitMiddleware Create(ILogger<SseRateLimitMiddleware> logger, int maxConnectionsPerMinute = 60)
        {
            return new SseRateLimitMiddleware(logger, maxConnectionsPerMinute);
        }

        /// <summary>
        /// Processes the SSE connection request with rate limiting
        /// </summary>
        public async Task<SseResponse?> ProcessAsync(SseContext context, SseRequestDelegate next)
        {
            var clientKey = context.RemoteEndPoint ?? "unknown";

            await _lock.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;

                // Clean old entries
                if (_connectionHistory.ContainsKey(clientKey))
                {
                    _connectionHistory[clientKey] = _connectionHistory[clientKey]
                        .Where(t => now - t < _window)
                        .ToList();
                }
                else
                {
                    _connectionHistory[clientKey] = new List<DateTime>();
                }

                // Check rate limit
                if (_connectionHistory[clientKey].Count >= _maxConnectionsPerMinute)
                {
                    _logger.LogWarning("Rate limit exceeded for {ClientKey}", clientKey);
                    return SseResponse.Denied(429, "Too many connection attempts");
                }

                _connectionHistory[clientKey].Add(now);
            }
            finally
            {
                _lock.Release();
            }

            return await next(context);
        }
    }

    /// <summary>
    /// Simple semaphore implementation for .NET Standard compatibility
    /// </summary>
    internal class SemaphoreSlim
    {
        private readonly System.Threading.SemaphoreSlim _semaphore;

        public SemaphoreSlim(int initialCount, int maxCount)
        {
            _semaphore = new System.Threading.SemaphoreSlim(initialCount, maxCount);
        }

        public Task WaitAsync()
        {
            return _semaphore.WaitAsync();
        }

        public Task WaitAsync(System.Threading.CancellationToken cancellationToken)
        {
            return _semaphore.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _semaphore.Release();
        }
    }
}
