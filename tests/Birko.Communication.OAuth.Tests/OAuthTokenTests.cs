using System;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class OAuthTokenTests
{
    [Fact]
    public void IsExpired_ReturnsFalse_WhenTokenIsFresh()
    {
        var token = new OAuthToken
        {
            AccessToken = "abc",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenTokenIsExpired()
    {
        var token = new OAuthToken
        {
            AccessToken = "abc",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };

        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WithBuffer_ReturnsTrueWhenWithinBuffer()
    {
        var token = new OAuthToken
        {
            AccessToken = "abc",
            ExpiresAt = DateTime.UtcNow.AddSeconds(30) // Expires in 30s
        };

        token.IsExpiredWithBuffer(60).Should().BeTrue(); // Within 60s buffer
        token.IsExpiredWithBuffer(10).Should().BeFalse(); // Outside 10s buffer
    }

    [Fact]
    public void DefaultTokenType_IsBearer()
    {
        var token = new OAuthToken();
        token.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public void DeviceAuthorizationResponse_DefaultInterval_Is5()
    {
        var response = new DeviceAuthorizationResponse();
        response.IntervalSeconds.Should().Be(5);
    }
}
