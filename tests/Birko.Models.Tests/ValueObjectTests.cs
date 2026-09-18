using System;
using Birko.Models.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Tests;

/// <summary>
/// CR-H124: coverage for the previously-untested Birko.Models value objects —
/// arithmetic, currency/unit-mismatch guards, tax derivation, rounding, and equality.
/// </summary>
public class ValueObjectTests
{
    // ── Money ────────────────────────────────────────────

    [Fact]
    public void Money_Ctor_RejectsBlankCurrency()
    {
        var act = () => new Money(1m, "  ");
        act.Should().Throw<ArgumentException>().WithParameterName("currencyCode");
    }

    [Fact]
    public void Money_Zero_HasZeroAmount()
    {
        Money.Zero("EUR").Amount.Should().Be(0m);
        Money.Zero("EUR").CurrencyCode.Should().Be("EUR");
    }

    [Fact]
    public void Money_Add_SameCurrency_Sums()
    {
        new Money(10m, "EUR").Add(new Money(5m, "EUR")).Should().Be(new Money(15m, "EUR"));
    }

    [Fact]
    public void Money_Add_DifferentCurrency_Throws()
    {
        var act = () => new Money(10m, "EUR").Add(new Money(5m, "USD"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*EUR*USD*");
    }

    [Fact]
    public void Money_Subtract_DifferentCurrency_Throws()
    {
        var act = () => new Money(10m, "EUR").Subtract(new Money(5m, "USD"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Money_Add_Null_Throws()
    {
        var act = () => new Money(10m, "EUR").Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Money_Multiply_And_Round()
    {
        new Money(10m, "EUR").Multiply(3m).Amount.Should().Be(30m);
        new Money(1.23456m, "EUR").Round(2).Should().Be(new Money(1.23m, "EUR"));
    }

    [Fact]
    public void Money_Equality_And_Operators()
    {
        var a = new Money(5m, "EUR");
        var b = new Money(5m, "EUR");
        var c = new Money(5m, "USD");

        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals((object)b).Should().BeTrue();
        a.Equals(null).Should().BeFalse();
        a!.ToString().Should().Be("5 EUR");
    }

    // ── Quantity ─────────────────────────────────────────

    [Fact]
    public void Quantity_Ctor_RejectsBlankUnit()
    {
        var act = () => new Quantity(1m, "");
        act.Should().Throw<ArgumentException>().WithParameterName("unit");
    }

    [Fact]
    public void Quantity_Add_UnitMismatch_Throws()
    {
        var act = () => new Quantity(1m, "kg").Add(new Quantity(1m, "l"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*kg*l*");
    }

    [Fact]
    public void Quantity_Subtract_And_Multiply()
    {
        new Quantity(5m, "kg").Subtract(new Quantity(2m, "kg")).Should().Be(new Quantity(3m, "kg"));
        new Quantity(5m, "kg").Multiply(2m).Amount.Should().Be(10m);
        Quantity.Zero("kg").Amount.Should().Be(0m);
    }

    // ── MoneyWithTax ─────────────────────────────────────

    [Fact]
    public void MoneyWithTax_FromNetAndVat_DerivesGross()
    {
        var m = MoneyWithTax.FromNetAndVat(100m, 20m);
        m.Price.Should().Be(100m);
        m.VAT.Should().Be(20m);
        m.PriceVAT.Should().Be(120m);
    }

    [Fact]
    public void MoneyWithTax_FromGrossAndVat_DerivesNet()
    {
        var m = MoneyWithTax.FromGrossAndVat(120m, 20m);
        m.Price.Should().Be(100m);
        m.PriceVAT.Should().Be(120m);
        m.VAT.Should().Be(20m);
    }

    [Fact]
    public void MoneyWithTax_Empty_IsAllNull()
    {
        var m = MoneyWithTax.Empty;
        m.Price.Should().BeNull();
        m.PriceVAT.Should().BeNull();
        m.VAT.Should().BeNull();
    }

    [Fact]
    public void MoneyWithTax_Round_PreservesNulls()
    {
        var m = new MoneyWithTax(1.239m, null, 0.2478m).Round(2);
        m.Price.Should().Be(1.24m);
        m.PriceVAT.Should().BeNull();
        m.VAT.Should().Be(0.25m);
    }

    [Fact]
    public void MoneyWithTax_Equality()
    {
        var a = MoneyWithTax.FromNetAndVat(100m, 20m);
        var b = MoneyWithTax.FromNetAndVat(100m, 20m);
        (a == b).Should().BeTrue();
        (a != MoneyWithTax.Empty).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ── Percentage ───────────────────────────────────────

    [Fact]
    public void Percentage_ApplyTo_And_AddTo()
    {
        var twenty = new Percentage(20m);
        twenty.ApplyTo(100m).Should().Be(20m);
        twenty.AddTo(100m).Should().Be(120m);
        Percentage.Zero.Value.Should().Be(0m);
    }

    [Fact]
    public void Percentage_Conversions()
    {
        decimal asDecimal = new Percentage(15m);   // implicit
        asDecimal.Should().Be(15m);
        var back = (Percentage)15m;                 // explicit
        back.Should().Be(new Percentage(15m));
        new Percentage(15m).ToString().Should().Be("15%");
    }

    // ── PostalAddress ────────────────────────────────────

    [Fact]
    public void PostalAddress_NullRequiredField_Throws()
    {
        var act = () => new PostalAddress(null!, "1", "City", "000", "CC");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PostalAddress_Equality_IncludesState()
    {
        var a = new PostalAddress("Main", "1", "Town", "12345", "US", "CA");
        var b = new PostalAddress("Main", "1", "Town", "12345", "US", "CA");
        var c = new PostalAddress("Main", "1", "Town", "12345", "US", "NY");
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
    }

    [Fact]
    public void PostalAddress_ToString_OmitsBlankState()
    {
        new PostalAddress("Main", "1", "Town", "12345", "US")
            .ToString().Should().Be("Main 1, 12345 Town, US");
        new PostalAddress("Main", "1", "Town", "12345", "US", "CA")
            .ToString().Should().Contain(", CA,");
    }
}
