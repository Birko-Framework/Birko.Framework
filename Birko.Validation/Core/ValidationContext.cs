using System;
using System.Collections.Generic;

namespace Birko.Validation;

/// <summary>
/// Context passed to validation rules, providing access to the model instance and metadata.
/// </summary>
public class ValidationContext
{
    public object Instance { get; }
    public Type InstanceType { get; }
    public string PropertyName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    /// <summary>
    /// Arbitrary data that rules can use (e.g., service locator, tenant info).
    /// </summary>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    public ValidationContext(object instance)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        InstanceType = instance.GetType();
    }

    public static ValidationContext<T> For<T>(T instance) => new(instance);
}

/// <summary>
/// Strongly-typed validation context.
/// </summary>
public class ValidationContext<T> : ValidationContext
{
    public new T Instance { get; }

    public ValidationContext(T instance) : base(instance!)
    {
        Instance = instance;
    }
}
