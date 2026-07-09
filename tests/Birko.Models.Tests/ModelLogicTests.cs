using System;
using System.Linq;
using Birko.Extensions;
using Birko.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Tests;

/// <summary>
/// CR-H124: coverage for the remaining Birko.Models logic — AbstractTree.BuildPath,
/// ValueData rounding on load, and the SourceValue GetValue/SetValue extensions.
/// </summary>
public class ModelLogicTests
{
    // ── AbstractTree.BuildPath ───────────────────────────

    [Fact]
    public void BuildPath_Null_ReturnsSeparatorOnly()
    {
        AbstractTree.BuildPath(null!).Should().Be("/");
    }

    [Fact]
    public void BuildPath_Empty_ReturnsSeparatorOnly()
    {
        AbstractTree.BuildPath(Array.Empty<Guid>()).Should().Be("/");
    }

    [Fact]
    public void BuildPath_SingleGuid_IsBracedAndPrefixed()
    {
        var g = Guid.NewGuid();
        AbstractTree.BuildPath(new[] { g }).Should().Be("/" + g.ToString("B"));
    }

    [Fact]
    public void BuildPath_DedupesRepeatedGuids()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        var path = AbstractTree.BuildPath(new[] { g1, g2, g1 });

        path.Should().Be($"/{g1:B}/{g2:B}");
        path!.Split('/', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }

    // ── ValueData rounding on load ───────────────────────

    [Fact]
    public void ValueData_LoadFrom_RoundsToStoreDecimalPlaces()
    {
        var source = new ValueData { Price = 1.23456789m, PriceVAT = 2.99999995m, VAT = null };
        var target = new ValueData();

        target.LoadFrom((IValueData)source);

        target.Price.Should().Be(Math.Round(1.23456789m, ValueData.StoreDecimalPlaces));
        target.PriceVAT.Should().Be(Math.Round(2.99999995m, ValueData.StoreDecimalPlaces));
        target.VAT.Should().BeNull();
    }

    [Fact]
    public void ValueData_CopyTo_CopiesAllValueFields()
    {
        var source = new ValueData { Price = 5m, PriceVAT = 6m, VAT = 1m };

        var clone = source.CopyTo(new ValueData());

        clone.Price.Should().Be(5m);
        clone.PriceVAT.Should().Be(6m);
        clone.VAT.Should().Be(1m);
    }

    // ── SourceValue extensions ───────────────────────────

    [Fact]
    public void GetValue_ReturnsMatchingSource()
    {
        var values = new[]
        {
            new SourceValue<int> { Source = "a", Value = 1 },
            new SourceValue<int> { Source = "b", Value = 2 },
        };

        values.GetValue("b").Should().Be(2);
    }

    [Fact]
    public void GetValue_MissingSource_ReturnsDefault()
    {
        var values = new[] { new SourceValue<int> { Source = "a", Value = 1 } };
        values.GetValue("z").Should().Be(0);
    }

    [Fact]
    public void GetValue_NullOrEmptySource_ReturnsDefault()
    {
        var values = new[] { new SourceValue<string?> { Source = "a", Value = "x" } };
        values.GetValue("").Should().BeNull();
        ((SourceValue<int>[])null!).GetValue("a").Should().Be(0);
    }

    // NOTE: These use string values, not int. The built-in Array.SetValue(object, int) instance
    // method would otherwise shadow the SourceValueExtensions.SetValue extension when the value is
    // an int (instance methods win over extensions), binding to the wrong (void-returning) method.

    [Fact]
    public void SetValue_AddsNewSource()
    {
        var values = new[] { new SourceValue<string> { Source = "a", Value = "1" } };

        var result = values.SetValue("b", "2");

        result.Should().HaveCount(2);
        result.GetValue("b").Should().Be("2");
    }

    [Fact]
    public void SetValue_UpdatesExistingSourceInPlace()
    {
        var values = new[] { new SourceValue<string> { Source = "a", Value = "1" } };

        var result = values.SetValue("a", "99");

        result.Should().HaveCount(1);
        result.GetValue("a").Should().Be("99");
    }

    [Fact]
    public void SetValue_NullArray_CreatesSingleEntry()
    {
        SourceValue<string>[] values = null!;

        var result = values.SetValue("a", "7");

        result.Should().HaveCount(1);
        result.GetValue("a").Should().Be("7");
    }

    [Fact]
    public void SetValue_EmptySource_ReturnsUnchanged()
    {
        var values = new[] { new SourceValue<string> { Source = "a", Value = "1" } };

        var result = values.SetValue("", "5");

        result.Should().BeSameAs(values);
    }
}
