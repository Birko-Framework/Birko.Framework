using System;
using System.Collections.Generic;

namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Defines the shape of an aggregate — which related entities are included
/// and how they relate to the root entity.
/// </summary>
public interface IAggregateDefinition
{
    /// <summary>
    /// The root entity type of this aggregate.
    /// </summary>
    Type RootType { get; }

    /// <summary>
    /// All relationships defined for this aggregate.
    /// </summary>
    IReadOnlyList<RelationshipDescriptor> Relationships { get; }
}
