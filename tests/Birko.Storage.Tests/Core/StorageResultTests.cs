using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Core;

public class StorageResultTests
{
    [Fact]
    public void Success_SetsFoundAndValue()
    {
        var result = StorageResult<string>.Success("hello");

        result.Found.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void NotFound_SetsFoundFalse()
    {
        var result = StorageResult<string>.NotFound();

        result.Found.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Success_WithNullValue_StillReportsFound()
    {
        var result = StorageResult<string?>.Success(null);

        result.Found.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Success_WithValueType_ReturnsCorrectValue()
    {
        var result = StorageResult<int>.Success(42);

        result.Found.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void NotFound_ValueType_ReturnsDefault()
    {
        var result = StorageResult<int>.NotFound();

        result.Found.Should().BeFalse();
        result.Value.Should().Be(0);
    }
}
