namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Type of relationship between entities in an aggregate definition.
/// </summary>
public enum RelationshipType
{
    /// <summary>
    /// One-to-one relationship (parent has single child via FK on child).
    /// </summary>
    OneToOne,

    /// <summary>
    /// One-to-many relationship (parent has collection of children via FK on child).
    /// </summary>
    OneToMany,

    /// <summary>
    /// Many-to-many relationship via a junction/bridge table.
    /// </summary>
    ManyToMany
}
