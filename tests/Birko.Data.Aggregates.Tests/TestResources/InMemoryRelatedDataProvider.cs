using Birko.Data.Aggregates.Core;
using Birko.Data.Aggregates.Mapping;
using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Aggregates.Tests.TestResources;

/// <summary>
/// In-memory data provider for tests. Stores related entities indexed by parent Guid + relationship.
/// </summary>
public class InMemoryRelatedDataProvider : IRelatedDataProvider, IAsyncRelatedDataProvider
{
    private readonly Dictionary<(Guid parentGuid, string navProp), IEnumerable<AbstractModel>> _directRelated = [];
    private readonly Dictionary<(Guid parentGuid, string navProp), IEnumerable<AbstractModel>> _junctionRelated = [];

    public void SetDirectRelated(Guid parentGuid, string navigationProperty, IEnumerable<AbstractModel> entities)
    {
        _directRelated[(parentGuid, navigationProperty)] = entities;
    }

    public void SetJunctionRelated(Guid parentGuid, string navigationProperty, IEnumerable<AbstractModel> entities)
    {
        _junctionRelated[(parentGuid, navigationProperty)] = entities;
    }

    public IEnumerable<AbstractModel> GetRelated(Guid parentGuid, RelationshipDescriptor relationship)
    {
        return _directRelated.TryGetValue((parentGuid, relationship.NavigationProperty), out var result)
            ? result
            : [];
    }

    public IEnumerable<AbstractModel> GetRelatedViaJunction(Guid parentGuid, RelationshipDescriptor relationship)
    {
        return _junctionRelated.TryGetValue((parentGuid, relationship.NavigationProperty), out var result)
            ? result
            : [];
    }

    public Task<IEnumerable<AbstractModel>> GetRelatedAsync(Guid parentGuid, RelationshipDescriptor relationship, CancellationToken ct = default)
    {
        return Task.FromResult(GetRelated(parentGuid, relationship));
    }

    public Task<IEnumerable<AbstractModel>> GetRelatedViaJunctionAsync(Guid parentGuid, RelationshipDescriptor relationship, CancellationToken ct = default)
    {
        return Task.FromResult(GetRelatedViaJunction(parentGuid, relationship));
    }
}
