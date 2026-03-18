using Xunit;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class StringInterpolatorTests
{
    [Fact]
    public void Interpolate_Named_ReplacesPlaceholders()
    {
        var result = StringInterpolator.Interpolate(
            "Hello {userName}, you have {count} messages",
            new Dictionary<string, object?> { ["userName"] = "John", ["count"] = 5 });

        result.Should().Be("Hello John, you have 5 messages");
    }

    [Fact]
    public void Interpolate_Named_LeavesUnmatchedPlaceholders()
    {
        var result = StringInterpolator.Interpolate(
            "Hello {userName}, {missing}",
            new Dictionary<string, object?> { ["userName"] = "John" });

        result.Should().Be("Hello John, {missing}");
    }

    [Fact]
    public void Interpolate_Named_NullValue_ReplacesWithEmpty()
    {
        var result = StringInterpolator.Interpolate(
            "Value: {key}",
            new Dictionary<string, object?> { ["key"] = null });

        result.Should().Be("Value: ");
    }

    [Fact]
    public void Interpolate_Named_EmptyTemplate_ReturnsEmpty()
    {
        var result = StringInterpolator.Interpolate("", new Dictionary<string, object?> { ["key"] = "val" });
        result.Should().Be("");
    }

    [Fact]
    public void Interpolate_Named_EmptyArgs_ReturnsTemplate()
    {
        var result = StringInterpolator.Interpolate("Hello {name}", new Dictionary<string, object?>());
        result.Should().Be("Hello {name}");
    }

    [Fact]
    public void Interpolate_Positional_ReplacesPlaceholders()
    {
        var result = StringInterpolator.Interpolate("Hello {0}, you have {1} messages", new object[] { "John", 5 });
        result.Should().Be("Hello John, you have 5 messages");
    }

    [Fact]
    public void Interpolate_Positional_EmptyArgs_ReturnsTemplate()
    {
        var result = StringInterpolator.Interpolate("Hello", Array.Empty<object>());
        result.Should().Be("Hello");
    }
}
