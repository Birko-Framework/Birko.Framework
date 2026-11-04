using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Birko.Messaging;
using Birko.Messaging.Templates;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Templates;

/// <summary>
/// TASK-815: a placeholder may name a filter, <c>{{Path|name}}</c> / <c>{{Path|name:ArgPath}}</c>, and the <c>money</c>
/// filter writes an amount in the argument's currency the way the render culture writes money.
/// </summary>
/// <remarks>
/// The expected money strings are what the browser's <c>Intl.NumberFormat(lang, { style: 'currency', currency })</c>
/// prints (U+00A0 for every space), which is what a storefront shows next to the e-mail.
/// </remarks>
public class TemplateFilterTests
{
    private const char Nbsp = ' ';

    private readonly StringTemplateEngine _engine = new();

    private sealed record Payment(decimal Amount, string CurrencyCode);
    private sealed record Model(decimal TotalAmount, string CurrencyCode, Payment Payment, decimal? Missing = null);

    private static TemplateRenderOptions WithMoney(ITemplateValueEncoder? encoder = null) => new()
    {
        Encoder = encoder,
        Filters = new Dictionary<string, ITemplateValueFilter>(StringComparer.OrdinalIgnoreCase)
        {
            [MoneyTemplateFilter.Name] = new MoneyTemplateFilter(),
        },
    };

    private static async Task<string> InCulture(string culture, Func<Task<string>> render)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try { return await render(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ── The filter syntax ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFilter_ReceivesTheValue_AndTheArgumentResolvedAsAModelPath()
    {
        var seen = new Recording();
        var options = new TemplateRenderOptions
        {
            Filters = new Dictionary<string, ITemplateValueFilter> { ["probe"] = seen },
        };

        var text = await _engine.RenderAsync(
            "[{{Payment.Amount|probe:Payment.CurrencyCode}}] [{{TotalAmount|probe}}]",
            new Model(1m, "EUR", new Payment(2.5m, "CZK")), options);

        text.Should().Be("[2.5/CZK] [1/]");
    }

    [Fact]
    public async Task AFilterNameTheOptionsDoNotCarry_Throws_RatherThanShippingTheMarker()
    {
        var act = () => _engine.RenderAsync("{{TotalAmount|monye:CurrencyCode}}", new Model(1m, "EUR", new Payment(1m, "EUR")), WithMoney());

        (await act.Should().ThrowAsync<TemplateRenderException>()).WithMessage("*Unknown filter 'monye'*");
    }

    [Fact]
    public async Task AFilterRenderedWithoutOptions_Throws()
    {
        var act = () => _engine.RenderAsync("{{TotalAmount|money:CurrencyCode}}", new Model(1m, "EUR", new Payment(1m, "EUR")));

        await act.Should().ThrowAsync<TemplateRenderException>();
    }

    [Fact]
    public async Task APlaceholderWithoutAFilter_IsUnchanged()
    {
        var text = await InCulture("en", () => _engine.RenderAsync("{{TotalAmount}} {{CurrencyCode}}", new Model(390.0m, "CZK", new Payment(1m, "CZK")), WithMoney()));

        text.Should().Be("390.0 CZK", "a raw placeholder keeps printing the value as it is; only a filter formats it");
    }

    [Fact]
    public async Task TheEncoderRunsOnTheFiltersOutput()
    {
        var text = await _engine.RenderAsync("{{TotalAmount|money:CurrencyCode}}",
            new Model(1m, "EUR", new Payment(1m, "EUR")), WithMoney(new Upper()));

        text.Should().StartWith("ENCODED:");
    }

    // ── money ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cs", "CZK", "390,00 Kč")]
    [InlineData("cs", "EUR", "390,00 €")]
    [InlineData("en", "CZK", "CZK 390.00")]
    [InlineData("en", "EUR", "€390.00")]
    [InlineData("cs-CZ", "CZK", "390,00 Kč")]
    [InlineData("en-US", "CZK", "CZK 390.00")]
    public async Task Money_IsWrittenTheWayTheCultureWritesMoney_InTheArgumentsCurrency(string culture, string currency, string expected)
    {
        var text = await InCulture(culture, () => _engine.RenderAsync("{{TotalAmount|money:CurrencyCode}}",
            new Model(390m, currency, new Payment(1m, currency)), WithMoney()));

        text.Should().Be(expected.Replace(' ', Nbsp));
    }

    /// <summary>The bug this exists for: one e-mail printed <c>390</c> and <c>390.0</c> for the same amount.</summary>
    [Theory]
    [InlineData("390")]
    [InlineData("390.0")]
    [InlineData("390.000")]
    public async Task Money_PrintsTheCurrencysPlaces_WhateverTheValuesScale(string stored)
    {
        var amount = decimal.Parse(stored, CultureInfo.InvariantCulture);

        var text = await InCulture("cs", () => _engine.RenderAsync("{{TotalAmount|money:CurrencyCode}}",
            new Model(amount, "CZK", new Payment(1m, "CZK")), WithMoney()));

        text.Should().Be($"390,00{Nbsp}Kč");
    }

    [Fact]
    public async Task Money_GroupsThousands_WithNoBreakSpacesOnly()
    {
        var text = await InCulture("cs", () => _engine.RenderAsync("{{TotalAmount|money:CurrencyCode}}",
            new Model(1200m, "CZK", new Payment(1m, "CZK")), WithMoney()));

        text.Should().Be($"1{Nbsp}200,00{Nbsp}Kč");
        text.Should().NotContain(" ");
    }

    [Fact]
    public async Task Money_OfNothing_IsEmpty()
    {
        var text = await _engine.RenderAsync("[{{Missing|money:CurrencyCode}}]",
            new Model(1m, "CZK", new Payment(1m, "CZK")), WithMoney());

        text.Should().Be("[]");
    }

    [Fact]
    public async Task Money_WithoutACurrency_Throws()
    {
        var act = () => _engine.RenderAsync("{{TotalAmount|money}}", new Model(1m, "CZK", new Payment(1m, "CZK")), WithMoney());

        (await act.Should().ThrowAsync<TemplateRenderException>()).WithMessage("*currency code*");
    }

    // ── fakes ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class Recording : ITemplateValueFilter
    {
        public string Apply(object? value, object? argument, CultureInfo culture)
            => $"{Convert.ToString(value, CultureInfo.InvariantCulture)}/{argument}";
    }

    private sealed class Upper : ITemplateValueEncoder
    {
        public string Encode(string value) => "ENCODED:" + value;
    }
}
