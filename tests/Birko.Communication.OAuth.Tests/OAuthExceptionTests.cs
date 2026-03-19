using System;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class OAuthExceptionTests
{
    [Fact]
    public void Constructor_Message_SetsMessage()
    {
        var ex = new OAuthException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new OAuthException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_WithErrorDetails_SetsProperties()
    {
        var ex = new OAuthException("fail", "invalid_grant", "Token expired", 400);

        ex.ErrorCode.Should().Be("invalid_grant");
        ex.ErrorDescription.Should().Be("Token expired");
        ex.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Constructor_WithErrorDetailsAndInner_SetsAll()
    {
        var inner = new Exception("cause");
        var ex = new OAuthException("fail", "invalid_client", "Bad client", 401, inner);

        ex.ErrorCode.Should().Be("invalid_client");
        ex.ErrorDescription.Should().Be("Bad client");
        ex.StatusCode.Should().Be(401);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
