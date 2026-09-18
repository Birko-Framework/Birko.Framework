using Birko.Data.Aggregates.Core;
using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Provides related entity data for aggregate flattening.
/// Implement this interface using your specific stores (SQL, MongoDB, etc.).
/// </summary>
public interface IRelatedDataProvider
{
    /// <summary>
    /// Gets child entities related to a parent via direct foreign key (OneToOne/OneToMany).
    /// </summary>
    /// <param name="parentGuid">The parent entity's Guid.</param>
    /// <param name="relationship">The relationship descriptor.</param>
    /// <returns>Related child entities.</returns>
    IEnumerable<AbstractModel> GetRelated(Guid parentGuid, RelationshipDescriptor relationship);

    /// <summary>
    /// Gets child entities related to a parent via a junction table (ManyToMany).
    /// </summary>
    /// <param name="parentGuid">The parent entity's Guid.</param>
    /// <param name="relationship">The relationship descriptor (must have JunctionType set).</param>
    /// <returns>Related child entities (resolved through the junction table).</returns>
    IEnumerable<AbstractModel> GetRelatedViaJunction(Guid parentGuid, RelationshipDescriptor relationship);
}

/// <summary>
/// Async version of <see cref="IRelatedDataProvider"/>.
/// </summary>
public interface IAsyncRelatedDataProvider
{
    /// <summary>
    /// Gets child entities related to a parent via direct foreign key (OneToOne/OneToMany).
    /// </summary>
    Task<IEnumerable<AbstractModel>> GetRelatedAsync(Guid parentGuid, RelationshipDescriptor relationship, CancellationToken ct = default);

    /// <summary>
    /// Gets child entities related to a parent via a junction table (ManyToMany).
    /// </summary>
    Task<IEnumerable<AbstractModel>> GetRelatedViaJunctionAsync(Guid parentGuid, RelationshipDescriptor relationship, CancellationToken ct = default);
}
