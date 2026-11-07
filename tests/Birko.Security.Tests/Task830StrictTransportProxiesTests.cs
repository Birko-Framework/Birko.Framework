using System;
using System.Collections.Generic;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-830: a transport configuration's <see cref="AuthenticationConfiguration.TrustedProxies"/> is parsed strictly by
/// default, so a bad entry refuses at construction instead of leaving its proxy silently untrusted.
/// <see cref="TrustedProxyParsing.Lenient"/> opts back in to the old skip-and-log behaviour.
/// </summary>
public class Task830StrictTransportProxiesTests
{
    private sealed class TransportConfig : AuthenticationConfiguration;

    private static AuthenticationService Create(TrustedProxyParsing? mode, params string[] proxies)
    {
        var config = new TransportConfig { TrustedProxies = new List<string>(proxies) };
        if (mode != null) config.TrustedProxyParsing = mode.Value;
        return new AuthenticationService(config);
    }

    [Fact]
    public void Strict_is_the_default() =>
        new TransportConfig().TrustedProxyParsing.Should().Be(TrustedProxyParsing.Strict);

    [Theory]
    [InlineData("10.0.0.300")]
    [InlineData("10.0.0.0/40")]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.5")]
    public void A_bad_entry_refuses_at_construction_naming_the_configuration(string entry)
    {
        var create = () => Create(null, "10.0.0.5", entry);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage($"TransportConfig.TrustedProxies: *'{entry}'*TrustedProxyParsing = Lenient*")
            .WithInnerException<FormatException>();
    }

    [Fact]
    public void An_unset_variable_refuses()
    {
        var entry = "${BIRKO_T830_UNSET_" + Guid.NewGuid().ToString("N") + "}";

        var create = () => Create(null, entry);

        create.Should().Throw<InvalidOperationException>().WithMessage("*not set*");
    }

    [Fact]
    public void Lenient_skips_the_bad_entry_and_keeps_the_good_one()
    {
        using var service = Create(TrustedProxyParsing.Lenient, "10.0.0.300", "10.0.0.5");

        service.ResolveClientIp(h => h == "X-Forwarded-For" ? "203.0.113.7" : null, "10.0.0.5").Should().Be("203.0.113.7");
    }

    [Fact]
    public void A_valid_list_still_constructs_and_walks()
    {
        using var service = Create(null, "10.0.0.0/24", "::1");

        service.ResolveClientIp(h => h == "X-Forwarded-For" ? "203.0.113.7" : null, "10.0.0.5").Should().Be("203.0.113.7");
    }

    [Fact]
    public void The_static_walk_is_lenient_by_default_and_strict_on_request()
    {
        string? Header(string name) => name == "X-Forwarded-For" ? "203.0.113.7" : null;

        AuthenticationService.GetClientIpAddress(Header, "10.0.0.5", new[] { "garbage", "10.0.0.5" }).Should().Be("203.0.113.7");

        var strict = () => AuthenticationService.GetClientIpAddress(Header, "10.0.0.5", new[] { "garbage" }, TrustedProxyParsing.Strict);
        strict.Should().Throw<FormatException>().WithMessage("*'garbage'*");
    }
}
