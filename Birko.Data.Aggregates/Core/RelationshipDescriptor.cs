using System;

namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Metadata describing a single relationship within an aggregate definition.
/// </summary>
public class RelationshipDescriptor
{
    /// <summary>
    /// Type of relationship (OneToOne, OneToMany, ManyToMany).
    /// </summary>
    public RelationshipType Type { get; internal set; }

    /// <summary>
    /// The root/parent entity type.
    /// </summary>
    public Type ParentType { get; }

    /// <summary>
    /// The related/child entity type.
    /// </summary>
    public Type ChildType { get; }

    /// <summary>
    /// Property name on the parent entity that holds the related data (e.g., "Categories").
    /// </summary>
    public string NavigationProperty { get; }

    /// <summary>
    /// Foreign key property name on the child entity pointing to the parent (for OneToOne/OneToMany).
    /// </summary>
    public string? ForeignKeyProperty { get; internal set; }

    /// <summary>
    /// Junction/bridge table entity type (for ManyToMany).
    /// </summary>
    public Type? JunctionType { get; internal set; }

    /// <summary>
    /// FK property name on the junction table pointing to the parent entity.
    /// </summary>
    public string? JunctionParentFk { get; internal set; }

    /// <summary>
    /// FK property name on the junction table pointing to the child entity.
    /// </summary>
    public string? JunctionChildFk { get; internal set; }

    public RelationshipDescriptor(Type parentType, Type childType, string navigationProperty, RelationshipType type)
    {
        ParentType = parentType ?? throw new ArgumentNullException(nameof(parentType));
        ChildType = childType ?? throw new ArgumentNullException(nameof(childType));
        NavigationProperty = navigationProperty ?? throw new ArgumentNullException(nameof(navigationProperty));
        Type = type;
    }
}
