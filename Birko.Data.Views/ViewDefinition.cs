using System;
using System.Collections.Generic;

namespace Birko.Data.Views;

/// <summary>
/// Immutable, platform-agnostic view definition produced by <see cref="ViewDefinitionBuilder{TView}"/>.
/// Platform translators read this to produce native queries or persistent views.
/// </summary>
public sealed class ViewDefinition
{
    /// <summary>Persistent view name (required for Persistent/Auto modes).</summary>
    public string? Name { get; }

    /// <summary>Query execution mode.</summary>
    public ViewQueryMode QueryMode { get; }

    /// <summary>The primary source entity type (the From type).</summary>
    public Type PrimarySource { get; }

    /// <summary>The view result type (TView).</summary>
    public Type ViewType { get; }

    /// <summary>Fields selected from source entities into the view.</summary>
    public IReadOnlyList<FieldSelector> Fields { get; }

    /// <summary>Join relationships between source entities.</summary>
    public IReadOnlyList<JoinClause> Joins { get; }

    /// <summary>Aggregate operations.</summary>
    public IReadOnlyList<AggregateClause> Aggregates { get; }

    /// <summary>GroupBy fields (required when aggregates are present).</summary>
    public IReadOnlyList<GroupByClause> GroupBy { get; }

    /// <summary>Platform-specific hints (e.g., MaterializedViewType for SQL, partition key for Cosmos).</summary>
    public IReadOnlyDictionary<string, object> Hints { get; }

    public bool HasAggregates => Aggregates.Count > 0;
    public bool HasJoins => Joins.Count > 0;
    public bool HasGroupBy => GroupBy.Count > 0;

    internal ViewDefinition(
        string? name,
        ViewQueryMode queryMode,
        Type primarySource,
        Type viewType,
        IReadOnlyList<FieldSelector> fields,
        IReadOnlyList<JoinClause> joins,
        IReadOnlyList<AggregateClause> aggregates,
        IReadOnlyList<GroupByClause> groupBy,
        IReadOnlyDictionary<string, object> hints)
    {
        Name = name;
        QueryMode = queryMode;
        PrimarySource = primarySource;
        ViewType = viewType;
        Fields = fields;
        Joins = joins;
        Aggregates = aggregates;
        GroupBy = groupBy;
        Hints = hints;
    }
}
