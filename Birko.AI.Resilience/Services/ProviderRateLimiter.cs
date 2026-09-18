using System.Collections.Concurrent;
using Birko.AI.Resilience.Configuration;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Resilience.Services
{
    /// <summary>
    /// Enforces per-provider rate limits using sliding window counters.
    /// Thread-safe for concurrent access from multiple agents.
    /// </summary>
    public class ProviderRateLimiter
    {
        private readonly ConcurrentDictionary<string, RateLimitWindow> _minuteWindows = new();
        private readonly ConcurrentDictionary<string, RateLimitWindow> _dayWindows = new();
        private readonly Dictionary<string, ProviderRateLimit> _limits;
        private readonly bool _enabled;
        private readonly ILogger? _logger;

        public ProviderRateLimiter(RateLimitConfiguration config, ILogger? logger = null)
        {
            _enabled = config.Enabled;
            _logger = logger;
            // Last-wins build: two config entries for the same provider differing only in case
            // (e.g. "OpenAI" and "openai") lowercase to the same key, which would make ToDictionary
            // throw ArgumentException at construction. Keys are already lowercased, so the
            // OrdinalIgnoreCase comparer was redundant too (CR-L013).
            _limits = new Dictionary<string, ProviderRateLimit>();
            foreach (var limit in config.ProviderLimits)
                _limits[limit.Provider.ToLowerInvariant()] = limit;
        }

        public bool CanMakeRequest(string provider)
        {
            if (!_enabled) return true;

            var key = provider.ToLowerInvariant();
            if (!_limits.TryGetValue(key, out var limit)) return true;

            var now = DateTime.UtcNow;

            if (limit.RequestsPerMinute > 0)
            {
                var window = _minuteWindows.GetOrAdd(key, _ => new RateLimitWindow());
                lock (window)
                {
                    window.Slide(now, TimeSpan.FromMinutes(1));
                    if (window.RequestCount >= limit.RequestsPerMinute)
                    {
                        _logger?.LogWarning("Rate limit hit for {Provider}: {Count}/{Limit} RPM",
                            provider, window.RequestCount, limit.RequestsPerMinute);
                        return false;
                    }
                }
            }

            if (limit.TokensPerMinute > 0)
            {
                var window = _minuteWindows.GetOrAdd(key, _ => new RateLimitWindow());
                lock (window)
                {
                    window.Slide(now, TimeSpan.FromMinutes(1));
                    if (window.TokenCount >= limit.TokensPerMinute)
                    {
                        _logger?.LogWarning("Token rate limit hit for {Provider}: {Count}/{Limit} TPM",
                            provider, window.TokenCount, limit.TokensPerMinute);
                        return false;
                    }
                }
            }

            if (limit.RequestsPerDay > 0 || limit.TokensPerDay > 0)
            {
                var dayWindow = _dayWindows.GetOrAdd(key, _ => new RateLimitWindow());
                lock (dayWindow)
                {
                    dayWindow.Slide(now, TimeSpan.FromDays(1));

                    if (limit.RequestsPerDay > 0 && dayWindow.RequestCount >= limit.RequestsPerDay)
                    {
                        _logger?.LogWarning("Daily rate limit hit for {Provider}: {Count}/{Limit} RPD",
                            provider, dayWindow.RequestCount, limit.RequestsPerDay);
                        return false;
                    }

                    if (limit.TokensPerDay > 0 && dayWindow.TokenCount >= limit.TokensPerDay)
                    {
                        _logger?.LogWarning("Daily token limit hit for {Provider}: {Count}/{Limit} TPD",
                            provider, dayWindow.TokenCount, limit.TokensPerDay);
                        return false;
                    }
                }
            }

            return true;
        }

        public void RecordRequest(string provider, int tokenCount = 0)
        {
            if (!_enabled) return;

            var key = provider.ToLowerInvariant();
            var now = DateTime.UtcNow;

            var minuteWindow = _minuteWindows.GetOrAdd(key, _ => new RateLimitWindow());
            lock (minuteWindow)
            {
                minuteWindow.Slide(now, TimeSpan.FromMinutes(1));
                minuteWindow.RequestCount++;
                minuteWindow.TokenCount += tokenCount;
            }

            var dayWindow = _dayWindows.GetOrAdd(key, _ => new RateLimitWindow());
            lock (dayWindow)
            {
                dayWindow.Slide(now, TimeSpan.FromDays(1));
                dayWindow.RequestCount++;
                dayWindow.TokenCount += tokenCount;
            }
        }

        public TimeSpan? GetRetryAfter(string provider)
        {
            if (!_enabled) return null;

            var key = provider.ToLowerInvariant();
            if (!_limits.TryGetValue(key, out var limit)) return null;

            var now = DateTime.UtcNow;
            TimeSpan? retryAfter = null;

            // A blocking per-minute window (requests OR tokens) clears at the top of the next minute.
            if ((limit.RequestsPerMinute > 0 || limit.TokensPerMinute > 0)
                && _minuteWindows.TryGetValue(key, out var minuteWindow))
            {
                lock (minuteWindow)
                {
                    minuteWindow.Slide(now, TimeSpan.FromMinutes(1));
                    var minuteBlocked =
                        (limit.RequestsPerMinute > 0 && minuteWindow.RequestCount >= limit.RequestsPerMinute) ||
                        (limit.TokensPerMinute > 0 && minuteWindow.TokenCount >= limit.TokensPerMinute);
                    if (minuteBlocked)
                        retryAfter = Max(retryAfter, minuteWindow.WindowStart.AddMinutes(1) - now);
                }
            }

            // A blocking daily window (requests OR tokens) clears when the day window rolls over.
            if ((limit.RequestsPerDay > 0 || limit.TokensPerDay > 0)
                && _dayWindows.TryGetValue(key, out var dayWindow))
            {
                lock (dayWindow)
                {
                    dayWindow.Slide(now, TimeSpan.FromDays(1));
                    var dayBlocked =
                        (limit.RequestsPerDay > 0 && dayWindow.RequestCount >= limit.RequestsPerDay) ||
                        (limit.TokensPerDay > 0 && dayWindow.TokenCount >= limit.TokensPerDay);
                    if (dayBlocked)
                        retryAfter = Max(retryAfter, dayWindow.WindowStart.AddDays(1) - now);
                }
            }

            // Never hand back a negative/zero span (the window may have just slid).
            if (retryAfter.HasValue && retryAfter.Value <= TimeSpan.Zero)
                return null;

            return retryAfter;
        }

        private static TimeSpan? Max(TimeSpan? current, TimeSpan candidate)
            => !current.HasValue || candidate > current.Value ? candidate : current;

        public Dictionary<string, RateLimitStatus> GetAllStatuses()
        {
            var result = new Dictionary<string, RateLimitStatus>();
            var now = DateTime.UtcNow;

            foreach (var (provider, limit) in _limits)
            {
                var status = new RateLimitStatus { Provider = provider };

                if (_minuteWindows.TryGetValue(provider, out var mw))
                {
                    lock (mw)
                    {
                        mw.Slide(now, TimeSpan.FromMinutes(1));
                        status.RequestsThisMinute = mw.RequestCount;
                        status.TokensThisMinute = mw.TokenCount;
                    }
                }

                if (_dayWindows.TryGetValue(provider, out var dw))
                {
                    lock (dw)
                    {
                        dw.Slide(now, TimeSpan.FromDays(1));
                        status.RequestsToday = dw.RequestCount;
                        status.TokensToday = dw.TokenCount;
                    }
                }

                status.RequestsPerMinuteLimit = limit.RequestsPerMinute;
                status.TokensPerMinuteLimit = limit.TokensPerMinute;
                status.RequestsPerDayLimit = limit.RequestsPerDay;
                status.TokensPerDayLimit = limit.TokensPerDay;

                result[provider] = status;
            }

            return result;
        }

        private class RateLimitWindow
        {
            public DateTime WindowStart { get; set; } = DateTime.UtcNow;
            public int RequestCount { get; set; }
            public int TokenCount { get; set; }

            public void Slide(DateTime now, TimeSpan windowSize)
            {
                if (now - WindowStart >= windowSize)
                {
                    WindowStart = now;
                    RequestCount = 0;
                    TokenCount = 0;
                }
            }
        }
    }

    public class RateLimitStatus
    {
        public string Provider { get; set; } = "";
        public int RequestsThisMinute { get; set; }
        public int TokensThisMinute { get; set; }
        public int RequestsToday { get; set; }
        public int TokensToday { get; set; }
        public int RequestsPerMinuteLimit { get; set; }
        public int TokensPerMinuteLimit { get; set; }
        public int RequestsPerDayLimit { get; set; }
        public int TokensPerDayLimit { get; set; }
    }
}
