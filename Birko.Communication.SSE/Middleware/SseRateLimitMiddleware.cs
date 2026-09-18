using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Rate limiting middleware for SSE connections
    /// </summary>
    public class SseRateLimitMiddleware : ISseMiddleware, IDisposable
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

                // Prune every client's history and DROP keys that have fully aged out. The old code
                // only pruned the current key's list and never removed empty keys, so a server facing
                // many distinct client IPs grew this dictionary without bound (a slow leak / DoS in the
                // very component meant to mitigate abuse) — CR-M067.
                foreach (var key in _connectionHistory.Keys.ToList())
                {
                    var pruned = _connectionHistory[key].Where(t => now - t < _window).ToList();
                    if (pruned.Count == 0)
                        _connectionHistory.Remove(key);
                    else
                        _connectionHistory[key] = pruned;
                }

                if (!_connectionHistory.TryGetValue(clientKey, out var history))
                {
                    history = new List<DateTime>();
                    _connectionHistory[clientKey] = history;
                }

                // Check rate limit
                if (history.Count >= _maxConnectionsPerMinute)
                {
                    _logger.LogWarning("Rate limit exceeded for {ClientKey}", clientKey);
                    return SseResponse.Denied(429, "Too many connection attempts");
                }

                history.Add(now);
            }
            finally
            {
                _lock.Release();
            }

            return await next(context);
        }

        /// <summary>
        /// Disposes the internal lock semaphore (CR-H034: it was never disposed, leaking the handle).
        /// </summary>
        public void Dispose()
        {
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
