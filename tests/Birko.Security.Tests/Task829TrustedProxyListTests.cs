using System;
using System.Collections.Generic;
using System.Net;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-829: <see cref="TrustedProxyList"/>, the one parser behind every trusted-proxy list. Strict for the
/// forwarded-headers registration, lenient (as <see cref="AuthenticationService"/> always was) for the transports.
/// </summary>
public class Task829TrustedProxyListTests
{
    [Fact]
    public void Addresses_and_networks_are_matched_including_ipv4_mapped_peers()
    {
        var list = TrustedProxyList.Parse(new[] { "10.0.0.5", "172.18.0.0/16", "::1" });

        list.Contains(IPAddress.Parse("10.0.0.5")).Should().BeTrue();
        list.Contains(IPAddress.Parse("::ffff:10.0.0.5")).Should().BeTrue();
        list.Contains(IPAddress.Parse("172.18.44.2")).Should().BeTrue();
        list.Contains(IPAddress.Parse("::ffff:172.18.44.2")).Should().BeTrue();
        list.Contains(IPAddress.IPv6Loopback).Should().BeTrue();
        list.Contains(IPAddress.Parse("10.0.0.6")).Should().BeFalse();
        list.Contains(IPAddress.Parse("172.19.0.1")).Should().BeFalse();
        list.Contains(null).Should().BeFalse();
    }

    [Fact]
    public void Nothing_configured_trusts_nobody()
    {
        TrustedProxyList.Parse(null).IsEmpty.Should().BeTrue();
        TrustedProxyList.Parse(Array.Empty<string>()).IsEmpty.Should().BeTrue();
        TrustedProxyList.Parse(null).Contains(IPAddress.Loopback).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.300", "is not an IP address")]
    [InlineData("unknown", "is not an IP address")]
    [InlineData("10.0.0.5:8080", "is not an IP address")]
    [InlineData("10.0.0.0/40", "is not a CIDR network")]
    [InlineData("10.5", "is not an IP address")]
    [InlineData("[::1]:80", "is not an IP address")]
    [InlineData("[::1]", "is not an IP address")]
    [InlineData("0.0.0.0/0", "trusts every sender")]
    [InlineData("::/0", "trusts every sender")]
    [InlineData("  ", "is blank")]
    public void Strict_parse_names_the_entry_it_refuses(string entry, string reason)
    {
        var parse = () => TrustedProxyList.Parse(new[] { "10.0.0.5", entry });

        parse.Should().Throw<FormatException>().WithMessage($"*'{entry}' {reason}*");
    }

    [Fact]
    public void Strict_parse_refuses_a_variable_that_is_not_set()
    {
        // A unique name nobody sets, so no parallel test can make it exist.
        var entry = "${BIRKO_T829_UNSET_" + Guid.NewGuid().ToString("N") + "}";

        var parse = () => TrustedProxyList.Parse(new[] { entry });

        parse.Should().Throw<FormatException>().WithMessage("*not set*");
    }

    [Fact]
    public void A_set_variable_is_expanded()
    {
        var name = "BIRKO_T829_SET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "10.9.8.7");
        try
        {
            TrustedProxyList.Parse(new[] { "${" + name + "}" }).Contains(IPAddress.Parse("10.9.8.7")).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Lenient_parse_skips_and_reports_what_strict_refuses_and_keeps_an_address_with_a_port()
    {
        var skipped = new List<string>();

        var list = TrustedProxyList.ParseLenient(
            new[] { "10.0.0.300", "10.0.0.0/40", " ", "10.0.0.5:8080", "192.168.0.0/24" },
            (entry, _) => skipped.Add(entry));

        skipped.Should().Equal("10.0.0.300", "10.0.0.0/40", " ");
        list.Contains(IPAddress.Parse("10.0.0.5")).Should().BeTrue();
        list.Contains(IPAddress.Parse("192.168.0.9")).Should().BeTrue();
    }

    [Fact]
    public void Two_lists_are_the_same_when_they_trust_the_same_things_in_any_order()
    {
        var a = TrustedProxyList.Parse(new[] { "10.0.0.5", "172.18.0.0/16" });

        a.SameAs(TrustedProxyList.Parse(new[] { "172.18.0.0/16", "::ffff:10.0.0.5" })).Should().BeTrue();
        a.SameAs(TrustedProxyList.Parse(new[] { "10.0.0.5/32", "172.18.0.0/16" })).Should().BeTrue();
        a.SameAs(TrustedProxyList.Parse(new[] { "10.0.0.5" })).Should().BeFalse();
        a.SameAs(TrustedProxyList.Empty).Should().BeFalse();
    }

    [Fact]
    public void The_client_address_walk_is_unchanged_for_the_transports()
    {
        // SH-M249 and TASK-651 behaviour, now through the shared list.
        string? Header(string name) => name switch
        {
            "X-Forwarded-For" => "198.51.100.1, 203.0.113.7, 10.0.0.9",
            _ => null,
        };

        AuthenticationService.GetClientIpAddress(Header, "10.0.0.5", new[] { "10.0.0.0/24" }).Should().Be("203.0.113.7");
        AuthenticationService.GetClientIpAddress(Header, "10.0.0.5", new[] { "10.0.0.5" }).Should().Be("10.0.0.9");
        AuthenticationService.GetClientIpAddress(Header, "10.0.0.5", new[] { "garbage" }).Should().Be("10.0.0.5");
        AuthenticationService.GetClientIpAddress(Header, "10.0.0.5").Should().Be("10.0.0.5");
    }
}
