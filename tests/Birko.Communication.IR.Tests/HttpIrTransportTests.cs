using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Birko.Communication.IR.Protocols;
using Birko.Communication.IR.Transports;

namespace Birko.Communication.IR.Tests;

public class HttpIrTransportTests
{
    /// <summary>Captures the outgoing request body and returns a canned response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public string? LastBody { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public int SendCount { get; private set; }

        public CapturingHandler(HttpStatusCode status = HttpStatusCode.OK) => _status = status;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequest = request;
            if (request.Content != null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(_status);
        }
    }

    /// <summary>HttpClient that records whether it was disposed.</summary>
    private sealed class DisposeTrackingHttpClient : HttpClient
    {
        public bool DisposedFlag { get; private set; }
        public DisposeTrackingHttpClient(HttpMessageHandler handler) : base(handler) { }
        protected override void Dispose(bool disposing)
        {
            DisposedFlag = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task TransmitAsync_MapsMarksPositive_SpacesNegative()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var transport = new HttpIrTransport("http://192.168.1.100/", http);
        await transport.ConnectAsync();

        // marks at even indices, spaces at odd
        var timing = new IrTiming(new[] { 9000, 4500, 560, 1690 }, 38000) { RepeatCount = 2 };
        await transport.TransmitAsync(timing);

        handler.LastBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;

        var command = root.GetProperty("command").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        command.Should().Equal(9000, -4500, 560, -1690);
        root.GetProperty("carrier_frequency").GetInt32().Should().Be(38000);
        root.GetProperty("repeat").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task TransmitAsync_PostsToEspHomeTransmitRawEndpoint()
    {
        var handler = new CapturingHandler();
        var transport = new HttpIrTransport("http://host", new HttpClient(handler));
        await transport.ConnectAsync();

        await transport.TransmitAsync(new IrTiming(new[] { 100, 200 }, 38000));

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Be("http://host/api/services/remote_transmitter/transmit_raw");
    }

    [Fact]
    public async Task TransmitAsync_NotConnected_Throws()
    {
        var transport = new HttpIrTransport("http://host", new HttpClient(new CapturingHandler()));
        var act = async () => await transport.TransmitAsync(new IrTiming(new[] { 100, 200 }, 38000));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TransmitAsync_NonSuccessResponse_Throws()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var transport = new HttpIrTransport("http://host", new HttpClient(handler));
        await transport.ConnectAsync();

        var act = async () => await transport.TransmitAsync(new IrTiming(new[] { 100, 200 }, 38000));
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task TransmitAsync_WithApiPassword_AddsBearerHeader()
    {
        var handler = new CapturingHandler();
        var transport = new HttpIrTransport("http://host", new HttpClient(handler), "secret-token");
        await transport.ConnectAsync();

        await transport.TransmitAsync(new IrTiming(new[] { 100, 200 }, 38000));

        handler.LastRequest!.Headers.Authorization?.ToString()
            .Should().Be("Bearer secret-token");
    }

    [Fact]
    public void StartReceiveAsync_NotSupported()
    {
        var transport = new HttpIrTransport("http://host", new HttpClient(new CapturingHandler()));
        // The method throws synchronously (before returning a Task), so assert with a plain Action.
        Action act = () => { _ = transport.StartReceiveAsync(); };
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task ConnectDisconnect_TogglesIsConnected()
    {
        var transport = new HttpIrTransport("http://host", new HttpClient(new CapturingHandler()));
        transport.IsConnected.Should().BeFalse();

        await transport.ConnectAsync();
        transport.IsConnected.Should().BeTrue();

        await transport.DisconnectAsync();
        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Ctor_NullBaseUrl_Throws()
    {
        var act = () => new HttpIrTransport(null!, new HttpClient(new CapturingHandler()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Dispose_InjectedHttpClient_IsNotDisposed()
    {
        // The injected-client ctor sets ownsHttpClient = false, so Dispose must leave it alone.
        var tracking = new DisposeTrackingHttpClient(new CapturingHandler());
        var transport = new HttpIrTransport("http://host", tracking);

        transport.Dispose();

        tracking.DisposedFlag.Should().BeFalse("an injected HttpClient is owned by the caller");
        tracking.Dispose();
    }

    [Fact]
    public void Dispose_OwnedHttpClient_DoesNotThrow()
    {
        // The single-arg ctor creates and owns its own HttpClient; Dispose should dispose it cleanly.
        var transport = new HttpIrTransport("http://host");
        var act = () => transport.Dispose();
        act.Should().NotThrow();
    }
}
