using System;

namespace Birko.Data.Views;

/// <summary>
/// Describes a field selected from a source entity into a view.
/// </summary>
public sealed class FieldSelector
{
    /// <summary>The source entity type containing the field.</summary>
    public Type SourceType { get; }

    /// <summary>Property name on the source entity.</summary>
    public string SourceProperty { get; }

    /// <summary>Property name on the view result type.</summary>
    public string ViewProperty { get; }

    public FieldSelector(Type sourceType, string sourceProperty, string viewProperty)
    {
        SourceType = sourceType;
        SourceProperty = sourceProperty;
        ViewProperty = viewProperty;
    }
}
