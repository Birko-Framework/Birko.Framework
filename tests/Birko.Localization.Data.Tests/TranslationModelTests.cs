using Xunit;
using FluentAssertions;

namespace Birko.Localization.Data.Tests;

public class TranslationModelTests
{
    [Fact]
    public void CopyTo_CopiesAllProperties()
    {
        var original = new TranslationModel
        {
            Guid = Guid.NewGuid(),
            Key = "greeting",
            Culture = "sk",
            Value = "Ahoj",
            Namespace = "app",
            UpdatedAt = new DateTime(2026, 3, 18)
        };

        var clone = new TranslationModel();
        original.CopyTo(clone);

        clone.Guid.Should().Be(original.Guid);
        clone.Key.Should().Be("greeting");
        clone.Culture.Should().Be("sk");
        clone.Value.Should().Be("Ahoj");
        clone.Namespace.Should().Be("app");
        clone.UpdatedAt.Should().Be(new DateTime(2026, 3, 18));
    }

    [Fact]
    public void CopyTo_CreatesNewInstance_WhenNull()
    {
        var original = new TranslationModel { Key = "test", Culture = "en", Value = "Test" };
        var result = original.CopyTo(null);
        result.Should().BeOfType<TranslationModel>();
        ((TranslationModel)result).Key.Should().Be("test");
    }

    [Fact]
    public void DefaultValues()
    {
        var model = new TranslationModel();
        model.Key.Should().BeEmpty();
        model.Culture.Should().BeEmpty();
        model.Value.Should().BeEmpty();
        model.Namespace.Should().BeNull();
        model.UpdatedAt.Should().BeNull();
        model.Guid.Should().BeNull();
    }
}
