using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Birko.Security.Authentication
{
    /// <summary>
    /// Protocol-agnostic authentication service with token and IP binding support.
    /// Thread-safe and caches expanded environment variables for performance.
    /// </summary>
    public class AuthenticationService
    {
        private readonly AuthenticationConfiguration _config;
        private readonly ILogger<AuthenticationService>? _logger;
        private readonly HashSet<string> _expandedTokens;
        private readonly List<CachedTokenBinding> _expandedBindings;
        private readonly ReaderWriterLockSlim _lock;

        /// <summary>
        /// Cached token binding with pre-expanded values
        /// </summary>
        private class CachedTokenBinding
        {
            public string Token { get; set; } = string.Empty;
            public HashSet<string> AllowedIps { get; set; } = new();
        }

        /// <summary>
        /// Initializes a new instance of the AuthenticationService class
        /// </summary>
        /// <param name="config">The authentication configuration</param>
        /// <param name="logger">Optional logger for diagnostics</param>
        public AuthenticationService(
            AuthenticationConfiguration config,
            ILogger<AuthenticationService>? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
            _expandedTokens = new HashSet<string>(StringComparer.Ordinal);
            _expandedBindings = new List<CachedTokenBinding>();
            _lock = new ReaderWriterLockSlim();

            // Initialize cache immediately to avoid first-request latency
            InitializeCache();
        }

        /// <summary>
        /// Checks if authentication is enabled and tokens are configured
        /// </summary>
        /// <returns>True if authentication is enabled; otherwise, false</returns>
        public bool IsAuthenticationEnabled()
        {
            _lock.EnterReadLock();
            try
            {
                return _config.Enabled && (_expandedTokens.Count > 0 || _expandedBindings.Count > 0);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Validates a token against the configured tokens and optional IP binding
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <param name="clientIp">The client IP address (required for IP-bound tokens)</param>
        /// <returns>True if the token is valid; otherwise, false</returns>
        public bool ValidateToken(string? token, string? clientIp)
        {
            // If authentication is not enabled, allow all connections
            if (!IsAuthenticationEnabled())
            {
                return true;
            }

            // If authentication is enabled but no token provided, reject
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger?.LogWarning("Connection attempt without token from IP: {ClientIp}", clientIp ?? "unknown");
                return false;
            }

            _lock.EnterReadLock();
            try
            {
                // First, check token bindings (token + IP validation)
                if (_expandedBindings.Count > 0)
                {
                    foreach (var binding in _expandedBindings)
                    {
                        if (binding.Token == token)
                        {
                            // Token matches, now check IP
                            if (string.IsNullOrWhiteSpace(clientIp))
                            {
                                _logger?.LogWarning("Token matched but client IP is unknown for token binding validation");
                                return false;
                            }

                            if (binding.AllowedIps.Contains(clientIp))
                            {
                                _logger?.LogInformation("Authenticated token with IP binding: {ClientIp}", clientIp);
                                return true;
                            }
                            else
                            {
                                _logger?.LogWarning("Token valid but IP {ClientIp} not in allowed list for this token", clientIp);
                                return false;
                            }
                        }
                    }
                }

                // Fall back to simple token validation (no IP binding)
                if (_expandedTokens.Count > 0)
                {
                    var isValid = _expandedTokens.Contains(token);

                    if (isValid)
                    {
                        _logger?.LogInformation("Authenticated token (no IP binding) from IP: {ClientIp}", clientIp ?? "unknown");
                    }
                    else
                    {
                        _logger?.LogWarning("Invalid token attempt from IP: {ClientIp}", clientIp ?? "unknown");
                    }

                    return isValid;
                }

                _logger?.LogWarning("Authentication enabled but no tokens or bindings configured");
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Extracts the client IP address from common forwarded headers
        /// </summary>
        /// <param name="getHeaderValue">Function to get header values</param>
        /// <param name="fallbackIp">Fallback direct connection IP</param>
        /// <returns>The client IP address or null</returns>
        public static string? GetClientIpAddress(Func<string, string?> getHeaderValue, string? fallbackIp)
        {
            // Check for forwarded IP (behind proxy/load balancer)
            var forwardedFor = getHeaderValue("X-Forwarded-For");
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, take the first one (original client)
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ips.Length > 0)
                {
                    return ips[0];
                }
            }

            // Check for X-Real-IP header (nginx)
            var realIp = getHeaderValue("X-Real-IP");
            if (!string.IsNullOrWhiteSpace(realIp))
            {
                return realIp;
            }

            // Check for CF-Connecting-IP header (Cloudflare)
            var cfIp = getHeaderValue("CF-Connecting-IP");
            if (!string.IsNullOrWhiteSpace(cfIp))
            {
                return cfIp;
            }

            // Fall back to direct connection IP
            return fallbackIp;
        }

        /// <summary>
        /// Expands environment variables in format ${ENV_VAR}
        /// </summary>
        /// <param name="value">The value potentially containing environment variables</param>
        /// <returns>The expanded value</returns>
        public static string ExpandEnvironmentVariable(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Check if value is in format ${ENV_VAR}
            if (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
            {
                var envVar = value.Substring(2, value.Length - 3);
                return Environment.GetEnvironmentVariable(envVar) ?? value;
            }

            return value;
        }

        private void InitializeCache()
        {
            _lock.EnterWriteLock();
            try
            {
                _expandedTokens.Clear();
                _expandedBindings.Clear();

                // Cache expanded simple tokens
                foreach (var token in _config.Tokens)
                {
                    var expanded = ExpandEnvironmentVariable(token);
                    if (!string.IsNullOrWhiteSpace(expanded))
                    {
                        _expandedTokens.Add(expanded);
                    }
                }

                // Cache expanded token bindings
                foreach (var binding in _config.TokenBindings)
                {
                    var expandedToken = ExpandEnvironmentVariable(binding.Token);
                    if (string.IsNullOrWhiteSpace(expandedToken))
                        continue;

                    var cachedBinding = new CachedTokenBinding
                    {
                        Token = expandedToken,
                        AllowedIps = new HashSet<string>(StringComparer.Ordinal)
                    };

                    foreach (var ip in binding.AllowedIps)
                    {
                        var expandedIp = ExpandEnvironmentVariable(ip);
                        if (!string.IsNullOrWhiteSpace(expandedIp))
                        {
                            cachedBinding.AllowedIps.Add(expandedIp);
                        }
                    }

                    _expandedBindings.Add(cachedBinding);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Disposes the authentication service
        /// </summary>
        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}
