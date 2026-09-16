using Birko.Data.Aggregates.Core;
using Birko.Data.Models;
using Birko.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Default implementation of <see cref="IAggregateMapper{T}"/>.
/// Resolves related data via <see cref="IRelatedDataProvider"/> and composes/decomposes aggregates.
/// </summary>
public class AggregateMapper<T> : IAggregateMapper<T> where T : AbstractModel
{
    private readonly IAggregateDefinition _definition;

    public AggregateMapper(IAggregateDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));

        if (_definition.RootType != typeof(T))
            throw new ArgumentException(
                $"Definition root type '{_definition.RootType.Name}' does not match mapper type '{typeof(T).Name}'.",
                nameof(definition));
    }

    public FlattenResult<T> Flatten(T root, IRelatedDataProvider dataProvider)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (dataProvider == null) throw new ArgumentNullException(nameof(dataProvider));

        var result = new FlattenResult<T>(root);

        foreach (var relationship in _definition.Relationships)
        {
            var related = relationship.Type == RelationshipType.ManyToMany
                ? dataProvider.GetRelatedViaJunction(result.RootGuid, relationship)
                : dataProvider.GetRelated(result.RootGuid, relationship);

            if (relationship.Type == RelationshipType.OneToOne)
            {
                result.NestedSingles[relationship.NavigationProperty] = related.FirstOrDefault();
            }
            else
            {
                result.NestedCollections[relationship.NavigationProperty] = related;
            }
        }

        return result;
    }

    public async Task<FlattenResult<T>> FlattenAsync(T root, IAsyncRelatedDataProvider dataProvider, CancellationToken ct = default)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (dataProvider == null) throw new ArgumentNullException(nameof(dataProvider));

        var result = new FlattenResult<T>(root);

        foreach (var relationship in _definition.Relationships)
        {
            ct.ThrowIfCancellationRequested();

            var related = relationship.Type == RelationshipType.ManyToMany
                ? await dataProvider.GetRelatedViaJunctionAsync(result.RootGuid, relationship, ct)
                : await dataProvider.GetRelatedAsync(result.RootGuid, relationship, ct);

            if (relationship.Type == RelationshipType.OneToOne)
            {
                result.NestedSingles[relationship.NavigationProperty] = related.FirstOrDefault();
            }
            else
            {
                result.NestedCollections[relationship.NavigationProperty] = related;
            }
        }

        return result;
    }

    public IEnumerable<FlattenResult<T>> FlattenMany(IEnumerable<T> roots, IRelatedDataProvider dataProvider)
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (dataProvider == null) throw new ArgumentNullException(nameof(dataProvider));

        return roots.Select(root => Flatten(root, dataProvider));
    }

    public async Task<IEnumerable<FlattenResult<T>>> FlattenManyAsync(
        IEnumerable<T> roots, IAsyncRelatedDataProvider dataProvider, CancellationToken ct = default)
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (dataProvider == null) throw new ArgumentNullException(nameof(dataProvider));

        var results = new List<FlattenResult<T>>();
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await FlattenAsync(root, dataProvider, ct));
        }
        return results;
    }

    /// <summary>
    /// Produces the sync operations to reconcile the desired aggregate against current state.
    /// <para><b>Emits Insert/Delete only (CR-L100):</b> a OneToOne change is modeled as Delete+Insert and
    /// collection diffing acts on Added/Removed; an in-place field change to a child present in both
    /// current and desired produces <b>no</b> operation. <c>SyncOperationType.Update</c> is not emitted by
    /// this mapper — callers that need field-level child updates must diff payloads themselves.</para>
    /// </summary>
    public IEnumerable<SyncOperation> Expand(FlattenResult<T> aggregate, IRelatedDataProvider currentStateProvider)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (currentStateProvider == null) throw new ArgumentNullException(nameof(currentStateProvider));

        var operations = new List<SyncOperation>();

        foreach (var relationship in _definition.Relationships)
        {
            var currentRelated = relationship.Type == RelationshipType.ManyToMany
                ? currentStateProvider.GetRelatedViaJunction(aggregate.RootGuid, relationship)
                : currentStateProvider.GetRelated(aggregate.RootGuid, relationship);

            if (relationship.Type == RelationshipType.OneToOne)
            {
                ExpandSingle(aggregate, relationship, currentRelated.FirstOrDefault(), operations);
            }
            else
            {
                ExpandCollection(aggregate, relationship, currentRelated, operations);
            }
        }

        return operations;
    }

    public async Task<IEnumerable<SyncOperation>> ExpandAsync(
        FlattenResult<T> aggregate, IAsyncRelatedDataProvider currentStateProvider, CancellationToken ct = default)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (currentStateProvider == null) throw new ArgumentNullException(nameof(currentStateProvider));

        var operations = new List<SyncOperation>();

        foreach (var relationship in _definition.Relationships)
        {
            ct.ThrowIfCancellationRequested();

            var currentRelated = relationship.Type == RelationshipType.ManyToMany
                ? await currentStateProvider.GetRelatedViaJunctionAsync(aggregate.RootGuid, relationship, ct)
                : await currentStateProvider.GetRelatedAsync(aggregate.RootGuid, relationship, ct);

            if (relationship.Type == RelationshipType.OneToOne)
            {
                ExpandSingle(aggregate, relationship, currentRelated.FirstOrDefault(), operations);
            }
            else
            {
                ExpandCollection(aggregate, relationship, currentRelated, operations);
            }
        }

        return operations;
    }

    private static void ExpandSingle(
        FlattenResult<T> aggregate,
        RelationshipDescriptor relationship,
        AbstractModel? currentEntity,
        List<SyncOperation> operations)
    {
        aggregate.NestedSingles.TryGetValue(relationship.NavigationProperty, out var desiredEntity);

        if (currentEntity == null && desiredEntity != null)
        {
            operations.Add(new SyncOperation(SyncOperationType.Insert, relationship.ChildType, desiredEntity, relationship.NavigationProperty));
        }
        else if (currentEntity != null && desiredEntity == null)
        {
            operations.Add(new SyncOperation(SyncOperationType.Delete, relationship.ChildType, currentEntity, relationship.NavigationProperty));
        }
        else if (currentEntity != null && desiredEntity != null && currentEntity.Guid != desiredEntity.Guid)
        {
            operations.Add(new SyncOperation(SyncOperationType.Delete, relationship.ChildType, currentEntity, relationship.NavigationProperty));
            operations.Add(new SyncOperation(SyncOperationType.Insert, relationship.ChildType, desiredEntity, relationship.NavigationProperty));
        }
    }

    private static void ExpandCollection(
        FlattenResult<T> aggregate,
        RelationshipDescriptor relationship,
        IEnumerable<AbstractModel> currentEntities,
        List<SyncOperation> operations)
    {
        IEnumerable<AbstractModel> desiredEntities = [];
        if (aggregate.NestedCollections.TryGetValue(relationship.NavigationProperty, out var collection) && collection != null)
        {
            desiredEntities = collection;
        }

        var isManyToMany = relationship.Type == RelationshipType.ManyToMany;

        // New children have no Guid yet; DiffByKey excludes null-key items, so they'd never appear
        // in diff.Added and would be silently dropped. Treat them as unconditional inserts and diff
        // only the already-keyed entities (CR-H041).
        var desiredList = desiredEntities.ToList();
        foreach (var newChild in desiredList.Where(e => e.Guid == null))
        {
            if (isManyToMany)
            {
                // SH-H014: a many-to-many association IS the junction row, and a junction row cannot be
                // formed without the child's key. Refusing names the remedy rather than emitting a child
                // insert with no association, which is the silent half-write this finding is about.
                throw new InvalidOperationException(
                    $"Cannot expand the many-to-many relationship '{relationship.NavigationProperty}' on "
                        + $"'{typeof(T).Name}': a desired '{relationship.ChildType.Name}' has no Guid, so no "
                        + $"junction row can reference it. Assign the child's Guid (or persist the child "
                        + "first) before expanding the aggregate.");
            }

            operations.Add(new SyncOperation(SyncOperationType.Insert, relationship.ChildType, newChild, relationship.NavigationProperty));
        }

        var diff = EnumerableHelper.DiffByKey(currentEntities, desiredList.Where(e => e.Guid != null), e => e.Guid);

        // SH-H014: a many-to-many add/remove changes the ASSOCIATION, not the child. This branch used to
        // run the OneToMany code for ManyToMany too, tagging each operation with relationship.ChildType and
        // the child entity itself — so removing one category from a product emitted Delete of the shared
        // Category row, and adding an existing one emitted Insert of a Category that already exists. A
        // caller applying those operations destroys or duplicates data every other aggregate shares.
        // JunctionType / JunctionParentFk / JunctionChildFk were set by RelationshipBuilder.Through and
        // read nowhere in this mapper, while IAggregateMapper.Expand's own doc promises junction-table
        // operations. The junction row's identity is the (parentFk, childFk) pair, so both the insert and
        // the delete carry a junction instance with exactly those two properties set and a null Guid —
        // a caller deletes by matching them, and lets the store assign the key on insert.
        foreach (var added in diff.Added)
        {
            operations.Add(isManyToMany
                ? JunctionOperation(SyncOperationType.Insert, aggregate.RootGuid, added, relationship)
                : new SyncOperation(SyncOperationType.Insert, relationship.ChildType, added, relationship.NavigationProperty));
        }

        foreach (var removed in diff.Removed)
        {
            operations.Add(isManyToMany
                ? JunctionOperation(SyncOperationType.Delete, aggregate.RootGuid, removed, relationship)
                : new SyncOperation(SyncOperationType.Delete, relationship.ChildType, removed, relationship.NavigationProperty));
        }
    }

    /// <summary>
    /// Builds a <see cref="SyncOperation"/> over a freshly materialised junction row whose parent and child
    /// foreign keys are set from the relationship descriptor (SH-H014).
    /// </summary>
    /// <remarks>
    /// The junction type is guaranteed to be an <see cref="AbstractModel"/> by
    /// <see cref="Core.RelationshipBuilder{TParent, TChild}.Through{TJunction}"/>'s constraint, and the two
    /// FK names are guaranteed non-null by the same method — <see cref="RelationshipDescriptor"/>'s setters
    /// are internal and that builder is their only writer. The guards below are therefore defensive rather
    /// than witnessed: they exist so a descriptor assembled some other way fails by name instead of with a
    /// NullReferenceException three frames away.
    /// </remarks>
    private static SyncOperation JunctionOperation(
        SyncOperationType type,
        Guid rootGuid,
        AbstractModel child,
        RelationshipDescriptor relationship)
    {
        if (relationship.JunctionType == null
            || relationship.JunctionParentFk == null
            || relationship.JunctionChildFk == null)
        {
            throw new InvalidOperationException(
                $"Many-to-many relationship '{relationship.NavigationProperty}' on '{typeof(T).Name}' has no "
                    + "junction configured. Declare it with .Through<TJunction>(parentFk, childFk).");
        }

        if (Activator.CreateInstance(relationship.JunctionType) is not AbstractModel junction)
        {
            throw new InvalidOperationException(
                $"Junction type '{relationship.JunctionType.Name}' for relationship "
                    + $"'{relationship.NavigationProperty}' must derive from AbstractModel.");
        }

        SetJunctionKey(junction, relationship.JunctionParentFk, rootGuid, relationship);
        SetJunctionKey(junction, relationship.JunctionChildFk, child.Guid!.Value, relationship);

        return new SyncOperation(type, relationship.JunctionType, junction, relationship.NavigationProperty);
    }

    private static void SetJunctionKey(
        AbstractModel junction, string propertyName, Guid value, RelationshipDescriptor relationship)
    {
        var property = junction.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite)
        {
            throw new InvalidOperationException(
                $"Junction type '{junction.GetType().Name}' for relationship "
                    + $"'{relationship.NavigationProperty}' has no writable property '{propertyName}'.");
        }

        // Defensive, not witnessed: reflection would throw here anyway, but with a type-conversion message
        // that names neither the relationship nor the property, so an author would have to guess which of
        // the two foreign keys is wrong.
        if (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?))
        {
            throw new InvalidOperationException(
                $"Junction type '{junction.GetType().Name}' property '{propertyName}' for relationship "
                    + $"'{relationship.NavigationProperty}' is '{property.PropertyType.Name}'; a junction "
                    + "foreign key must be Guid or Guid?.");
        }

        property.SetValue(junction, property.PropertyType == typeof(Guid) ? value : (Guid?)value);
    }
}
