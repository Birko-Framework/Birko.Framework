namespace Birko.Xaml.Core.Data;

/// <summary>
/// The thin CRUD port the base ViewModels depend on — Guid-keyed and shaped to mirror the
/// Birko.Data async store (<c>SaveAsync</c> = create-or-update, <c>NewInstance</c> = factory).
///
/// Why a port and not <c>IAsyncBulkStore&lt;T&gt;</c> directly: the Birko.Data.* stores are shared
/// <c>.projitems</c> (compiled INTO each importing assembly). Birko.Xaml.* are real assemblies. If
/// Core imported the store projitems, its types would be duplicated against a consumer's own
/// aggregator that also imports them — a hard type-conflict. So Core owns this small port, and the
/// consumer supplies a ~10-line adapter from their store (in the assembly that already has the
/// Birko.Data types). See CLAUDE.md.
/// </summary>
public interface ICrudDataSource<T>
    where T : class
{
    /// <summary>All entities (list/CRUD views load from here).</summary>
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Load one entity by id, or null.</summary>
    Task<T?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Create or update; returns the entity's id.</summary>
    Task<Guid> SaveAsync(T item, CancellationToken ct = default);

    /// <summary>Delete an entity.</summary>
    Task DeleteAsync(T item, CancellationToken ct = default);

    /// <summary>Factory for a new, unsaved entity (for the "create" flow).</summary>
    T NewInstance();
}
