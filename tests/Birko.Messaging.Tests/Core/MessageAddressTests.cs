using System;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Core;

public class MessageAddressTests
{
    [Fact]
    public void Constructor_SetsValue()
    {
        var address = new MessageAddress("user@example.com");

        address.Value.Should().Be("user@example.com");
        address.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        var act = () => new MessageAddress(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void Constructor_WithDisplayName_SetsBoth()
    {
        var address = new MessageAddress("user@example.com", "John Doe");

        address.Value.Should().Be("user@example.com");
        address.DisplayName.Should().Be("John Doe");
    }

    [Fact]
    public void ToString_NoDisplayName_ReturnsValue()
    {
        var address = new MessageAddress("user@example.com");

        address.ToString().Should().Be("user@example.com");
    }

    [Fact]
    public void ToString_WithDisplayName_ReturnsFormatted()
    {
        var address = new MessageAddress("user@example.com", "John Doe");

        address.ToString().Should().Be("John Doe <user@example.com>");
    }

    [Fact]
    public void Equals_SameValue_CaseInsensitive_ReturnsTrue()
    {
        var a = new MessageAddress("User@Example.COM");
        var b = new MessageAddress("user@example.com");

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        var a = new MessageAddress("a@example.com");
        var b = new MessageAddress("b@example.com");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValueDifferentCase_ReturnsSameHash()
    {
        var a = new MessageAddress("User@Example.COM");
        var b = new MessageAddress("user@example.com");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
