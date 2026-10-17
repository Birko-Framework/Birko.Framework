using System;
using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// Rule context backed by a string→object dictionary.
/// Simplest context — use for IoT telemetry, ad-hoc data, etc.
/// </summary>
public class DictionaryRuleContext : IRuleContext
{
    private readonly Dictionary<string, object?> _values;

    public DictionaryRuleContext(Dictionary<string, object?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <remarks>
    /// SH-M369 (TASK-156): a field resolves case-insensitively, as on <see cref="ObjectRuleContext{T}"/> and in the
    /// translator. The caller's dictionary keeps its own comparer; an exact miss falls back to a case-insensitive key.
    /// TASK-651: when two keys differ only in case ("Name" and "name") and neither matches exactly, the fallback is
    /// ambiguous and is refused: it answered with whichever the dictionary enumerated first, which is not guaranteed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">More than one key matches <paramref name="field"/> ignoring case.</exception>
    public bool TryGetValue(string field, out object? value)
    {
        if (_values.TryGetValue(field, out value))
            return true;
        string? found = null;
        foreach (var pair in _values)
        {
            if (!string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase))
                continue;
            if (found != null)
                throw new InvalidOperationException(
                    $"Field '{field}' matches both '{found}' and '{pair.Key}' ignoring case; use the exact key.");
            found = pair.Key;
            value = pair.Value;
        }
        if (found != null)
            return true;
        value = null;
        return false;
    }

    public bool HasField(string field) => TryGetValue(field, out _);

    /// <summary>
    /// Fluent builder for quick context creation.
    /// </summary>
    public static DictionaryRuleContext From(params (string field, object? value)[] values)
    {
        var dict = new Dictionary<string, object?>(values.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var (field, value) in values)
            dict[field] = value;
        return new DictionaryRuleContext(dict);
    }
}
