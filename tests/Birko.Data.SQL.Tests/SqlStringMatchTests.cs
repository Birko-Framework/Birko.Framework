using System;
using System.Linq;
using Birko.Data.SQL.Conditions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-869 — <see cref="SqlStringMatch"/>, the one producer of the patterns an Ordinal / OrdinalIgnoreCase string comparison
/// binds. Its case classes must be C#'s <c>OrdinalIgnoreCase</c> classes exactly, or every provider's
/// OrdinalIgnoreCase form is wrong in the same way.
/// </summary>
public class SqlStringMatchTests
{
    [Fact]
    public void Every_case_class_is_exactly_what_OrdinalIgnoreCase_calls_equal_over_planes_0_and_1()
    {
        var wrong = 0;
        for (var codePoint = 0; codePoint <= 0x1FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF) continue;
            var text = char.ConvertFromUtf32(codePoint);
            var variants = SqlStringMatch.Elements(text).Single().Variants ?? new[] { text };
            // Every member is equal to it.
            if (variants.Any(v => !string.Equals(v, text, StringComparison.OrdinalIgnoreCase))) wrong++;
            // Its upper and lower case are members exactly when C# calls them equal.
            foreach (var other in new[] { text.ToUpperInvariant(), text.ToLowerInvariant() })
                if (string.Equals(other, text, StringComparison.OrdinalIgnoreCase) != variants.Contains(other)) wrong++;
        }
        wrong.Should().Be(0);
    }

    [Theory]
    [InlineData("i", new[] { "I", "i" })]
    [InlineData("ı", null)]
    [InlineData("ſ", null)]
    [InlineData("σ", new[] { "Σ", "ς", "σ" })]
    [InlineData("k", new[] { "K", "k" })]
    [InlineData("ǅ", new[] { "Ǆ", "ǅ", "ǆ" })]
    [InlineData("1", null)]
    public void A_character_has_the_case_variants_CSharp_gives_it(string text, string[]? expected)
    {
        var variants = SqlStringMatch.Elements(text).Single().Variants;
        if (expected == null)
            variants.Should().BeNull();
        else
            variants.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(ConditionType.Like, "a%b_c[!", "%a!%b!_c![!!%")]
    [InlineData(ConditionType.StartsWith, "ab", "ab%")]
    [InlineData(ConditionType.EndsWith, "ab", "%ab")]
    [InlineData(ConditionType.TextEqual, "a_b", "a!_b")]
    public void The_ordinal_LIKE_pattern_escapes_every_metacharacter(ConditionType kind, string term, string pattern)
        => SqlStringMatch.LikePattern(kind, term).Should().Be(pattern);

    [Fact]
    public void The_case_class_LIKE_pattern_brackets_each_cased_character_and_escapes_the_rest()
        => SqlStringMatch.LikeCaseClassPattern(ConditionType.StartsWith, "a_1").Should().Be("[Aa]!_1%");

    [Fact]
    public void The_case_class_LIKE_pattern_refuses_a_cased_letter_outside_the_BMP_by_name()
    {
        var act = () => SqlStringMatch.LikeCaseClassPattern(ConditionType.Like, "\U00010428");
        act.Should().Throw<NotSupportedException>().WithMessage("*Basic Multilingual Plane*");
    }

    [Theory]
    [InlineData(ConditionType.Like, "a.b", "[Aa]\\.[Bb]")]
    [InlineData(ConditionType.StartsWith, "1$", "^1\\$")]
    [InlineData(ConditionType.EndsWith, "(", "\\($")]
    [InlineData(ConditionType.TextEqual, "", "^$")]
    [InlineData(ConditionType.Like, "", "^")]
    [InlineData(ConditionType.EndsWith, "", "^")]
    public void The_case_class_regex_quotes_punctuation_and_anchors_by_kind(ConditionType kind, string term, string pattern)
        => SqlStringMatch.RegexCaseClassPattern(kind, term, "^", "$").Should().Be(pattern);
}
