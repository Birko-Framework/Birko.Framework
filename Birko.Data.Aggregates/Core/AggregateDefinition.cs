using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Base class for defining aggregate shapes. Derive from this and configure
/// relationships in the constructor using HasMany/HasOne.
/// </summary>
/// <example>
/// public class ProductAggregate : AggregateDefinition&lt;Product&gt;
/// {
///     public ProductAggregate()
///     {
///         HasMany(p => p.Categories)
///             .Through&lt;ProductCategory&gt;(j => j.ProductGuid, j => j.CategoryGuid);
///
///         HasMany(p => p.Tags)
///             .Via(t => t.ProductGuid);
///
///         HasOne(p => p.DefaultImage)
///             .Via(i => i.ProductGuid);
///     }
/// }
/// </example>
public abstract class AggregateDefinition<T> : IAggregateDefinition where T : AbstractModel
{
    private readonly List<RelationshipDescriptor> _relationships = [];

    public Type RootType => typeof(T);

    public IReadOnlyList<RelationshipDescriptor> Relationships => _relationships.AsReadOnly();

    /// <summary>
    /// Defines a one-to-many relationship (collection navigation property).
    /// </summary>
    protected RelationshipBuilder<T, TChild> HasMany<TChild>(Expression<Func<T, IEnumerable<TChild>>> navigation)
    {
        var propertyName = ExpressionHelper.GetPropertyName(navigation);
        var descriptor = new RelationshipDescriptor(typeof(T), typeof(TChild), propertyName, RelationshipType.OneToMany);
        _relationships.Add(descriptor);
        return new RelationshipBuilder<T, TChild>(descriptor);
    }

    /// <summary>
    /// Defines a one-to-one relationship (single navigation property).
    /// </summary>
    protected RelationshipBuilder<T, TChild> HasOne<TChild>(Expression<Func<T, TChild?>> navigation)
    {
        var propertyName = ExpressionHelper.GetPropertyName(navigation);
        var descriptor = new RelationshipDescriptor(typeof(T), typeof(TChild), propertyName, RelationshipType.OneToOne);
        _relationships.Add(descriptor);
        return new RelationshipBuilder<T, TChild>(descriptor);
    }
}
