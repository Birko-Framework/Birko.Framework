using System;
using System.Linq.Expressions;

namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Fluent builder for configuring a relationship in an aggregate definition.
/// </summary>
public class RelationshipBuilder<TParent, TChild>
{
    private readonly RelationshipDescriptor _descriptor;

    internal RelationshipBuilder(RelationshipDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    /// <summary>
    /// Configures a direct foreign key relationship (OneToOne or OneToMany).
    /// The child entity has a FK property pointing to the parent's Guid.
    /// </summary>
    /// <example>
    /// HasMany(p => p.Tags).Via(t => t.ProductGuid);
    /// HasOne(p => p.DefaultImage).Via(i => i.ProductGuid);
    /// </example>
    public RelationshipBuilder<TParent, TChild> Via<TFk>(Expression<Func<TChild, TFk>> foreignKey)
    {
        _descriptor.ForeignKeyProperty = ExpressionHelper.GetPropertyName(foreignKey);
        return this;
    }

    /// <summary>
    /// Configures a many-to-many relationship via a junction/bridge table.
    /// The junction entity has FKs to both parent and child.
    /// </summary>
    /// <example>
    /// HasMany(p => p.Categories)
    ///     .Through&lt;ProductCategory&gt;(j => j.ProductGuid, j => j.CategoryGuid);
    /// </example>
    public RelationshipBuilder<TParent, TChild> Through<TJunction>(
        Expression<Func<TJunction, object?>> parentFk,
        Expression<Func<TJunction, object?>> childFk)
    {
        _descriptor.Type = RelationshipType.ManyToMany;
        _descriptor.JunctionType = typeof(TJunction);
        _descriptor.JunctionParentFk = ExpressionHelper.GetPropertyName(parentFk);
        _descriptor.JunctionChildFk = ExpressionHelper.GetPropertyName(childFk);
        return this;
    }
}
