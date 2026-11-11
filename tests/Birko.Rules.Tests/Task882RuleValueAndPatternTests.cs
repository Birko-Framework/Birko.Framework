using System;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-882 — the two producers every runner of a rule now reads through: <see cref="RuleLikePattern"/> (a <c>Like</c>
/// rule's pattern) and <see cref="RuleValueConverter"/> (a rule value as a value of the member's type).
/// </summary>
public class Task882RuleValueAndPatternTests
{
    [Theory]
    [InlineData("abc", false, false, "abc")]
    [InlineData("%abc", true, false, "abc")]
    [InlineData("abc%", false, true, "abc")]
    [InlineData("%abc%", true, true, "abc")]
    [InlineData("%", true, true, "")]
    [InlineData("%%%", true, true, "")]
    [InlineData("a%%", false, true, "a")]
    [InlineData("a_c", false, false, "a_c")]
    public void A_like_pattern_parses_into_its_anchors(string pattern, bool anyPrefix, bool anySuffix, string core)
    {
        var parsed = RuleLikePattern.Parse(pattern, "Name");

        parsed.Should().NotBeNull();
        parsed!.Value.AnyPrefix.Should().Be(anyPrefix);
        parsed.Value.AnySuffix.Should().Be(anySuffix);
        parsed.Value.Core.Should().Be(core);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_like_pattern_is_no_pattern(string? pattern)
        => RuleLikePattern.Parse(pattern, "Name").Should().BeNull();

    [Theory]
    [InlineData("a%b")]
    [InlineData("%a%b%")]
    [InlineData("a%%b")]
    public void A_like_pattern_with_an_interior_wildcard_is_refused_by_field_never_by_value(string pattern)
    {
        Action parse = () => RuleLikePattern.Parse(pattern, "Name");
        parse.Should().Throw<NotSupportedException>().WithMessage("*'Name'*interior*")
            .Which.Message.Should().NotContain(pattern, "a rule value is data and the message travels into logs");
    }

    [Theory]
    [InlineData("%B%", "abc", true)]
    [InlineData("AB%", "abc", true)]
    [InlineData("%BC", "abc", true)]
    [InlineData("ABC", "abc", true)]
    [InlineData("ABC", "abc ", false)]
    [InlineData("a_c", "abc", false)]
    [InlineData("I", "ı", false)]
    public void A_like_pattern_matches_ignoring_case_ordinally(string pattern, string value, bool expected)
        => RuleLikePattern.Parse(pattern, "Name")!.Value.IsMatch(value).Should().Be(expected);

    [Fact]
    public void A_like_pattern_never_matches_null()
        => RuleLikePattern.Parse("%", "Name")!.Value.IsMatch(null).Should().BeFalse();

    [Theory]
    [InlineData("Active", typeof(RuleLeftoverState), RuleLeftoverState.Active)]
    [InlineData("closed", typeof(RuleLeftoverState?), RuleLeftoverState.Closed)]
    [InlineData(1, typeof(RuleLeftoverState), RuleLeftoverState.Active)]
    public void A_rule_value_converts_to_an_enum(object value, Type target, object expected)
    {
        RuleValueConverter.TryConvert(value, target, out var converted).Should().BeTrue();
        converted.Should().Be(expected);
    }

    [Fact]
    public void A_rule_value_converts_text_to_a_guid_a_date_and_a_number_invariantly()
    {
        RuleValueConverter.TryConvert(RuleLeftoverShapes.G1.ToString("N"), typeof(Guid), out var guid).Should().BeTrue();
        guid.Should().Be(RuleLeftoverShapes.G1);
        RuleValueConverter.TryConvert("2026-01-02T03:04:05", typeof(DateTime?), out var date).Should().BeTrue();
        date.Should().Be(RuleLeftoverShapes.D2);
        RuleValueConverter.TryConvert("2.5", typeof(decimal), out var number).Should().BeTrue();
        number.Should().Be(2.5m);
    }

    [Theory]
    [InlineData("Bogus", typeof(RuleLeftoverState))]
    [InlineData("not-a-guid", typeof(Guid))]
    [InlineData("abc", typeof(int))]
    [InlineData("5.5", typeof(int))]
    [InlineData(null, typeof(int))]
    public void A_rule_value_without_a_form_for_the_member_does_not_convert(object? value, Type target)
        => RuleValueConverter.TryConvert(value, target, out _).Should().BeFalse();

    [Fact]
    public void A_null_rule_value_converts_for_a_member_that_holds_null()
    {
        RuleValueConverter.TryConvert(null, typeof(int?), out var converted).Should().BeTrue();
        converted.Should().BeNull();
        RuleValueConverter.TryConvert(null, typeof(string), out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(10.7, typeof(int), true)]
    [InlineData(10.0, typeof(int), false)]
    [InlineData(10.7, typeof(decimal), false)]
    [InlineData(10, typeof(int?), false)]
    public void A_fraction_against_an_integral_member_is_compared_not_converted(object value, Type member, bool expected)
        => RuleValueConverter.ComparesAsFraction(value, member).Should().Be(expected);
}
