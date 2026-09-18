using System.Collections.Generic;
using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// SH-H040, the SSE half — <c>AuthenticateConnection</c> granted a connection before it ever looked for
/// a token, whenever authentication was switched <b>on</b> but misconfigured to nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>A second gate, the same root cause, a different file.</b> The filed finding named
/// <c>AuthenticationService.ValidateToken</c>. This method never reaches that call: it opened with
/// </para>
/// <code>
/// if (!_authService.IsAuthenticationEnabled())
/// {
///     return SseAuthenticationResult.Success(Guid.NewGuid().ToString("N"));
/// }
/// </code>
/// <para>
/// and <c>IsAuthenticationEnabled()</c> is also false when authentication is enabled with an empty token
/// set — so fixing only <c>ValidateToken</c> would have left every SSE connection authenticated. Both
/// gates now read one producer (<c>IsAuthenticationDisabled</c>), which is what stops a third caller
/// reintroducing the split.
/// </para>
/// <para>
/// The engine-level mechanism, the measured env-var contrast, and why the constructor does not throw are
/// documented in <c>Birko.Security.Tests/AuthenticationMisconfigurationFailsClosedTests</c>.
/// </para>
/// </remarks>
public class SseAuthenticationMisconfigurationFailsClosedTests
{
    private static SseAuthenticationConfiguration Config(bool enabled, params string[] tokens)
    {
        var config = new SseAuthenticationConfiguration { Enabled = enabled };
        config.Tokens.AddRange(tokens);
        return config;
    }

    [Fact]
    public void Enabled_WithNothingConfigured_RefusesTheConnection()
    {
        using var service = new SseAuthenticationService(Config(enabled: true));

        // Before the fix this returned Success with a fresh connection id, without extracting a token.
        var result = service.AuthenticateConnection(
            headers: new Dictionary<string, string>(),
            queryString: null,
            remoteEndPoint: "127.0.0.1");

        result.IsAuthenticated.Should().BeFalse(
            "authentication is switched ON, so a misconfiguration must refuse the connection");
    }

    [Fact]
    public void Enabled_WithNothingConfigured_RefusesEvenWhenATokenIsPresented()
    {
        using var service = new SseAuthenticationService(Config(enabled: true));

        var result = service.AuthenticateConnection(
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer anything" },
            queryString: "?token=anything",
            remoteEndPoint: "127.0.0.1");

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Misconfiguration_IsIndistinguishableFromAnInvalidToken_ToTheCaller()
    {
        // The operator detail belongs in the LogError, not in the response. An anonymous caller must not
        // be able to tell "the server is misconfigured" from "your token is wrong" — that would hand a
        // prober a positive signal for a state in which the server is known to be broken. This was a
        // finding from the security pass on the change that added the misconfiguration arm: its first
        // version returned "Authentication is misconfigured on the server".
        using var misconfigured = new SseAuthenticationService(Config(enabled: true));
        using var wrongToken = new SseAuthenticationService(Config(enabled: true, "secret"));

        var onMisconfig = misconfigured.AuthenticateConnection(
            new Dictionary<string, string>(), "?token=anything", "127.0.0.1");
        var onWrongToken = wrongToken.AuthenticateConnection(
            new Dictionary<string, string>(), "?token=wrong", "127.0.0.1");

        onMisconfig.IsAuthenticated.Should().BeFalse();
        onWrongToken.IsAuthenticated.Should().BeFalse();
        onMisconfig.ErrorMessage.Should().Be(onWrongToken.ErrorMessage,
            "the two refusals must be indistinguishable to the caller");
    }

    [Fact]
    public void Disabled_StillAllowsTheConnection()
    {
        // The opt-out. Breaking this would be worse than the defect being fixed, so it is asserted
        // rather than assumed — and it is the case that must NOT be caught by the misconfiguration arm.
        using var service = new SseAuthenticationService(Config(enabled: false));

        var result = service.AuthenticateConnection(
            headers: new Dictionary<string, string>(),
            queryString: null,
            remoteEndPoint: "127.0.0.1");

        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Enabled_WithAValidToken_StillAuthenticates()
    {
        using var service = new SseAuthenticationService(Config(enabled: true, "secret"));

        var result = service.AuthenticateConnection(
            headers: new Dictionary<string, string>(),
            queryString: "?token=secret",
            remoteEndPoint: "127.0.0.1");

        result.IsAuthenticated.Should().BeTrue("the configured happy path is untouched by this fix");
    }

    [Fact]
    public void Enabled_WithAnInvalidToken_StillRefuses()
    {
        using var service = new SseAuthenticationService(Config(enabled: true, "secret"));

        var result = service.AuthenticateConnection(
            headers: new Dictionary<string, string>(),
            queryString: "?token=wrong",
            remoteEndPoint: "127.0.0.1");

        result.IsAuthenticated.Should().BeFalse();
    }
}
