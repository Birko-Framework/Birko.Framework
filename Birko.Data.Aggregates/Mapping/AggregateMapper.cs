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
        if (aggregate.NestedCollections.TryGetValue(relationship.NavigationProperty, out var collection))
        {
            desiredEntities = collection;
        }

        var diff = EnumerableHelper.DiffByKey(currentEntities, desiredEntities, e => e.Guid);

        foreach (var added in diff.Added)
        {
            operations.Add(new SyncOperation(SyncOperationType.Insert, relationship.ChildType, added, relationship.NavigationProperty));
        }

        foreach (var removed in diff.Removed)
        {
            operations.Add(new SyncOperation(SyncOperationType.Delete, relationship.ChildType, removed, relationship.NavigationProperty));
        }
    }
}
