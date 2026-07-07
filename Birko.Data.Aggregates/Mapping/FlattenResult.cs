using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Result of flattening an aggregate — the root entity with all related data resolved
/// into nested collections and single-entity properties.
/// </summary>
public class FlattenResult<T> where T : AbstractModel
{
    /// <summary>
    /// The root entity's Guid.
    /// </summary>
    public Guid RootGuid { get; }

    /// <summary>
    /// The root entity.
    /// </summary>
    public T Root { get; }

    /// <summary>
    /// Nested collections keyed by navigation property name.
    /// Values are IEnumerable of the related child entities.
    /// </summary>
    public Dictionary<string, IEnumerable<AbstractModel>> NestedCollections { get; } = [];

    /// <summary>
    /// Nested single entities keyed by navigation property name.
    /// Values are the single related entity (or null if not found).
    /// </summary>
    public Dictionary<string, AbstractModel?> NestedSingles { get; } = [];

    public FlattenResult(T root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        RootGuid = root.Guid ?? throw new ArgumentException("Root entity must have a non-null Guid.", nameof(root));
    }

    /// <summary>
    /// Gets a nested collection cast to the expected child type.
    /// </summary>
    public IEnumerable<TChild>? GetCollection<TChild>(string navigationProperty) where TChild : AbstractModel
    {
        // OfType, not a reference cast: a provider that materializes to List<AbstractModel> is not
        // reference-assignable to IEnumerable<TChild>, so `as` returned null silently (CR-H040).
        if (NestedCollections.TryGetValue(navigationProperty, out var collection))
            return collection?.OfType<TChild>();
        return null;
    }

    /// <summary>
    /// Gets a nested single entity cast to the expected child type.
    /// </summary>
    public TChild? GetSingle<TChild>(string navigationProperty) where TChild : AbstractModel
    {
        if (NestedSingles.TryGetValue(navigationProperty, out var single))
            return single as TChild;
        return null;
    }
}
