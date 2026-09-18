using System;
using System.Collections.Generic;
using Birko.Communication.WebSocket.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// CR-M074 regression: WebSocketAuthenticationService declares a Dispose() but historically did
/// NOT implement IDisposable, so DI (and `using`) never disposed it and the wrapped
/// AuthenticationService's ReaderWriterLockSlim leaked. It must be assignable to IDisposable.
///
/// CR-M075 coverage: the auth path (missing / invalid / valid token, enabled vs disabled, and
/// token extraction from the query string) had no tests — only WebSocketSettings did.
/// </summary>
public class WebSocketAuthenticationServiceTests
{
    private static WebSocketAuthenticationService NewService(
        bool enabled = false,
        params string[] tokens)
    {
        var config = new WebSocketAuthenticationConfiguration
        {
            Enabled = enabled,
            Tokens = new List<string>(tokens)
        };

        return new WebSocketAuthenticationService(
            Options.Create(config),
            NullLogger<WebSocketAuthenticationService>.Instance,
            NullLogger<Birko.Security.Authentication.AuthenticationService>.Instance);
    }

    // ---- CR-M074: IDisposable ----

    [Fact]
    public void Service_ImplementsIDisposable()
    {
        typeof(WebSocketAuthenticationService).Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = NewService();

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = NewService(enabled: true, "token");

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void UsingBlock_DisposesWithoutThrowing()
    {
        var act = () =>
        {
            using var service = NewService();
            service.IsAuthenticationEnabled();
        };

        act.Should().NotThrow();
    }

    // ---- CR-M075: authentication enabled/disabled ----

    [Fact]
    public void IsAuthenticationEnabled_False_WhenDisabled()
    {
        using var service = NewService(enabled: false, "token");

        service.IsAuthenticationEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticationEnabled_False_WhenEnabledButNoTokens()
    {
        using var service = NewService(enabled: true);

        service.IsAuthenticationEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticationEnabled_True_WhenEnabledWithTokens()
    {
        using var service = NewService(enabled: true, "token");

        service.IsAuthenticationEnabled().Should().BeTrue();
    }

    // ---- CR-M075: token validation (reject / allow) ----

    [Fact]
    public void ValidateToken_AllowsAny_WhenAuthenticationDisabled()
    {
        using var service = NewService(enabled: false, "secret");

        // Disabled => allow-all, regardless of token presence.
        service.ValidateToken(null, "127.0.0.1").Should().BeTrue();
        service.ValidateToken("anything", "127.0.0.1").Should().BeTrue();
    }

    [Fact]
    public void ValidateToken_Rejects_WhenTokenMissing()
    {
        using var service = NewService(enabled: true, "secret");

        service.ValidateToken(null, "127.0.0.1").Should().BeFalse();
        service.ValidateToken("", "127.0.0.1").Should().BeFalse();
        service.ValidateToken("   ", "127.0.0.1").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_Rejects_WhenTokenInvalid()
    {
        using var service = NewService(enabled: true, "secret");

        service.ValidateToken("wrong-token", "127.0.0.1").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_Allows_WhenTokenValid()
    {
        using var service = NewService(enabled: true, "secret");

        service.ValidateToken("secret", "127.0.0.1").Should().BeTrue();
    }

    // ---- CR-M075: token extraction from query string (the middleware entry point) ----

    [Fact]
    public void ExtractTokenFromQuery_ReturnsToken_WhenPresent()
    {
        using var service = NewService(enabled: true, "secret");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?token=secret");

        service.ExtractTokenFromQuery(context).Should().Be("secret");
    }

    [Fact]
    public void ExtractTokenFromQuery_ReturnsNull_WhenAbsent()
    {
        using var service = NewService(enabled: true, "secret");
        var context = new DefaultHttpContext();

        service.ExtractTokenFromQuery(context).Should().BeNull();
    }

    [Fact]
    public void ExtractedToken_DrivesRejection_ForAbsentToken()
    {
        // End-to-end of the middleware auth decision: no ?token= in the query => reject.
        using var service = NewService(enabled: true, "secret");
        var context = new DefaultHttpContext();

        var token = service.ExtractTokenFromQuery(context);

        service.ValidateToken(token, "127.0.0.1").Should().BeFalse();
    }

    [Fact]
    public void ExtractedToken_DrivesAcceptance_ForValidToken()
    {
        using var service = NewService(enabled: true, "secret");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?token=secret");

        var token = service.ExtractTokenFromQuery(context);

        service.ValidateToken(token, "127.0.0.1").Should().BeTrue();
    }
}
