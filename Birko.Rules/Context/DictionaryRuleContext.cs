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

    public bool TryGetValue(string field, out object? value) => _values.TryGetValue(field, out value);
    public bool HasField(string field) => _values.ContainsKey(field);

    /// <summary>
    /// Fluent builder for quick context creation.
    /// </summary>
    public static DictionaryRuleContext From(params (string field, object? value)[] values)
    {
        var dict = new Dictionary<string, object?>(values.Length);
        foreach (var (field, value) in values)
            dict[field] = value;
        return new DictionaryRuleContext(dict);
    }
}
