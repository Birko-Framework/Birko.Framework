using System;

namespace Birko.Data.Views;

/// <summary>
/// Describes an aggregate operation in a view definition.
/// </summary>
public sealed class AggregateClause
{
    public AggregateFunction Function { get; }

    /// <summary>The source entity type containing the field to aggregate.</summary>
    public Type SourceType { get; }

    /// <summary>Property name on the source entity to aggregate. Null for Count(*).</summary>
    public string? SourceProperty { get; }

    /// <summary>Property name on the view result type that receives the aggregate value.</summary>
    public string ViewProperty { get; }

    public AggregateClause(AggregateFunction function, Type sourceType, string? sourceProperty, string viewProperty)
    {
        Function = function;
        SourceType = sourceType;
        SourceProperty = sourceProperty;
        ViewProperty = viewProperty;
    }
}
