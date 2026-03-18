using Xunit;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

public class TranslationFilterTests
{
    private static readonly List<TranslationModel> TestData = new()
    {
        new() { Key = "greeting", Culture = "en", Value = "Hello", Namespace = "app" },
        new() { Key = "farewell", Culture = "en", Value = "Goodbye", Namespace = "app" },
        new() { Key = "greeting", Culture = "sk", Value = "Ahoj", Namespace = "app" },
        new() { Key = "error", Culture = "en", Value = "Error", Namespace = "errors" },
    };

    [Fact]
    public void ByCulture_FiltersCorrectly()
    {
        var filter = TranslationFilter.ByCulture("en").ToExpression();
        var result = TestData.AsQueryable().Where(filter).ToList();
        result.Should().HaveCount(3);
        result.Should().OnlyContain(t => t.Culture == "en");
    }

    [Fact]
    public void ByKeyAndCulture_FiltersCorrectly()
    {
        var filter = TranslationFilter.ByKeyAndCulture("greeting", "sk").ToExpression();
        var result = TestData.AsQueryable().Where(filter).ToList();
        result.Should().HaveCount(1);
        result[0].Value.Should().Be("Ahoj");
    }

    [Fact]
    public void ByNamespaceAndCulture_FiltersCorrectly()
    {
        var filter = TranslationFilter.ByNamespaceAndCulture("errors", "en").ToExpression();
        var result = TestData.AsQueryable().Where(filter).ToList();
        result.Should().HaveCount(1);
        result[0].Key.Should().Be("error");
    }

    [Fact]
    public void NoFilters_ReturnsAll()
    {
        var filter = new TranslationFilter().ToExpression();
        var result = TestData.AsQueryable().Where(filter).ToList();
        result.Should().HaveCount(4);
    }
}
