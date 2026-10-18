using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Proxy;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-569. <c>MapWebSocketProxy</c> connected upstream with no <c>Sec-WebSocket-Protocol</c> and accepted the client with
/// none, so a client that asked for a sub-protocol refused the handshake and could not use the proxy at all. Real Kestrel
/// upstream and proxy, as in <see cref="WebSocketProxyTests"/>.
/// </summary>
public class WebSocketProxySubProtocolTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string Supported = "graphql-transport-ws";

    private readonly List<WebApplication> _apps = new();
    private Uri _upstreamBase = null!;
    private IReadOnlyList<string> _offered = Array.Empty<string>();

    public async Task InitializeAsync()
    {
        _upstreamBase = await StartAsync(app =>
        {
            // Selects the first offered protocol it supports, or none.
            app.Map("/lenient", (HttpContext c) => EchoAsync(c, refuseUnsupported: false));
            // Refuses the handshake (400) unless a supported protocol was offered.
            app.Map("/strict", (HttpContext c) => EchoAsync(c, refuseUnsupported: true));
        });
    }

    public async Task DisposeAsync()
    {
        foreach (var app in _apps)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private async Task EchoAsync(HttpContext context, bool refuseUnsupported)
    {
        _offered = context.WebSockets.WebSocketRequestedProtocols.ToList();
        var selected = _offered.FirstOrDefault(p => p == Supported);
        if (selected == null && refuseUnsupported)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(selected);
        var buffer = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return;
            }
            await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
        }
    }

    private async Task<Uri> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        map(app);
        await app.StartAsync();
        _apps.Add(app);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Uri(address);
    }

    private static Uri Ws(Uri http, string path) => new UriBuilder(http) { Scheme = "ws", Path = path }.Uri;

    private async Task<Uri> ProxyTo(string upstreamPath)
    {
        var upstream = Ws(_upstreamBase, upstreamPath);
        var proxyBase = await StartAsync(app => app.MapWebSocketProxy("/proxy", upstream));
        return Ws(proxyBase, "/proxy");
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri uri, params string[] protocols)
    {
        var client = new ClientWebSocket();
        foreach (var p in protocols) client.Options.AddSubProtocol(p);
        await client.ConnectAsync(uri, CancellationToken.None).WaitAsync(Timeout);
        return client;
    }

    [Fact]
    public async Task A_requested_protocol_the_upstream_supports_is_negotiated_end_to_end()
    {
        using var client = await ConnectAsync(await ProxyTo("/lenient"), "mqtt", Supported);

        client.SubProtocol.Should().Be(Supported);
        _offered.Should().Equal(new[] { "mqtt", Supported }, "the client's protocols are offered upstream in its order");

        await client.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[64];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).WaitAsync(Timeout);
        Encoding.UTF8.GetString(buffer, 0, result.Count).Should().Be("ping");
    }

    [Fact]
    public async Task A_client_requesting_no_protocol_still_gets_none()
    {
        using var client = await ConnectAsync(await ProxyTo("/lenient"));

        client.SubProtocol.Should().BeNull();
        _offered.Should().BeEmpty();
    }

    [Fact]
    public async Task An_upstream_that_refuses_the_requested_protocol_is_answered_502()
    {
        var proxy = await ProxyTo("/strict");
        using var client = new ClientWebSocket();
        client.Options.AddSubProtocol("mqtt");
        client.Options.CollectHttpResponseDetails = true;

        var connect = () => client.ConnectAsync(proxy, CancellationToken.None).WaitAsync(Timeout);

        await connect.Should().ThrowAsync<WebSocketException>();
        client.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task An_upstream_that_selects_none_of_the_requested_protocols_accepts_the_client_with_none()
    {
        // The upstream accepted the handshake without choosing; the proxy passes that answer on. Whether the client then
        // accepts a connection with no protocol is the client's decision (.NET's ClientWebSocket does).
        using var client = await ConnectAsync(await ProxyTo("/lenient"), "mqtt");

        client.SubProtocol.Should().BeNull();
        _offered.Should().Equal("mqtt");
    }
}
