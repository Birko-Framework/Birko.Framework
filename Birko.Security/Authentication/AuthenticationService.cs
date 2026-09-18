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
    public class AuthenticationService : IDisposable
    {
        private readonly AuthenticationConfiguration _config;
        private readonly ILogger<AuthenticationService>? _logger;
        private readonly HashSet<string> _expandedTokens;
        private readonly List<CachedTokenBinding> _expandedBindings;
        private readonly ReaderWriterLockSlim _lock;
        private bool _disposed;

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

            // SH-H040: refusing every request is the right answer to a misconfiguration, but doing it
            // silently is not — an operator would see uniform 401s with nothing pointing at the cause.
            if (IsMisconfigured)
            {
                _logger?.LogError(
                    "Authentication is ENABLED but no token or binding survived expansion, so every request "
                    + "will be rejected. Check that each configured token is non-empty and that any ${{VAR}} "
                    + "placeholder names a variable that is set to a non-blank value. To allow all callers "
                    + "deliberately, set Enabled = false instead.");
            }
        }

        /// <summary>
        /// Checks if authentication is enabled <b>and</b> at least one token or binding survived expansion.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>This is not the question a gate should ask</b> (SH-H040). It answers "enabled AND
        /// configured", so it returns <c>false</c> for two states that must be treated oppositely: auth
        /// deliberately switched off, and auth switched on but misconfigured to nothing. A caller that
        /// reads it as "enabled" and allows everything when it is false serves an open endpoint on a
        /// misconfiguration. Gate on <see cref="IsAuthenticationDisabled"/> instead, and use
        /// <see cref="IsMisconfigured"/> to report the bad state.
        /// <para>
        /// Its return value is deliberately unchanged: five transport wrappers expose it publicly and a
        /// test pins all three of its states.
        /// </para>
        /// </remarks>
        /// <returns>True if authentication is enabled and something is configured; otherwise, false</returns>
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
        /// True when the operator has deliberately switched authentication off — the <b>only</b> state in
        /// which a gate may allow every caller through.
        /// </summary>
        /// <remarks>
        /// SH-H040. This is the opt-out that makes refusing a misconfiguration legitimate rather than a
        /// wall: <c>Enabled = false</c> is an explicit decision, whereas <c>Enabled = true</c> with nothing
        /// configured is a mistake. Reads only <c>_config.Enabled</c>, so it needs no lock — the expanded
        /// collections are not consulted, which is precisely the point.
        /// </remarks>
        public bool IsAuthenticationDisabled => !_config.Enabled;

        /// <summary>
        /// True when authentication is switched <b>on</b> but nothing usable survived expansion, so every
        /// request will be rejected.
        /// </summary>
        /// <remarks>
        /// SH-H040. Exposed because a service that refuses everything is as hard to diagnose as one that
        /// allows everything if it does so silently — the framework's report-rather-than-swallow rule. The
        /// constructor also logs this at error level.
        /// <para>
        /// The reachable causes are: <c>Enabled: true</c> with empty <c>Tokens</c> and empty
        /// <c>TokenBindings</c>; a <c>${VAR}</c> whose variable exists but is <b>blank</b>, since
        /// <c>GetEnvironmentVariable</c> then returns <c>""</c> rather than <c>null</c> and the
        /// <c>?? value</c> fallback in <see cref="ExpandEnvironmentVariable"/> never fires; or tokens that
        /// are whitespace in configuration. A <b>renamed or absent</b> variable is <i>not</i> a cause — it
        /// falls back to the literal <c>"${VAR}"</c>, which is kept, so authentication stays on and every
        /// real token is rejected. That distinction is the one the original finding got backwards.
        /// </para>
        /// </remarks>
        public bool IsMisconfigured
        {
            get
            {
                if (!_config.Enabled)
                {
                    return false;
                }

                _lock.EnterReadLock();
                try
                {
                    return _expandedTokens.Count == 0 && _expandedBindings.Count == 0;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
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
            // SH-H040: gate on the OPT-OUT, not on "enabled AND configured". The previous check was
            // `!IsAuthenticationEnabled()`, which is also false when authentication is switched ON and
            // misconfigured to nothing — so a config with the flag set and an empty token list allowed
            // every caller, including one presenting a null token. Only a deliberate `Enabled = false`
            // may allow all; a misconfiguration now falls through to the rejection at the end of this
            // method, which was unreachable until this line changed.
            if (IsAuthenticationDisabled)
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
                // CR-L336: reject an empty/whitespace variable name (e.g. "${}") rather than passing it to
                // Environment.GetEnvironmentVariable — treat the malformed token as a literal.
                if (string.IsNullOrWhiteSpace(envVar))
                {
                    return value;
                }
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
        /// Disposes the authentication service, releasing the ReaderWriterLockSlim's kernel handles.
        /// Implementing IDisposable makes this reachable by DI containers and `using` statements —
        /// previously the method existed but the class did not implement the interface, so it was
        /// never called and the lock leaked for the lifetime of every discarded instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lock?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
