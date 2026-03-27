using System;

namespace Birko.Data.Views;

/// <summary>
/// Describes a GROUP BY field in a view definition.
/// </summary>
public sealed class GroupByClause
{
    public Type SourceType { get; }
    public string PropertyName { get; }

    public GroupByClause(Type sourceType, string propertyName)
    {
        SourceType = sourceType;
        PropertyName = propertyName;
    }
}
