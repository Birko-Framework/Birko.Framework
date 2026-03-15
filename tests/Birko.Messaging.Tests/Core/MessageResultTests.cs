using System;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Core;

public class MessageResultTests
{
    [Fact]
    public void Succeeded_SetsSuccessTrue()
    {
        var result = MessageResult.Succeeded();

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Succeeded_WithMessageId_SetsId()
    {
        var result = MessageResult.Succeeded("msg-123");

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("msg-123");
    }

    [Fact]
    public void Succeeded_WithoutMessageId_IdIsNull()
    {
        var result = MessageResult.Succeeded();

        result.MessageId.Should().BeNull();
    }

    [Fact]
    public void Failed_SetsSuccessFalse()
    {
        var result = MessageResult.Failed("something went wrong");

        result.Success.Should().BeFalse();
        result.MessageId.Should().BeNull();
    }

    [Fact]
    public void Failed_SetsErrorMessage()
    {
        var result = MessageResult.Failed("connection refused");

        result.Error.Should().Be("connection refused");
    }

    [Fact]
    public void Failed_WithException_SetsException()
    {
        var ex = new InvalidOperationException("test");
        var result = MessageResult.Failed("error", ex);

        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Timestamp_IsSetToRecentUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var result = MessageResult.Succeeded();
        var after = DateTimeOffset.UtcNow;

        result.Timestamp.Should().BeOnOrAfter(before);
        result.Timestamp.Should().BeOnOrBefore(after);
    }
}
