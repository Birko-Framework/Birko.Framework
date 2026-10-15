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

    /// <remarks>
    /// SH-M069 (TASK-157): reading the relationships refuses one that was never given a key. A navigation registered
    /// with no <c>Via</c> or <c>Through</c> handed a null foreign key straight to the data provider.
    /// </remarks>
    public IReadOnlyList<RelationshipDescriptor> Relationships
    {
        get
        {
            foreach (var relationship in _relationships)
            {
                if (relationship.ForeignKeyProperty == null && relationship.JunctionType == null)
                {
                    throw new InvalidOperationException(
                        $"{GetType().Name}: the relationship '{relationship.NavigationProperty}' has no key. "
                        + "Call .Via(child => child.ParentGuid) or .Through<TJunction>(parentFk, childFk) on it.");
                }
            }
            return _relationships.AsReadOnly();
        }
    }

    /// <summary>
    /// SH-M069 (TASK-157): one descriptor per navigation. Registering a navigation twice made Flatten keep the second and
    /// Expand emit every Insert and Delete for it twice.
    /// </summary>
    private void RefuseDuplicate(string propertyName)
    {
        foreach (var relationship in _relationships)
        {
            if (relationship.NavigationProperty == propertyName)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}: the navigation '{propertyName}' is already registered; register each navigation once.");
            }
        }
    }

    /// <summary>
    /// Defines a one-to-many relationship (collection navigation property).
    /// </summary>
    protected RelationshipBuilder<T, TChild> HasMany<TChild>(Expression<Func<T, IEnumerable<TChild>>> navigation)
    {
        var propertyName = ExpressionHelper.GetPropertyName(navigation);
        RefuseDuplicate(propertyName);
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
        RefuseDuplicate(propertyName);
        var descriptor = new RelationshipDescriptor(typeof(T), typeof(TChild), propertyName, RelationshipType.OneToOne);
        _relationships.Add(descriptor);
        return new RelationshipBuilder<T, TChild>(descriptor);
    }
}
