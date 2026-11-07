using System;
using System.Collections.Generic;
using System.Net;
using Birko.Communication.WebSocket.Services;
using Birko.Security.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-829: behind <c>AddBirkoForwardedHeaders</c> the WebSocket token binding takes the address the forwarded headers
/// produced, and refuses a trusted-proxy list of its own that differs from the host's.
/// </summary>
public class Task829ForwardedHeadersTests
{
    private static WebSocketAuthenticationService NewService(string[] ownProxies, string[]? hostProxies = null)
    {
        var config = new WebSocketAuthenticationConfiguration { TrustedProxies = new List<string>(ownProxies) };
        var host = hostProxies == null ? null : new TrustedProxySettings { TrustedProxies = new List<string>(hostProxies) };
        return new WebSocketAuthenticationService(
            Options.Create(config),
            NullLogger<WebSocketAuthenticationService>.Instance,
            NullLogger<AuthenticationService>.Instance,
            host);
    }

    private static DefaultHttpContext Request(string peer, bool resolved)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        context.Request.Headers["X-Real-IP"] = "1.2.3.4";
        if (resolved) context.Items[TrustedProxySettings.ResolvedItemKey] = true;
        return context;
    }

    [Fact]
    public void Once_the_forwarded_headers_ran_the_connection_address_is_the_client()
    {
        using var service = NewService(new[] { "10.0.0.0/8" }, new[] { "10.0.0.0/8" });

        service.GetClientIpAddress(Request("10.0.0.5", resolved: true)).Should().Be("10.0.0.5");
    }

    [Fact]
    public void Without_the_marker_the_headers_are_walked_as_before()
    {
        using var service = NewService(new[] { "10.0.0.0/8" });

        service.GetClientIpAddress(Request("10.0.0.5", resolved: false)).Should().Be("203.0.113.7");
    }

    [Fact]
    public void A_list_that_differs_from_the_hosts_refuses()
    {
        var create = () => NewService(new[] { "10.0.0.5" }, new[] { "10.0.0.6" });

        create.Should().Throw<InvalidOperationException>().WithMessage("*10.0.0.5*10.0.0.6*");
    }

    [Fact]
    public void A_non_empty_list_where_the_host_trusts_nobody_refuses()
    {
        var create = () => NewService(new[] { "10.0.0.5" }, Array.Empty<string>());

        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_empty_list_or_the_same_list_is_accepted()
    {
        using var empty = NewService(Array.Empty<string>(), new[] { "10.0.0.5" });
        using var same = NewService(new[] { "::ffff:10.0.0.5" }, new[] { "10.0.0.5" });
        using var noHost = NewService(new[] { "10.0.0.5" });
    }
}
