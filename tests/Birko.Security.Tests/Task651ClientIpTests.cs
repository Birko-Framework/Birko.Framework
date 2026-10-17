using System;
using System.Collections.Generic;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>TASK-651: behind a trusted proxy only an address is a client address; a non-address hop never is.</summary>
public class Task651ClientIpTests
{
    private static Func<string, string?> Headers(params (string Name, string Value)[] headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers) map[name] = value;
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    private static readonly string[] Trusted = { "10.0.0.1" };

    [Fact]
    public void A_hop_that_is_not_an_address_answers_with_the_peer()
        => AuthenticationService.GetClientIpAddress(Headers(("X-Forwarded-For", "1.2.3.4, unknown")), "10.0.0.1", Trusted)
            .Should().Be("10.0.0.1", "'unknown' was returned as the client address");

    [Fact]
    public void A_hop_with_a_port_answers_with_the_address_only()
        => AuthenticationService.GetClientIpAddress(Headers(("X-Forwarded-For", "203.0.113.7:51234")), "10.0.0.1", Trusted)
            .Should().Be("203.0.113.7");

    [Fact]
    public void A_proxy_header_that_is_not_an_address_is_skipped()
    {
        AuthenticationService.GetClientIpAddress(Headers(("X-Real-IP", "garbage"), ("CF-Connecting-IP", "198.51.100.4")), "10.0.0.1", Trusted)
            .Should().Be("198.51.100.4");
        AuthenticationService.GetClientIpAddress(Headers(("X-Real-IP", "garbage")), "10.0.0.1", Trusted).Should().Be("10.0.0.1");
    }

    [Fact]
    public void A_normal_chain_still_answers_with_the_first_untrusted_hop()
        => AuthenticationService.GetClientIpAddress(Headers(("X-Forwarded-For", "198.51.100.9, 10.0.0.1")), "10.0.0.1", Trusted)
            .Should().Be("198.51.100.9");
}
