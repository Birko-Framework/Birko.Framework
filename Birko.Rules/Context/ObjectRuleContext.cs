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
    private static readonly ConcurrentDictionary<string, PropertyInfo[]?> PathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly T _instance;

    public ObjectRuleContext(T instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>
    /// The value of <paramref name="field"/>, a property name or a dotted path (<c>Address.City</c>). False when the type
    /// has no such property, or when an intermediate of the path is null: there is no value to compare, which the store
    /// filter answers the same way (its null guards, TASK-144).
    /// </summary>
    public bool TryGetValue(string field, out object? value)
    {
        value = null;
        var path = GetPath(field);
        if (path is null)
            return false;

        object? current = _instance;
        for (var i = 0; i < path.Length; i++)
        {
            if (current is null)
                return false;
            current = path[i].GetValue(current);
        }
        value = current;
        return true;
    }

    public bool HasField(string field) => GetPath(field) is not null;

    /// <summary>A typed entity's fields are its properties: one it does not have is a mistake in the rule.</summary>
    public bool HasFixedFields => true;

    private static PropertyInfo[]? GetPath(string field)
    {
        return PathCache.GetOrAdd(field, static name =>
        {
            var parts = name.Split('.');
            var path = new PropertyInfo[parts.Length];
            var current = typeof(T);
            for (var i = 0; i < parts.Length; i++)
            {
                var prop = current.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is null)
                    return null;
                path[i] = prop;
                current = prop.PropertyType;
            }
            return path;
        });
    }
}
