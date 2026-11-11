using System;

namespace Birko.Rules;

/// <summary>
/// TASK-882 — a <see cref="ComparisonOperator.Like"/> rule's pattern, as every runner of a rule reads it: <c>%</c> at the
/// start and/or the end is "anything", the rest is literal (<c>_</c> included), compared with
/// <see cref="StringComparison.OrdinalIgnoreCase"/> as the other string operators are. The one parser for
/// <see cref="RuleEvaluator"/>, <see cref="RuleExpressionConverter"/> and the SQL rule converter.
/// </summary>
/// <remarks>
/// <para>
/// <b>An interior <c>%</c> (<c>a%b</c>) is refused by name</b> (<see cref="NotSupportedException"/>), by every runner, whatever
/// the data. The compiled rule has no store-translatable form for it (no string method matches "a, then anything, then
/// b"), so a runner that answered it would answer alone. Before (measured, TASK-882), the in-memory runners matched
/// nothing (SH-M362, TASK-156) while SQL compared the literal text <c>a%b</c>: two answers for one rule.
/// </para>
/// <para>
/// Before, too, the SQL rule parsed the pattern itself: <c>a%%</c> became a prefix search for the literal <c>a%</c>, where
/// the in-memory runners read the prefix <c>a</c>.
/// </para>
/// </remarks>
public readonly struct RuleLikePattern
{
    private RuleLikePattern(bool anyPrefix, bool anySuffix, string core)
    {
        AnyPrefix = anyPrefix;
        AnySuffix = anySuffix;
        Core = core;
    }

    /// <summary>The pattern starts with <c>%</c>: anything may come before <see cref="Core"/>.</summary>
    public bool AnyPrefix { get; }

    /// <summary>The pattern ends with <c>%</c>: anything may come after <see cref="Core"/>.</summary>
    public bool AnySuffix { get; }

    /// <summary>The literal text between the wildcards.</summary>
    public string Core { get; }

    /// <summary>
    /// The pattern of a <c>Like</c> rule's <paramref name="value"/>, or null when there is none (a null or empty value): no
    /// predicate, so the rule matches nothing, negated or not (TASK-116).
    /// </summary>
    /// <param name="field">The rule's field, named by the refusal.</param>
    /// <exception cref="NotSupportedException">The pattern has a <c>%</c> between other characters.</exception>
    public static RuleLikePattern? Parse(object? value, string field)
    {
        var pattern = value?.ToString();
        if (string.IsNullOrEmpty(pattern))
            return null;

        var core = pattern.Trim('%');
        if (core.Contains('%'))
        {
            // The message names the field, never the pattern: a rule value is data, and this travels into logs (rule 42).
            throw new NotSupportedException(
                $"Cannot evaluate the Like rule on '{field}': its pattern has an interior '%' (between other characters). "
                + "No runner of a rule has one answer for it - the compiled rule has no form a store translates - so "
                + "RuleEvaluator, RuleExpressionConverter and the SQL rule converter all refuse it. Put '%' only at the start "
                + "and/or the end of the pattern.");
        }

        return new RuleLikePattern(pattern.StartsWith('%'), pattern.EndsWith('%'), core);
    }

    /// <summary>Whether <paramref name="value"/> matches, ignoring case ordinally. A null value never matches.</summary>
    public bool IsMatch(string? value)
    {
        if (value is null)
            return false;
        return (AnyPrefix, AnySuffix) switch
        {
            (true, true) => value.Contains(Core, StringComparison.OrdinalIgnoreCase),
            (true, false) => value.EndsWith(Core, StringComparison.OrdinalIgnoreCase),
            (false, true) => value.StartsWith(Core, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(value, Core, StringComparison.OrdinalIgnoreCase),
        };
    }
}
