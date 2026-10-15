namespace Birko.Rules;

/// <summary>
/// Provides field values for rule evaluation.
/// Abstracts the data source — could be a dictionary, an object, a DB row, a telemetry reading.
/// </summary>
public interface IRuleContext
{
    /// <summary>
    /// Try to get a field value by name. Returns false if field doesn't exist.
    /// </summary>
    bool TryGetValue(string field, out object? value);

    /// <summary>
    /// Check if a field exists in this context.
    /// </summary>
    bool HasField(string field);

    /// <summary>
    /// True when the fields are fixed by a type, so a field this context cannot reach is a mistake in the rule (a typo, a
    /// null intermediate of a nested path), not an absent value. False (the default) for an open context such as a
    /// dictionary, where a missing key legitimately reads as null for <c>IsNull</c> / <c>IsNotNull</c>.
    /// </summary>
    /// <remarks>
    /// TASK-144 (SH-M297). <see cref="RuleEvaluator"/> read every missing field as null for the null checks, so on a typed
    /// entity <c>Typo IsNull</c> matched every entity in memory while <see cref="RuleExpressionConverter"/> (the store
    /// filter) matched none. A fixed-field context makes an unreachable field match nothing, negated or not, as the
    /// store does.
    /// </remarks>
    bool HasFixedFields => false;
}
