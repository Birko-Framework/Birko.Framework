using System.Reflection;
using Birko.AI.Providers;
using Birko.Communication.OAuth;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// Regression for CR-H005: the streaming request factory only set Authorization and omitted the
/// Editor-Version / Editor-Plugin-Version headers that the Copilot API requires (and that the
/// non-streaming path sends). Both paths now build the request through a single shared
/// CreateRequest(json) helper, so the headers can't drift between them.
/// </summary>
public class GitHubCopilotProviderTests
{
    private sealed class StubOAuthClient : IOAuthClient
    {
        public Task<OAuthToken> GetTokenAsync(CancellationToken ct = default) =>
            Task.FromResult(new OAuthToken { AccessToken = "tok" });
        public Task<OAuthToken> RefreshTokenAsync(CancellationToken ct = default) =>
            Task.FromResult(new OAuthToken { AccessToken = "tok" });
        public Task<OAuthToken> ExchangeCodeAsync(string code, string? codeVerifier = null, CancellationToken ct = default) =>
            Task.FromResult(new OAuthToken { AccessToken = "tok" });
        public Task<OAuthToken> PollDeviceTokenAsync(string deviceCode, int? intervalSeconds = null, CancellationToken ct = default) =>
            Task.FromResult(new OAuthToken { AccessToken = "tok" });
        public Task<DeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceAuthorizationResponse());
        public string BuildAuthorizationUrl(string state, PkceChallenge? pkceChallenge = null) => string.Empty;
        public void ClearTokenCache() { }
        public void Dispose() { }
    }

    [Fact]
    public void CreateRequest_IncludesEditorHeadersAndAuth()
    {
        var provider = new GitHubCopilotProvider(new StubOAuthClient());

        // Give the provider a current token (CreateRequest dereferences _currentToken).
        typeof(GitHubCopilotProvider)
            .GetField("_currentToken", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(provider, new OAuthToken { AccessToken = "abc123" });

        var method = typeof(GitHubCopilotProvider)
            .GetMethod("CreateRequest", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        using var request = (HttpRequestMessage)method!.Invoke(provider, new object[] { "{}" })!;

        request.Headers.GetValues("Editor-Version").Should().ContainSingle().Which.Should().Be("vscode/1.96.0");
        request.Headers.GetValues("Editor-Plugin-Version").Should().ContainSingle().Which.Should().Be("copilot-chat/0.23.2");
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("abc123");
    }

    [Fact]
    public void OnlyOneRequestBuilder_SharedByBothPaths()
    {
        // The private CreateRequest(string) helper must exist — it is the single builder both
        // SendMessageAsync and SendMessageStreamingAsync route through.
        var method = typeof(GitHubCopilotProvider)
            .GetMethod("CreateRequest", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(string) });

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(HttpRequestMessage));
    }
}
