namespace Birko.Data.Tagging;

/// <summary>
/// Tag DTO returned by the tag service.
/// </summary>
public sealed record TagDto(Guid Id, string Name, string? Color, string? TagGroup);

/// <summary>
/// Service for managing tags and attaching/detaching them to/from entities.
/// All operations are tenant-scoped. Note the division of labor: <see cref="TagServiceBase"/> stamps
/// <c>TenantGuid</c> on every insert, but tenant isolation on READS is not enforceable at this layer —
/// the abstract read hooks carry no tenant parameter, so each platform implementation MUST filter every
/// read (by-id lookups included) to the ambient tenant. An implementation that forgets to filter leaks
/// other tenants' tags. See the "Abstract data access" region in <see cref="TagServiceBase"/>.
/// </summary>
public interface ITagService
{
    // ── Tag CRUD ──────────────────────────────────────────────────────────

    Task<TagDto> CreateTagAsync(string name, string? color = null, string? group = null, CancellationToken ct = default);
    Task<TagDto?> GetTagAsync(Guid tagId, CancellationToken ct = default);
    Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TagDto>> SearchTagsAsync(string query, int limit = 20, CancellationToken ct = default);
    Task UpdateTagAsync(Guid tagId, string? name = null, string? color = null, string? group = null, CancellationToken ct = default);
    Task DeleteTagAsync(Guid tagId, CancellationToken ct = default);

    // ── Entity tagging ───────────────────────────────────────────────────

    Task<IReadOnlyList<TagDto>> GetEntityTagsAsync(string entityType, Guid entityId, CancellationToken ct = default);
    Task AttachTagAsync(string entityType, Guid entityId, Guid tagId, CancellationToken ct = default);
    Task DetachTagAsync(string entityType, Guid entityId, Guid tagId, CancellationToken ct = default);

    /// <summary>
    /// Sync entity tags — sets the entity's tags to exactly the given set.
    /// Adds missing, removes extra.
    /// </summary>
    Task SetEntityTagsAsync(string entityType, Guid entityId, IReadOnlyList<Guid> tagIds, CancellationToken ct = default);

    /// <summary>
    /// Attach tag by name — creates the tag if it doesn't exist yet (for quick-tag UX).
    /// </summary>
    Task<TagDto> AttachTagByNameAsync(string entityType, Guid entityId, string tagName, string? color = null, CancellationToken ct = default);

    /// <summary>
    /// Bulk-load tags for multiple entities of the same type in one call.
    /// Returns a dictionary keyed by entityId.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<TagDto>>> GetEntityTagsBatchAsync(
        string entityType, IReadOnlyList<Guid> entityIds, CancellationToken ct = default);
}
