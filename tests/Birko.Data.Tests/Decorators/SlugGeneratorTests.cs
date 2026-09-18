using Birko.Data.Patterns.Decorators;
using FluentAssertions;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class SlugGeneratorTests
{
    #region Normalize

    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        SlugGenerator.Normalize(null).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        SlugGenerator.Normalize("").Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Whitespace_ReturnsEmpty()
    {
        SlugGenerator.Normalize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Normalize_SimpleText_ReturnsLowercaseHyphened()
    {
        SlugGenerator.Normalize("Hello World").Should().Be("hello-world");
    }

    [Fact]
    public void Normalize_WithDiacritics_RemovesDiacritics()
    {
        SlugGenerator.Normalize("Caf\u00e9 \u010cerven\u00fd").Should().Be("cafe-cerveny");
    }

    [Fact]
    public void Normalize_WithSpecialChars_RemovesInvalid()
    {
        SlugGenerator.Normalize("Hello! @World# $2026").Should().Be("hello-world-2026");
    }

    [Fact]
    public void Normalize_MultipleHyphens_CollapsesToSingle()
    {
        SlugGenerator.Normalize("hello---world").Should().Be("hello-world");
    }

    [Fact]
    public void Normalize_LeadingTrailingHyphens_Trimmed()
    {
        SlugGenerator.Normalize("-hello-world-").Should().Be("hello-world");
    }

    [Fact]
    public void Normalize_Underscores_ConvertedToHyphens()
    {
        SlugGenerator.Normalize("hello_world_test").Should().Be("hello-world-test");
    }

    #endregion

    #region EnsureUnique (sync)

    [Fact]
    public void EnsureUnique_NotTaken_ReturnsBaseSlug()
    {
        var result = SlugGenerator.EnsureUnique("test", _ => false);
        result.Should().Be("test");
    }

    [Fact]
    public void EnsureUnique_Taken_AppendsSuffix()
    {
        var taken = new HashSet<string> { "test" };
        var result = SlugGenerator.EnsureUnique("test", s => taken.Contains(s));
        result.Should().Be("test-2");
    }

    [Fact]
    public void EnsureUnique_MultipleTaken_IncrementsSuffix()
    {
        var taken = new HashSet<string> { "test", "test-2", "test-3" };
        var result = SlugGenerator.EnsureUnique("test", s => taken.Contains(s));
        result.Should().Be("test-4");
    }

    [Fact]
    public void EnsureUnique_EmptySlug_UsesFallback()
    {
        var result = SlugGenerator.EnsureUnique("", _ => false);
        result.Should().Be("item");
    }

    [Fact]
    public void EnsureUnique_EmptySlug_CustomFallback()
    {
        var result = SlugGenerator.EnsureUnique("", _ => false, "product");
        result.Should().Be("product");
    }

    #endregion

    #region EnsureUniqueAsync

    [Fact]
    public async Task EnsureUniqueAsync_NotTaken_ReturnsBaseSlug()
    {
        var result = await SlugGenerator.EnsureUniqueAsync("test", _ => Task.FromResult(false));
        result.Should().Be("test");
    }

    [Fact]
    public async Task EnsureUniqueAsync_Taken_AppendsSuffix()
    {
        var taken = new HashSet<string> { "test" };
        var result = await SlugGenerator.EnsureUniqueAsync("test", s => Task.FromResult(taken.Contains(s)));
        result.Should().Be("test-2");
    }

    #endregion
}
