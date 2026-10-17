using System.Net;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using Birko.Communication.OAuth;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-531 — a failed call carries a classified <see cref="LlmFailure"/> (kind, status, transient, Retry-After), set once
/// in <see cref="LlmProviderBase"/>, on every HTTP provider, sync and streaming. Before, every path built
/// <c>LlmResponse.Error(string)</c>: the status was thrown away, so DraCode classified by substrings of the message, and a
/// caller's cancellation came back as an error response ("A task was canceled", measured on Ollama and Z.AI).
/// </summary>
public class ClassifiedFailureTests
{
    // ── harness ──

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return send(request, ct);
        }
    }

    private static Handler Status(int status, string body = "{\"error\":{\"message\":\"nope\"}}", string? retryAfter = null) => new((_, _) =>
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return Task.FromResult(response);
    });

    private static Handler Throws(Exception ex) => new((_, _) => Task.FromException<HttpResponseMessage>(ex));

    private static Handler Hangs() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    });

    private static readonly Birko.RetryPolicy NoRetry = new() { MaxRetries = 0 };

    private sealed class TestClaude : ClaudeProvider
    { public TestClaude(HttpMessageHandler h) : base("key", "model", handler: h) => RetryPolicy = NoRetry; }
    private sealed class TestOpenAi : OpenAiProvider
    { public TestOpenAi(HttpMessageHandler h) : base("key", handler: h) => RetryPolicy = NoRetry; }
    private sealed class TestAzure : AzureOpenAiProvider
    { public TestAzure(HttpMessageHandler h) : base("https://example.invalid", "key", handler: h) => RetryPolicy = NoRetry; }
    private sealed class TestGemini : GeminiProvider
    { public TestGemini(HttpMessageHandler h) : base("key", handler: h) => RetryPolicy = NoRetry; }
    private sealed class TestOllama : OllamaProvider
    { public TestOllama(HttpMessageHandler h) : base("model", "http://localhost:11434", h) => RetryPolicy = NoRetry; }
    private sealed class TestZAi : ZAiProvider
    { public TestZAi(HttpMessageHandler h) : base("key", handler: h) => RetryPolicy = NoRetry; }
    private sealed class TestCompatible : OpenAiCompatibleProviderBase
    {
        public TestCompatible(HttpMessageHandler h) : base("model", "http://localhost:8000", "key", h) => RetryPolicy = NoRetry;
        protected override string ProviderName => "Compatible";
    }
    private sealed class TestCopilot : GitHubCopilotProvider
    {
        public TestCopilot(HttpMessageHandler h, IOAuthClient oauth) : base(oauth, handler: h) => RetryPolicy = NoRetry;
    }

    private sealed class FakeOAuth : IOAuthClient
    {
        public int Refreshes;
        private static OAuthToken Token() => new() { AccessToken = "t", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        public Task<OAuthToken> GetTokenAsync(CancellationToken ct = default) => Task.FromResult(Token());
        public Task<OAuthToken> RefreshTokenAsync(CancellationToken ct = default) { Refreshes++; return Task.FromResult(Token()); }
        public Task<OAuthToken> ExchangeCodeAsync(string code, string? codeVerifier = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthToken> PollDeviceTokenAsync(string deviceCode, int? intervalSeconds = null, CancellationToken ct = default) => throw new NotSupportedException();
        public string BuildAuthorizationUrl(string state, PkceChallenge? pkceChallenge = null) => throw new NotSupportedException();
        public void ClearTokenCache() { }
        public void Dispose() { }
    }

    private static readonly Dictionary<string, Func<HttpMessageHandler, LlmProviderBase>> Providers = new()
    {
        ["Claude"] = h => new TestClaude(h),
        ["OpenAI"] = h => new TestOpenAi(h),
        ["AzureOpenAI"] = h => new TestAzure(h),
        ["Gemini"] = h => new TestGemini(h),
        ["Ollama"] = h => new TestOllama(h),
        ["ZAi"] = h => new TestZAi(h),
        ["OpenAiCompatible"] = h => new TestCompatible(h),
        ["GitHubCopilot"] = h => new TestCopilot(h, new FakeOAuth()),
    };

    public static TheoryData<string> ProviderNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Providers.Keys) data.Add(name);
        return data;
    }

    private static readonly List<Message> Conversation = [new Message { Role = "user", Content = "hi" }];

    private static Task<LlmResponse> Send(string provider, HttpMessageHandler handler, CancellationToken ct = default)
        => Providers[provider](handler).SendMessageAsync(Conversation, [], "system", ct);

    private static async Task<LlmResponse> SendStreaming(string provider, HttpMessageHandler handler, CancellationToken ct = default)
    {
        await using var streaming = await Providers[provider](handler).SendMessageStreamingAsync(Conversation, [], "system", ct);
        streaming.Error.Should().NotBeNullOrEmpty();
        streaming.FinalResponse.Should().NotBeNull("a failed stream carries the classified failure in FinalResponse");
        return streaming.FinalResponse!;
    }

    // ── the status matrix, every provider, sync and streaming ──

    public static TheoryData<string, int, string, LlmFailureKind, bool> StatusCases()
    {
        var data = new TheoryData<string, int, string, LlmFailureKind, bool>();
        foreach (var p in Providers.Keys)
        {
            data.Add(p, 503, "{}", LlmFailureKind.ServerError, true);
            data.Add(p, 500, "{}", LlmFailureKind.ServerError, true);
            data.Add(p, 400, "{\"error\":{\"message\":\"bad\"}}", LlmFailureKind.BadRequest, false);
            data.Add(p, 401, "{}", LlmFailureKind.Unauthorized, false);
            data.Add(p, 402, "{}", LlmFailureKind.Billing, false);
            data.Add(p, 404, "{}", LlmFailureKind.NotFound, false);
            data.Add(p, 429, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"You exceeded your current quota\"}}", LlmFailureKind.Billing, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task A_failed_status_is_classified(string provider, int status, string body, LlmFailureKind kind, bool transient)
    {
        var response = await Send(provider, Status(status, body));

        response.StopReason.Should().Be("error");
        response.Failure.Should().NotBeNull();
        response.Failure!.Kind.Should().Be(kind);
        response.StatusCode.Should().Be(status);
        response.IsTransient.Should().Be(transient);
        response.Failure.Category.Should().Be(transient ? ErrorCategory.Transient : ErrorCategory.Permanent);
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task A_failed_streaming_status_is_classified(string provider, int status, string body, LlmFailureKind kind, bool transient)
    {
        var response = await SendStreaming(provider, Status(status, body));

        response.Failure!.Kind.Should().Be(kind);
        response.StatusCode.Should().Be(status);
        response.IsTransient.Should().Be(transient);
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task A_rate_limit_carries_the_servers_retry_after(string provider)
    {
        var response = await Send(provider, Status(429, "{}", retryAfter: "7"));

        response.Failure!.Kind.Should().Be(LlmFailureKind.RateLimited);
        response.StatusCode.Should().Be(429);
        response.IsTransient.Should().BeTrue();
        response.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task A_streaming_rate_limit_carries_the_servers_retry_after(string provider)
    {
        var response = await SendStreaming(provider, Status(429, "{}", retryAfter: "7"));
        response.Failure!.Kind.Should().Be(LlmFailureKind.RateLimited);
        response.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    // ── exceptions ──

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task A_network_failure_is_transient(string provider)
    {
        var response = await Send(provider, Throws(new HttpRequestException("connection refused")));

        response.Failure!.Kind.Should().Be(LlmFailureKind.Network);
        response.StatusCode.Should().BeNull();
        response.IsTransient.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task A_client_timeout_is_transient(string provider)
    {
        // What HttpClient throws when its own Timeout elapses: a TaskCanceledException the caller's token did not cause.
        var response = await Send(provider, Throws(new TaskCanceledException("timed out", new TimeoutException())));

        response.Failure!.Kind.Should().Be(LlmFailureKind.Timeout);
        response.IsTransient.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task A_streaming_network_failure_is_transient(string provider)
    {
        var response = await SendStreaming(provider, Throws(new HttpRequestException("connection refused")));
        response.Failure!.Kind.Should().Be(LlmFailureKind.Network);
        response.IsTransient.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task The_callers_cancellation_propagates_instead_of_failing(string provider)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var act = () => Send(provider, Hangs(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task The_callers_cancellation_of_a_stream_propagates(string provider)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var act = async () =>
        {
            await using var _ = await Providers[provider](Hangs()).SendMessageStreamingAsync(Conversation, [], "system", cts.Token);
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── other paths ──

    [Fact]
    public async Task A_provider_without_a_key_is_classified_not_configured()
    {
        var response = await new ClaudeProvider("", "model").SendMessageAsync(Conversation, [], "system");
        response.Failure!.Kind.Should().Be(LlmFailureKind.NotConfigured);
        response.IsTransient.Should().BeFalse();
    }

    [Theory]
    [InlineData("overloaded_error", LlmFailureKind.ServerError, true)]
    [InlineData("rate_limit_error", LlmFailureKind.RateLimited, true)]
    [InlineData("authentication_error", LlmFailureKind.Unauthorized, false)]
    [InlineData("invalid_request_error", LlmFailureKind.BadRequest, false)]
    [InlineData("something_new", LlmFailureKind.Unknown, false)]
    public async Task A_claude_error_in_a_200_body_is_classified_by_its_type(string type, LlmFailureKind kind, bool transient)
    {
        var body = $"{{\"type\":\"error\",\"error\":{{\"type\":\"{type}\",\"message\":\"m\"}}}}";
        var response = await Send("Claude", Status(200, body));

        response.Failure!.Kind.Should().Be(kind);
        response.IsTransient.Should().Be(transient);
    }

    [Fact]
    public async Task An_unreadable_success_body_is_an_invalid_response()
    {
        var response = await Send("OpenAiCompatible", Status(200, "{\"unexpected\":true}"));
        response.Failure!.Kind.Should().Be(LlmFailureKind.InvalidResponse);
        response.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task A_streaming_401_from_copilot_refreshes_the_token_and_retries_once()
    {
        // Before TASK-531 the retry tested `response is { StatusCode: Unauthorized }`, but the streaming connect
        // returns no response for a non-success status, so the refresh never ran and the stream failed.
        var calls = 0;
        var handler = new Handler((_, _) => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: [DONE]\n\n") }));
        var oauth = new FakeOAuth();

        await using var streaming = await new TestCopilot(handler, oauth).SendMessageStreamingAsync(Conversation, [], "system");

        streaming.Error.Should().BeNull();
        oauth.Refreshes.Should().Be(1);
        handler.Calls.Should().Be(2);
    }

    // ── the contract types ──

    [Theory]
    [InlineData(408, LlmFailureKind.Timeout)]
    [InlineData(409, LlmFailureKind.BadRequest)]
    [InlineData(413, LlmFailureKind.BadRequest)]
    [InlineData(502, LlmFailureKind.ServerError)]
    [InlineData(529, LlmFailureKind.ServerError)]
    [InlineData(302, LlmFailureKind.Unknown)]
    public void Status_codes_map_to_kinds(int status, LlmFailureKind kind)
        => LlmFailure.FromStatusCode(status).Kind.Should().Be(kind);

    [Fact]
    public void An_unclassified_error_is_unknown_and_permanent()
    {
        var response = LlmResponse.Error("something");
        response.Failure.Should().Be(LlmFailure.Unknown);
        response.IsTransient.Should().BeFalse();
        response.Failure!.Category.Should().Be(ErrorCategory.Permanent);
    }

    [Fact]
    public void A_success_has_no_failure()
    {
        var response = new LlmResponse { StopReason = "end_turn", Content = [] };
        response.Failure.Should().BeNull();
        response.IsTransient.Should().BeFalse();
        response.StatusCode.Should().BeNull();
    }

    [Fact]
    public void The_enum_values_are_pinned()
    {
        ((int)ErrorCategory.Transient).Should().Be(0);
        ((int)ErrorCategory.Permanent).Should().Be(1);
        ((int)LlmFailureKind.Unknown).Should().Be(0);
        ((int)LlmFailureKind.InvalidResponse).Should().Be(11);
    }
}
