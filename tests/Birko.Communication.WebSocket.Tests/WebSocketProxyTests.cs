using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-542. <c>MapWebSocketProxy</c> relays a client to an upstream WebSocket server. DraCode's hand-written copy disabled
/// TLS validation unconditionally, concatenated the token into the query unencoded, accepted the client before knowing
/// the upstream would answer, and replaced every close status with NormalClosure. Each test runs a real Kestrel upstream
/// and a real Kestrel proxy, so the frames cross two actual sockets.
/// </summary>
public class WebSocketProxyTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<WebApplication> _apps = new();
    private readonly Upstream _upstream = new();
    private Uri _upstreamBase = null!;

    public async Task InitializeAsync()
    {
        _upstreamBase = await StartAsync(app =>
        {
            app.Map("/refuse", (HttpContext c) => Results.StatusCode(StatusCodes.Status401Unauthorized));
            app.Map("/ws", _upstream.HandleAsync);
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

    private async Task<Uri> StartProxyAsync(string upstreamPath = "/ws", Action<WebSocketProxyOptions>? configure = null)
    {
        var upstream = Ws(_upstreamBase, upstreamPath);
        var proxyBase = await StartAsync(app => app.MapWebSocketProxy("/proxy", upstream, configure));
        return Ws(proxyBase, "/proxy");
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri uri)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(uri, CancellationToken.None).WaitAsync(Timeout);
        return client;
    }

    private static async Task<(WebSocketReceiveResult result, byte[] data)> ReceiveFrameAsync(SysWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).WaitAsync(Timeout);
        return (result, buffer.Take(result.Count).ToArray());
    }

    private static Task SendAsync(SysWebSocket socket, string text, bool endOfMessage = true) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage, CancellationToken.None);

    // ---- relay ----

    [Fact]
    public async Task TextAndBinary_AreRelayedBothWays()
    {
        using var client = await ConnectAsync(await StartProxyAsync());

        await SendAsync(client, "hello");
        var (text, textData) = await ReceiveFrameAsync(client);
        await client.SendAsync(new byte[] { 1, 2, 3, 250 }, WebSocketMessageType.Binary, true, CancellationToken.None);
        var (binary, binaryData) = await ReceiveFrameAsync(client);

        text.MessageType.Should().Be(WebSocketMessageType.Text);
        Encoding.UTF8.GetString(textData).Should().Be("hello", "the upstream echoes each frame, so this crossed the proxy twice");
        binary.MessageType.Should().Be(WebSocketMessageType.Binary);
        binaryData.Should().Equal(1, 2, 3, 250);
        _upstream.Frames.Select(f => f.Type).Should().Equal(WebSocketMessageType.Text, WebSocketMessageType.Binary);
    }

    [Fact]
    public async Task FragmentedMessage_ReachesUpstreamAsTheSameFrames_WithEndOfMessageOnTheLast()
    {
        using var client = await ConnectAsync(await StartProxyAsync());

        await SendAsync(client, "one-", endOfMessage: false);
        await SendAsync(client, "two-", endOfMessage: false);
        await SendAsync(client, "three", endOfMessage: true);
        var echoes = new List<(WebSocketReceiveResult result, byte[] data)>();
        do
        {
            echoes.Add(await ReceiveFrameAsync(client));
        } while (!echoes[^1].result.EndOfMessage);

        _upstream.Frames.Select(f => f.EndOfMessage).Should().Equal(false, false, true);
        Encoding.UTF8.GetString(_upstream.Frames.SelectMany(f => f.Data).ToArray()).Should().Be("one-two-three");
        echoes.Select(e => e.result.EndOfMessage).Should().Equal(new[] { false, false, true }, "the way back keeps the fragmentation too");
    }

    // ---- close propagation ----

    [Fact]
    public async Task ClientClose_ReachesUpstream_WithItsStatusAndDescription()
    {
        using var client = await ConnectAsync(await StartProxyAsync());

        await client.CloseAsync((WebSocketCloseStatus)4001, "client leaving", CancellationToken.None).WaitAsync(Timeout);

        var (status, description) = await _upstream.Closed.Task.WaitAsync(Timeout);
        status.Should().Be((WebSocketCloseStatus)4001);
        description.Should().Be("client leaving");
        client.State.Should().Be(WebSocketState.Closed, "the upstream's acknowledgement was relayed back, completing the handshake");
    }

    [Fact]
    public async Task UpstreamClose_ReachesClient_WithItsStatusAndDescription()
    {
        using var client = await ConnectAsync(await StartProxyAsync());

        await SendAsync(client, "close-me");
        var (result, _) = await ReceiveFrameAsync(client);

        result.MessageType.Should().Be(WebSocketMessageType.Close);
        result.CloseStatus.Should().Be((WebSocketCloseStatus)4002);
        result.CloseStatusDescription.Should().Be("upstream done");
    }

    [Fact]
    public async Task UpstreamDroppingWithoutClose_ClosesClient_EndpointUnavailable()
    {
        using var client = await ConnectAsync(await StartProxyAsync());

        await SendAsync(client, "drop");
        var (result, _) = await ReceiveFrameAsync(client);

        result.MessageType.Should().Be(WebSocketMessageType.Close);
        result.CloseStatus.Should().Be(WebSocketCloseStatus.EndpointUnavailable);
    }

    // ---- upstream refusal ----

    [Fact]
    public async Task UpstreamRefusal_Answers502_WithoutUpgradingTheClient()
    {
        var proxy = await StartProxyAsync("/refuse");
        using var client = new ClientWebSocket();
        client.Options.CollectHttpResponseDetails = true;

        var connect = () => client.ConnectAsync(proxy, CancellationToken.None).WaitAsync(Timeout);

        await connect.Should().ThrowAsync<WebSocketException>();
        client.HttpStatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task UnreachableUpstream_Answers502()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var deadPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var proxyBase = await StartAsync(app => app.MapWebSocketProxy("/proxy", new Uri($"ws://127.0.0.1:{deadPort}/ws")));
        using var client = new ClientWebSocket();
        client.Options.CollectHttpResponseDetails = true;

        var connect = () => client.ConnectAsync(Ws(proxyBase, "/proxy"), CancellationToken.None).WaitAsync(Timeout);

        await connect.Should().ThrowAsync<WebSocketException>();
        client.HttpStatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task PlainHttpRequest_Gets400()
    {
        var proxy = await StartProxyAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync(new UriBuilder(proxy) { Scheme = "http" }.Uri).WaitAsync(Timeout);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _upstream.Connections.Should().Be(0);
    }

    // ---- credentials ----

    [Fact]
    public async Task Credential_DefaultsToABearerHeader_NotTheQuery()
    {
        using var client = await ConnectAsync(await StartProxyAsync(configure: o => o.Credential = _ => "s3cret"));

        _upstream.Authorization.Should().Be("Bearer s3cret");
        _upstream.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task Credential_AsQuery_IsUrlEncoded_AndKeepsTheUpstreamQuery()
    {
        const string awkward = "a&b=c d+e/?#%";
        var upstream = new UriBuilder(Ws(_upstreamBase, "/ws")) { Query = "room=7" }.Uri;
        var proxyBase = await StartAsync(app => app.MapWebSocketProxy("/proxy", upstream, o =>
        {
            o.Credential = _ => awkward;
            o.CredentialTransport = WebSocketProxyCredentialTransport.QueryString;
        }));

        using var client = await ConnectAsync(Ws(proxyBase, "/proxy"));

        _upstream.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["room"] = "7", ["token"] = awkward },
            "an unencoded '&' or '=' would have split the token into extra parameters");
        _upstream.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Credential_CanBeTakenFromTheClientRequest()
    {
        var proxy = await StartProxyAsync(configure: o => o.Credential = c => c.Request.Query["token"]);

        using var client = await ConnectAsync(new UriBuilder(proxy) { Query = "token=from-client" }.Uri);

        _upstream.Authorization.Should().Be("Bearer from-client");
    }

    // ---- options ----

    [Fact]
    public void CertificateValidation_IsNotDisabledByDefault()
    {
        using var socket = new ClientWebSocket();

        new WebSocketProxyOptions(new Uri("wss://example.test/ws")).ConfigureUpstream(socket.Options, credential: null);

        socket.Options.RemoteCertificateValidationCallback.Should().BeNull("the platform's validation applies unless the unsafe flag is set");
    }

    [Fact]
    public void CertificateValidation_IsDisabledOnlyByTheDangerousFlag()
    {
        using var socket = new ClientWebSocket();

        new WebSocketProxyOptions(new Uri("wss://example.test/ws")) { DangerousAcceptAnyServerCertificate = true }
            .ConfigureUpstream(socket.Options, credential: null);

        socket.Options.RemoteCertificateValidationCallback.Should().NotBeNull();
    }

    [Theory]
    [InlineData("http://h:81/p", "ws://h:81/p")]
    [InlineData("https://h/p", "wss://h/p")]
    [InlineData("ws://h/p", "ws://h/p")]
    [InlineData("wss://h:9443/p?x=1", "wss://h:9443/p?x=1")]
    public void Upstream_SchemeIsNormalisedToWebSocket(string given, string expected)
    {
        new WebSocketProxyOptions(new Uri(given)).Upstream.Should().Be(new Uri(expected));
    }

    [Theory]
    [InlineData("ftp://h/p")]
    [InlineData("/relative")]
    public void Upstream_ThatIsNotAWebSocketAddress_IsRefused(string given)
    {
        var create = () => new WebSocketProxyOptions(new Uri(given, UriKind.RelativeOrAbsolute));

        create.Should().Throw<ArgumentException>();
    }

    /// <summary>Echoes every frame as it arrives; "close-me" closes with 4002, "drop" aborts without a close.</summary>
    private sealed class Upstream
    {
        private int _connections;

        public ConcurrentQueue<(WebSocketMessageType Type, bool EndOfMessage, byte[] Data)> Frames { get; } = new();
        public TaskCompletionSource<(WebSocketCloseStatus?, string?)> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Authorization { get; private set; }
        public Dictionary<string, string> Query { get; private set; } = new();
        public int Connections => _connections;

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            Interlocked.Increment(ref _connections);
            Authorization = context.Request.Headers.Authorization.FirstOrDefault();
            Query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Closed.TrySetResult((result.CloseStatus, result.CloseStatusDescription));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return;
                }

                var data = buffer.Take(result.Count).ToArray();
                Frames.Enqueue((result.MessageType, result.EndOfMessage, data));
                var text = result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(data) : null;

                if (text == "close-me")
                {
                    await socket.CloseAsync((WebSocketCloseStatus)4002, "upstream done", CancellationToken.None);
                    return;
                }

                if (text == "drop")
                {
                    socket.Abort();
                    context.Abort();
                    return;
                }

                await socket.SendAsync(new ArraySegment<byte>(data), result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        }
    }
}
