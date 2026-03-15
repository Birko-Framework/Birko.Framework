using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Birko.Rules;

/// <summary>
/// Rule context that reads property values from any object via reflection.
/// Caches property lookups per type for performance.
/// </summary>
public class ObjectRuleContext<T> : IRuleContext where T : class
{
    private static readonly ConcurrentDictionary<string, PropertyInfo?> PropertyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly T _instance;

    public ObjectRuleContext(T instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public bool TryGetValue(string field, out object? value)
    {
        var prop = GetProperty(field);
        if (prop is null)
        {
            value = null;
            return false;
        }

        value = prop.GetValue(_instance);
        return true;
    }

    public bool HasField(string field) => GetProperty(field) is not null;

    private static PropertyInfo? GetProperty(string field)
    {
        return PropertyCache.GetOrAdd(field, name =>
            typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }
}
