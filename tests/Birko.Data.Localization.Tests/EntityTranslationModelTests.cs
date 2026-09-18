using Birko.Data.Localization.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class EntityTranslationModelTests
{
    [Fact]
    public void CopyTo_CopiesAllProperties()
    {
        var original = new EntityTranslationModel
        {
            Guid = System.Guid.NewGuid(),
            EntityGuid = System.Guid.NewGuid(),
            EntityType = "Product",
            FieldName = "Name",
            Culture = "sk",
            Value = "Produkt",
            UpdatedAt = new System.DateTime(2026, 1, 1)
        };

        var clone = (EntityTranslationModel)original.CopyTo();

        clone.Guid.Should().Be(original.Guid);
        clone.EntityGuid.Should().Be(original.EntityGuid);
        clone.EntityType.Should().Be("Product");
        clone.FieldName.Should().Be("Name");
        clone.Culture.Should().Be("sk");
        clone.Value.Should().Be("Produkt");
        clone.UpdatedAt.Should().Be(original.UpdatedAt);
    }

    [Fact]
    public void CopyTo_WithExistingClone_OverwritesProperties()
    {
        var original = new EntityTranslationModel
        {
            EntityGuid = System.Guid.NewGuid(),
            EntityType = "Category",
            FieldName = "Description",
            Culture = "de",
            Value = "Beschreibung"
        };

        var target = new EntityTranslationModel();
        original.CopyTo(target);

        target.EntityGuid.Should().Be(original.EntityGuid);
        target.EntityType.Should().Be("Category");
        target.Culture.Should().Be("de");
        target.Value.Should().Be("Beschreibung");
    }

    [Fact]
    public void DefaultValues_AreEmptyStrings()
    {
        var model = new EntityTranslationModel();

        model.EntityType.Should().BeEmpty();
        model.FieldName.Should().BeEmpty();
        model.Culture.Should().BeEmpty();
        model.Value.Should().BeEmpty();
        model.UpdatedAt.Should().BeNull();
    }
}
