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
/// TASK-831: on a dual-stack socket the connection address of an IPv4 client is IPv4-mapped (measured on Kestrel bound
/// to <c>[::]</c>: <c>::ffff:127.0.0.1</c>). The WebSocket token binding passes it on as is, so a binding listing the
/// plain IPv4 address must still accept it.
/// </summary>
public class Task831MappedClientTests
{
    private static WebSocketAuthenticationService Service() => new(
        Options.Create(new WebSocketAuthenticationConfiguration
        {
            Enabled = true,
            TokenBindings = new List<TokenBinding> { new() { Token = "bound-token", AllowedIps = new List<string> { "203.0.113.7" } } },
        }),
        NullLogger<WebSocketAuthenticationService>.Instance,
        NullLogger<AuthenticationService>.Instance);

    [Theory]
    [InlineData(true)]   // behind AddBirkoForwardedHeaders: the short-circuit returns RemoteIpAddress as is
    [InlineData(false)]  // no registration: an untrusted peer's own address, also as is
    public void A_mapped_client_matches_a_binding_on_its_ipv4_address(bool forwardedHeadersRan)
    {
        using var service = Service();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.7");
        if (forwardedHeadersRan) context.Items[TrustedProxySettings.ResolvedItemKey] = true;

        var clientIp = service.GetClientIpAddress(context);

        clientIp.Should().Be("::ffff:203.0.113.7");
        service.ValidateToken("bound-token", clientIp).Should().BeTrue();
    }
}
