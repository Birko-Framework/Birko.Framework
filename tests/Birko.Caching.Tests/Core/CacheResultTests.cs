using Birko.Caching;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Tests.Core;

public class CacheResultTests
{
    [Fact]
    public void Hit_HasValue_ReturnsTrue()
    {
        var result = CacheResult<string>.Hit("hello");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void Miss_HasValue_ReturnsFalse()
    {
        var result = CacheResult<string>.Miss();

        result.HasValue.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Hit_WithNullValue_StillHasValue()
    {
        var result = CacheResult<string?>.Hit(null);

        result.HasValue.Should().BeTrue();
    }

    [Fact]
    public void Hit_WithIntValue_ReturnsCorrectValue()
    {
        var result = CacheResult<int>.Hit(42);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Miss_WithValueType_ReturnsDefault()
    {
        var result = CacheResult<int>.Miss();

        result.HasValue.Should().BeFalse();
        result.Value.Should().Be(0);
    }
}
