using System.Collections.Generic;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-831: a token binding's <c>AllowedIps</c> compare with the client as IP addresses, IPv4-mapped reduced to IPv4.
/// Measured 2026-11-07: Kestrel bound to <c>[::]</c> reports an IPv4 client as <c>::ffff:127.0.0.1</c>, and the binding
/// compared text, so a binding listing <c>127.0.0.1</c> refused that client.
/// </summary>
public class Task831BindingAddressTests
{
    private sealed class Config : AuthenticationConfiguration;

    private static AuthenticationService Bound(params string[] allowedIps) => new(new Config
    {
        Enabled = true,
        TokenBindings = new List<TokenBinding> { new() { Token = "bound-token", AllowedIps = new List<string>(allowedIps) } },
    });

    [Theory]
    [InlineData("203.0.113.7", "::ffff:203.0.113.7")]   // a dual-stack socket's IPv4 client
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]   // the entry written mapped
    [InlineData("2001:db8::1", "2001:DB8:0:0::0001")]   // one IPv6 address, two spellings
    [InlineData("203.0.113.7", "203.0.113.7")]
    public void The_same_address_matches_however_it_is_written(string allowed, string client)
    {
        using var service = Bound(allowed);

        service.ValidateToken("bound-token", client).Should().BeTrue();
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.8")]
    [InlineData("203.0.113.7", "::ffff:203.0.113.8")]
    [InlineData("2001:db8::1", "2001:db8::2")]
    [InlineData("203.0.113.7", "not-an-address")]
    public void A_different_address_is_refused(string allowed, string client)
    {
        using var service = Bound(allowed);

        service.ValidateToken("bound-token", client).Should().BeFalse();
    }

    [Fact]
    public void An_entry_that_is_not_an_address_is_skipped_and_matches_nothing()
    {
        // Before, it was kept as text and matched a client "address" spelled the same way.
        using var service = Bound("unknown", "203.0.113.7");

        service.ValidateToken("bound-token", "unknown").Should().BeFalse();
        service.ValidateToken("bound-token", "203.0.113.7").Should().BeTrue();
    }
}
