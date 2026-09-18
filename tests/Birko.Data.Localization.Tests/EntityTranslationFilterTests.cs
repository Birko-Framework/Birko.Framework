using Birko.Data.Localization.Filters;
using Birko.Data.Localization.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class EntityTranslationFilterTests
{
    private static EntityTranslationModel CreateTranslation(
        System.Guid entityGuid, string entityType, string fieldName, string culture, string value)
    {
        return new EntityTranslationModel
        {
            Guid = System.Guid.NewGuid(),
            EntityGuid = entityGuid,
            EntityType = entityType,
            FieldName = fieldName,
            Culture = culture,
            Value = value
        };
    }

    [Fact]
    public void ByEntity_MatchesAllTranslationsForEntity()
    {
        var entityGuid = System.Guid.NewGuid();
        var otherGuid = System.Guid.NewGuid();
        var filter = EntityTranslationFilter.ByEntity(entityGuid).ToExpression().Compile();

        var match1 = CreateTranslation(entityGuid, "Product", "Name", "sk", "Produkt");
        var match2 = CreateTranslation(entityGuid, "Product", "Description", "de", "Beschreibung");
        var noMatch = CreateTranslation(otherGuid, "Product", "Name", "sk", "Iný");

        filter(match1).Should().BeTrue();
        filter(match2).Should().BeTrue();
        filter(noMatch).Should().BeFalse();
    }

    [Fact]
    public void ByEntityAndCulture_MatchesCorrectCombination()
    {
        var entityGuid = System.Guid.NewGuid();
        var filter = EntityTranslationFilter.ByEntityAndCulture(entityGuid, "sk").ToExpression().Compile();

        var match = CreateTranslation(entityGuid, "Product", "Name", "sk", "Produkt");
        var wrongCulture = CreateTranslation(entityGuid, "Product", "Name", "de", "Produkt");
        var wrongEntity = CreateTranslation(System.Guid.NewGuid(), "Product", "Name", "sk", "Produkt");

        filter(match).Should().BeTrue();
        filter(wrongCulture).Should().BeFalse();
        filter(wrongEntity).Should().BeFalse();
    }

    [Fact]
    public void ByEntityFieldAndCulture_MatchesExactField()
    {
        var entityGuid = System.Guid.NewGuid();
        var filter = EntityTranslationFilter.ByEntityFieldAndCulture(entityGuid, "Name", "sk").ToExpression().Compile();

        var match = CreateTranslation(entityGuid, "Product", "Name", "sk", "Produkt");
        var wrongField = CreateTranslation(entityGuid, "Product", "Description", "sk", "Popis");

        filter(match).Should().BeTrue();
        filter(wrongField).Should().BeFalse();
    }

    [Fact]
    public void ByEntityType_MatchesAllOfType()
    {
        var filter = EntityTranslationFilter.ByEntityType("Product").ToExpression().Compile();

        var match = CreateTranslation(System.Guid.NewGuid(), "Product", "Name", "sk", "Produkt");
        var noMatch = CreateTranslation(System.Guid.NewGuid(), "Category", "Name", "sk", "Kategória");

        filter(match).Should().BeTrue();
        filter(noMatch).Should().BeFalse();
    }

    [Fact]
    public void ByEntityTypeAndCulture_MatchesCombination()
    {
        var filter = EntityTranslationFilter.ByEntityTypeAndCulture("Product", "de").ToExpression().Compile();

        var match = CreateTranslation(System.Guid.NewGuid(), "Product", "Name", "de", "Produkt");
        var wrongType = CreateTranslation(System.Guid.NewGuid(), "Category", "Name", "de", "Kategorie");
        var wrongCulture = CreateTranslation(System.Guid.NewGuid(), "Product", "Name", "sk", "Produkt");

        filter(match).Should().BeTrue();
        filter(wrongType).Should().BeFalse();
        filter(wrongCulture).Should().BeFalse();
    }

    [Fact]
    public void EmptyFilter_MatchesAll()
    {
        var filter = new EntityTranslationFilter().ToExpression().Compile();
        var translation = CreateTranslation(System.Guid.NewGuid(), "Product", "Name", "sk", "Produkt");

        filter(translation).Should().BeTrue();
    }
}
