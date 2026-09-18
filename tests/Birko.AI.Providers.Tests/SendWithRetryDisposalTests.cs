using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using System.Net;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// Regression test for CR-M010: SendWithRetryAsync returned the live HttpResponseMessage to callers
/// (which only read StatusCode/body), so every non-streaming call leaked the response handle. The
/// body is fully buffered, so the response must be disposed inside SendWithRetryAsync before return.
/// </summary>
public class SendWithRetryDisposalTests
{
    private sealed class TrackingResponse : HttpResponseMessage
    {
        public bool Disposed { get; private set; }
        public TrackingResponse(HttpStatusCode code) : base(code) { }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class SingleResponseHandler : HttpMessageHandler
    {
        public TrackingResponse Response { get; }
        public SingleResponseHandler(HttpStatusCode code, string body)
        {
            Response = new TrackingResponse(code) { Content = new StringContent(body) };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<HttpResponseMessage>(Response);
        }
    }

    /// <summary>Minimal provider exposing the protected SendWithRetryAsync for testing.</summary>
    private sealed class TestProvider : LlmProviderBase
    {
        private readonly HttpClient _client;
        public TestProvider(HttpClient client) { _client = client; }
        public override string Name => "test";
        protected override bool IsConfigured() => true;
        public override Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { StopReason = "end_turn", Content = [] });

        public Task<(HttpResponseMessage? Response, string? ResponseBody)> CallSendWithRetry(CancellationToken ct = default)
            => SendWithRetryAsync(_client, () => new HttpRequestMessage(HttpMethod.Post, "http://localhost/x"), Name, ct);
    }

    [Fact]
    public async Task SendWithRetry_DisposesResponse_OnSuccess()
    {
        var handler = new SingleResponseHandler(HttpStatusCode.OK, "{\"ok\":true}");
        var provider = new TestProvider(new HttpClient(handler));

        var (response, body) = await provider.CallSendWithRetry();

        body.Should().Be("{\"ok\":true}");
        // StatusCode is still readable after Dispose (only Content access would throw).
        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Response.Disposed.Should().BeTrue("the buffered response must be disposed before return");
    }

    [Fact]
    public async Task SendWithRetry_DisposesResponse_OnNonRetryableError()
    {
        var handler = new SingleResponseHandler(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}");
        var provider = new TestProvider(new HttpClient(handler));

        var (response, _) = await provider.CallSendWithRetry();

        response!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.Response.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SendWithRetry_HonorsCancellation()
    {
        var handler = new SingleResponseHandler(HttpStatusCode.OK, "{}");
        var provider = new TestProvider(new HttpClient(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await provider.CallSendWithRetry(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
