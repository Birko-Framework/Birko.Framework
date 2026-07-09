using System.Collections.Generic;
using Birko.Models.Pricing;
using FluentAssertions;
using Xunit;
using CurrencyVm = Birko.Models.Pricing.ViewModels.Currency;
using TaxVm = Birko.Models.Pricing.ViewModels.Tax;

namespace Birko.Models.Pricing.Tests;

/// <summary>
/// CR-H130: coverage for the previously-untested Birko.Models.Pricing — model CopyTo/LoadFrom
/// round-trips and the ViewModel PropertyChanged fan-out (only a subset of properties raise the
/// aggregate *ObjectProperty event).
/// </summary>
public class PricingModelTests
{
    // ── Model CopyTo / LoadFrom round-trips ──────────────

    [Fact]
    public void Tax_CopyTo_CopiesAllFields()
    {
        var source = new Tax { Name = "Standard", ShortCut = "S", Percentage = 20m, IsDefault = true };

        var clone = source.CopyTo(new Tax());

        clone.Name.Should().Be("Standard");
        clone.ShortCut.Should().Be("S");
        clone.Percentage.Should().Be(20m);
        clone.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Tax_LoadFrom_CopiesAllFields()
    {
        var vm = new TaxVm { Name = "Reduced", ShortCut = "R", Percentage = 10m, IsDefault = false };

        var model = new Tax();
        model.LoadFrom(vm);

        model.Name.Should().Be("Reduced");
        model.ShortCut.Should().Be("R");
        model.Percentage.Should().Be(10m);
        model.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Currency_CopyTo_CopiesAllFields()
    {
        var source = new Currency { Code = "EUR", Name = "Euro", Symbol = "€", IsLeftSymbol = false, IsDefault = true };

        var clone = source.CopyTo(new Currency());

        clone.Code.Should().Be("EUR");
        clone.Name.Should().Be("Euro");
        clone.Symbol.Should().Be("€");
        clone.IsLeftSymbol.Should().BeFalse();
        clone.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void PriceGroup_LoadFrom_CopiesAllFields()
    {
        var vm = new Birko.Models.Pricing.ViewModels.PriceGroup { Name = "Wholesale", Percentage = -15m, IsDefault = true };

        var model = new PriceGroup();
        model.LoadFrom(vm);

        model.Name.Should().Be("Wholesale");
        model.Percentage.Should().Be(-15m);
        model.IsDefault.Should().BeTrue();
    }

    // ── ViewModel PropertyChanged fan-out ────────────────

    [Fact]
    public void CurrencyVm_FansOutObjectEvent_ForVisualProperties()
    {
        var vm = new CurrencyVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Code = "USD";
        vm.Name = "Dollar";
        vm.Symbol = "$";
        vm.IsLeftSymbol = true;

        raised.Should().Contain(CurrencyVm.CurrencyObjectProperty);
        // Each of the four visual properties should trigger the aggregate event.
        raised.FindAll(p => p == CurrencyVm.CurrencyObjectProperty).Should().HaveCount(4);
    }

    [Fact]
    public void CurrencyVm_IsDefault_DoesNotFanOut()
    {
        var vm = new CurrencyVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsDefault = true;

        // IsDefault is not a RaisePropertyChanged property and is excluded from the fan-out.
        raised.Should().NotContain(CurrencyVm.CurrencyObjectProperty);
    }

    [Fact]
    public void TaxVm_FansOutObjectEvent_OnlyForNameAndShortCut()
    {
        var vm = new TaxVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "Standard";
        vm.ShortCut = "S";

        raised.FindAll(p => p == TaxVm.TaxObjectProperty).Should().HaveCount(2);
    }
}
