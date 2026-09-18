using Birko.Data.Localization.Expressions;
using Birko.Data.Localization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

public class LocalizedExpressionAnalyzerTests
{
    private static readonly string[] LocalizableFields = new[] { "Name", "Description" };

    [Fact]
    public void Split_NullFilter_ReturnsNoLocalizedConditions()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(null, LocalizableFields);

        result.HasLocalizedConditions.Should().BeFalse();
        result.RemainingFilter.Should().BeNull();
    }

    [Fact]
    public void Split_NonLocalizedField_PassesThrough()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Code == "W001", LocalizableFields);

        result.HasLocalizedConditions.Should().BeFalse();
        result.RemainingFilter.Should().NotBeNull();

        // Verify the remaining filter still works
        var model = new TestLocalizableModel { Code = "W001" };
        result.RemainingFilter!.Compile()(model).Should().BeTrue();
    }

    [Fact]
    public void Split_LocalizedFieldEquals_ExtractsCondition()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name == "Komponent", LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions.Should().HaveCount(1);
        result.LocalizedConditions[0].FieldName.Should().Be("Name");
        result.LocalizedConditions[0].ValuePredicate("Komponent").Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Other").Should().BeFalse();
        result.RemainingFilter.Should().BeNull();
    }

    [Fact]
    public void Split_LocalizedFieldNotEquals_ExtractsCondition()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name != "Widget", LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Widget").Should().BeFalse();
        result.LocalizedConditions[0].ValuePredicate("Komponent").Should().BeTrue();
    }

    [Fact]
    public void Split_LocalizedFieldContains_ExtractsCondition()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name.Contains("omp"), LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions[0].FieldName.Should().Be("Name");
        result.LocalizedConditions[0].ValuePredicate("Komponent").Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Widget").Should().BeFalse();
    }

    [Fact]
    public void Split_LocalizedFieldStartsWith_ExtractsCondition()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name.StartsWith("Komp"), LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Komponent").Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Widget").Should().BeFalse();
    }

    [Fact]
    public void Split_LocalizedFieldEndsWith_ExtractsCondition()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Description.EndsWith("pis"), LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions[0].FieldName.Should().Be("Description");
        result.LocalizedConditions[0].ValuePredicate("Popis").Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Widget").Should().BeFalse();
    }

    [Fact]
    public void Split_MixedConditions_SplitsCorrectly()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name == "Komponent" && x.Code == "W001", LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions.Should().HaveCount(1);
        result.LocalizedConditions[0].FieldName.Should().Be("Name");

        result.RemainingFilter.Should().NotBeNull();
        var model = new TestLocalizableModel { Code = "W001" };
        result.RemainingFilter!.Compile()(model).Should().BeTrue();
        model.Code = "G001";
        result.RemainingFilter.Compile()(model).Should().BeFalse();
    }

    [Fact]
    public void Split_MultipleLocalizedFields_ExtractsBoth()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name == "Komponent" && x.Description.Contains("popis"), LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions.Should().HaveCount(2);
        result.LocalizedConditions.Should().Contain(c => c.FieldName == "Name");
        result.LocalizedConditions.Should().Contain(c => c.FieldName == "Description");
        result.RemainingFilter.Should().BeNull();
    }

    [Fact]
    public void Split_CapturedVariable_ResolvesValue()
    {
        var searchTerm = "Komponent";
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name == searchTerm, LocalizableFields);

        result.HasLocalizedConditions.Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Komponent").Should().BeTrue();
        result.LocalizedConditions[0].ValuePredicate("Other").Should().BeFalse();
    }

    [Fact]
    public void Split_EmptyLocalizableFields_PassesThrough()
    {
        var result = LocalizedExpressionAnalyzer.Split<TestLocalizableModel>(
            x => x.Name == "Komponent", Array.Empty<string>());

        result.HasLocalizedConditions.Should().BeFalse();
        result.RemainingFilter.Should().NotBeNull();
    }
}
