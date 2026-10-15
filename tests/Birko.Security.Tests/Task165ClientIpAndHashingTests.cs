using System;
using System.Collections.Generic;
using System.Diagnostics;
using Birko.Security.Authentication;
using Birko.Security.Hashing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-165: SH-M249 (forwarding headers believed from anyone), SH-M250 (a binding with no address left is silent) and
/// SH-M246 (an unbounded stored PBKDF2 iteration count).
/// </summary>
public class Task165ClientIpAndHashingTests
{
    private sealed class TestAuthConfig : AuthenticationConfiguration { }

    private static Func<string, string?> Headers(params (string Name, string Value)[] headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers) map[name] = value;
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    // ── SH-M249 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_bound_token_is_not_unlocked_by_a_forged_forwarded_header()
    {
        var config = new TestAuthConfig
        {
            Enabled = true,
            TokenBindings = { new TokenBinding { Token = "bound", AllowedIps = { "10.0.0.1" } } },
        };
        using var service = new AuthenticationService(config);

        var ip = service.ResolveClientIp(Headers(("X-Forwarded-For", "10.0.0.1")), "203.0.113.9");

        ip.Should().Be("203.0.113.9", "no proxy is trusted, so the header is the caller's own claim");
        service.ValidateToken("bound", ip).Should().BeFalse();
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Real-IP")]
    [InlineData("CF-Connecting-IP")]
    public void With_no_trusted_proxy_every_forwarding_header_is_ignored(string header)
    {
        AuthenticationService.GetClientIpAddress(Headers((header, "10.0.0.1")), "203.0.113.9")
            .Should().Be("203.0.113.9");
    }

    [Fact]
    public void A_header_from_a_peer_that_is_not_a_trusted_proxy_is_ignored()
    {
        AuthenticationService.GetClientIpAddress(Headers(("X-Forwarded-For", "10.0.0.1")), "203.0.113.9", new[] { "10.0.0.5" })
            .Should().Be("203.0.113.9");
    }

    [Fact]
    public void Behind_a_trusted_proxy_the_rightmost_untrusted_hop_is_the_client()
    {
        // The left end is whatever the client wrote; the proxy appended the address it saw.
        AuthenticationService.GetClientIpAddress(
                Headers(("X-Forwarded-For", "10.0.0.1, 198.51.100.7")), "10.0.0.5", new[] { "10.0.0.5" })
            .Should().Be("198.51.100.7");
    }

    [Fact]
    public void A_trusted_range_covers_a_chain_of_proxies()
    {
        AuthenticationService.GetClientIpAddress(
                Headers(("X-Forwarded-For", "10.0.0.1, 198.51.100.7, 10.9.9.9")), "10.1.2.3", new[] { "10.0.0.0/8" })
            .Should().Be("198.51.100.7");
    }

    [Fact]
    public void A_trusted_proxy_with_a_mapped_or_ported_address_is_recognised()
    {
        AuthenticationService.GetClientIpAddress(Headers(("X-Real-IP", "198.51.100.7")), "::ffff:10.0.0.5", new[] { "10.0.0.5" })
            .Should().Be("198.51.100.7");
        AuthenticationService.GetClientIpAddress(Headers(("X-Real-IP", "198.51.100.7")), "10.0.0.5:51234", new[] { "10.0.0.5" })
            .Should().Be("198.51.100.7");
    }

    [Fact]
    public void A_configured_trusted_proxy_lets_its_forwarded_client_satisfy_a_binding()
    {
        var config = new TestAuthConfig
        {
            Enabled = true,
            TrustedProxies = { "10.0.0.5" },
            TokenBindings = { new TokenBinding { Token = "bound", AllowedIps = { "198.51.100.7" } } },
        };
        using var service = new AuthenticationService(config);

        var ip = service.ResolveClientIp(Headers(("X-Forwarded-For", "198.51.100.7")), "10.0.0.5");

        service.ValidateToken("bound", ip).Should().BeTrue();
    }

    // ── SH-M250 ──────────────────────────────────────────────────────────────────────────────────

    private sealed class ListLogger : ILogger<AuthenticationService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void A_binding_with_no_address_left_after_expansion_is_reported()
    {
        var logger = new ListLogger();
        var config = new TestAuthConfig
        {
            Enabled = true,
            TokenBindings = { new TokenBinding { Token = "bound", AllowedIps = { "", "  " } } },
        };
        using var service = new AuthenticationService(config, logger);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("no allowed IP"));
        service.ValidateToken("bound", "10.0.0.1").Should().BeFalse("the binding still refuses; it is now said why");
    }

    // ── SH-M246 ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(20_000_000)]
    public void A_stored_iteration_count_above_the_ceiling_fails_without_running_it(int iterations)
    {
        var hasher = new Pbkdf2PasswordHasher();
        var salt = Convert.ToBase64String(new byte[16]);
        var hash = Convert.ToBase64String(new byte[32]);

        var watch = Stopwatch.StartNew();
        var verified = hasher.Verify("password", $"PBKDF2-SHA512:{iterations}:{salt}:{hash}");
        watch.Stop();

        verified.Should().BeFalse();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "the count is refused, not derived");
    }

    [Fact]
    public void A_hash_made_by_this_hasher_still_verifies()
    {
        var hasher = new Pbkdf2PasswordHasher(10_000);
        hasher.Verify("pw", hasher.Hash("pw")).Should().BeTrue();
    }
}
