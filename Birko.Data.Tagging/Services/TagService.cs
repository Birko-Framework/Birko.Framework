namespace Birko.Data.Tagging;

/// <summary>
/// Base tag service with shared logic. Platform implementations provide the data access.
/// This class is abstract — Symbio provides the concrete implementation via its IRepository.
/// </summary>
public abstract class TagServiceBase : ITagService
{
    // ── Abstract data access (implemented by platform) ───────────────────
    //
    // TENANT-SCOPING CONTRACT: this base class stamps TenantGuid = GetCurrentTenantId() on every
    // insert, and the read/delete hooks below receive NO tenant parameter — so implementations SHOULD
    // still scope every one of them to the ambient tenant, for correctness and for the query plan.
    //
    // SH-H019: the base class NO LONGER DEPENDS ON THEM DOING SO. It used to say exactly that
    // ("the base class has no guard"), and everything followed from that sentence being true: one
    // implementation omitting one filter in one hook exposed cross-tenant reads, cross-tenant writes
    // and a cascade delete, across every implementation of this base. The base now re-checks what the
    // hooks return, on two rules:
    //
    //   * loading a tag BY IDENTITY asserts ownership and THROWS CrossTenantTagAccessException — the
    //     caller named one record, so a wrong answer must not be silently downgraded to "not found",
    //     and Update/Delete must not proceed on it;
    //   * enumerating a COLLECTION filters foreign records out — one leaked row must not blank an
    //     entire list, which is what throwing would do to a tag picker.
    //
    // Defence in depth, not a replacement for the filter: a hook that skips it now fails loudly during
    // development instead of leaking in production.

    protected abstract Task<Tag> CreateTagInternalAsync(Tag tag, CancellationToken ct);
    protected abstract Task<Tag?> GetTagByIdAsync(Guid tagId, CancellationToken ct);
    protected abstract Task<Tag?> FindTagByNameAsync(string name, CancellationToken ct);
    protected abstract Task<IReadOnlyList<Tag>> ListAllTagsAsync(CancellationToken ct);
    protected abstract Task<IReadOnlyList<Tag>> SearchTagsByNameAsync(string query, int limit, CancellationToken ct);
    protected abstract Task UpdateTagInternalAsync(Tag tag, CancellationToken ct);
    protected abstract Task DeleteTagInternalAsync(Tag tag, CancellationToken ct);

    protected abstract Task<IReadOnlyList<EntityTag>> GetEntityTagLinksAsync(string entityType, Guid entityId, CancellationToken ct);
    protected abstract Task CreateEntityTagAsync(EntityTag link, CancellationToken ct);
    protected abstract Task DeleteEntityTagAsync(EntityTag link, CancellationToken ct);
    protected abstract Task DeleteAllEntityTagsForTagAsync(Guid tagId, CancellationToken ct);
    protected abstract Task<IReadOnlyList<EntityTag>> GetEntityTagLinksBatchAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken ct);

    protected abstract Guid GetCurrentTenantId();

    // ── Tenant guards (SH-H019) ──────────────────────────────────────────

    /// <summary>
    /// Asserts that a tag loaded by identity belongs to the ambient tenant, and returns it.
    /// </summary>
    /// <remarks>
    /// The single choke point: <see cref="GetTagAsync"/>, <see cref="UpdateTagAsync"/>,
    /// <see cref="DeleteTagAsync"/> and <see cref="GetEntityTagsAsync"/> all reach their target through
    /// here, so one assertion covers the read, the write and the cascade delete for every implementation
    /// of this base at once — without touching any of them.
    /// </remarks>
    private async Task<Tag?> LoadOwnedTagAsync(Guid tagId, CancellationToken ct)
    {
        var tag = await GetTagByIdAsync(tagId, ct);
        if (tag is null) return null;

        var current = GetCurrentTenantId();
        if (tag.TenantGuid != current)
        {
            throw new CrossTenantTagAccessException(tagId, tag.TenantGuid, current);
        }
        return tag;
    }

    /// <summary>
    /// The by-name twin of <see cref="LoadOwnedTagAsync"/>.
    /// </summary>
    /// <remarks>
    /// Guarded for the same reason and not merely filtered: <see cref="CreateTagAsync"/> returns this
    /// lookup's hit *instead of inserting*, so an unscoped hook would hand the caller another tenant's
    /// tag as though they had just created it — and <see cref="AttachTagByNameAsync"/> would then link a
    /// foreign tag to a local entity.
    /// </remarks>
    private async Task<Tag?> FindOwnedTagByNameAsync(string name, CancellationToken ct)
    {
        var tag = await FindTagByNameAsync(name, ct);
        if (tag is null) return null;

        var current = GetCurrentTenantId();
        if (tag.TenantGuid != current)
        {
            throw new CrossTenantTagAccessException(tag.Guid ?? Guid.Empty, tag.TenantGuid, current);
        }
        return tag;
    }

    /// <summary>
    /// Drops records belonging to another tenant from a collection result.
    /// </summary>
    /// <remarks>
    /// <para><b>Guid.Empty is a tenant value, not a wildcard</b> — deliberately, and pinned by a test. A
    /// tag stamped <c>Guid.Empty</c> is visible only while <see cref="GetCurrentTenantId"/> also returns
    /// <c>Guid.Empty</c>. Treating it as "matches everything" is how a wrapper elsewhere in this family
    /// once returned every tenant's rows to an unconfigured scope; the same accident is not repeated here.
    /// </para>
    /// <para>Filtering rather than throwing, unlike the by-identity path: these results are lists, and one
    /// foreign row must not blank a whole tag picker. The trade is that a broken hook is quieter here —
    /// which is why the by-identity paths, where a single wrong answer is the whole answer, throw.</para>
    /// </remarks>
    private IReadOnlyList<T> OwnedOnly<T>(IReadOnlyList<T> records, Func<T, Guid> tenantOf)
    {
        var current = GetCurrentTenantId();
        var owned = records.Where(r => tenantOf(r) == current).ToList();
        return owned.Count == records.Count ? records : owned;
    }

    // ── Tag CRUD ──────────────────────────────────────────────────────────

    public async Task<TagDto> CreateTagAsync(string name, string? color = null, string? group = null, CancellationToken ct = default)
    {
        var existing = await FindOwnedTagByNameAsync(name.Trim(), ct);
        if (existing is not null) return ToDto(existing);

        var tag = new Tag
        {
            TenantGuid = GetCurrentTenantId(),
            Name = name.Trim(),
            Color = color,
            TagGroup = group,
        };
        var created = await CreateTagInternalAsync(tag, ct);
        return ToDto(created);
    }

    public async Task<TagDto?> GetTagAsync(Guid tagId, CancellationToken ct = default)
    {
        var tag = await LoadOwnedTagAsync(tagId, ct);
        return tag is null ? null : ToDto(tag);
    }

    public async Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken ct = default)
    {
        var tags = OwnedOnly(await ListAllTagsAsync(ct), t => t.TenantGuid);
        return tags.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TagDto>> SearchTagsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var tags = OwnedOnly(await SearchTagsByNameAsync(query.Trim(), limit, ct), t => t.TenantGuid);
        return tags.Select(ToDto).ToList();
    }

    public async Task UpdateTagAsync(Guid tagId, string? name = null, string? color = null, string? group = null, CancellationToken ct = default)
    {
        var tag = await LoadOwnedTagAsync(tagId, ct)
                  ?? throw new InvalidOperationException($"Tag {tagId} not found.");

        if (name is not null) tag.Name = name.Trim();
        if (color is not null) tag.Color = string.IsNullOrWhiteSpace(color) ? null : color;
        if (group is not null) tag.TagGroup = string.IsNullOrWhiteSpace(group) ? null : group;

        await UpdateTagInternalAsync(tag, ct);
    }

    public async Task DeleteTagAsync(Guid tagId, CancellationToken ct = default)
    {
        // The guard runs BEFORE DeleteAllEntityTagsForTagAsync, so a foreign tag never reaches the
        // cascade — the delete was the worst of the three paths through this choke point.
        var tag = await LoadOwnedTagAsync(tagId, ct);
        if (tag is null) return;

        await DeleteAllEntityTagsForTagAsync(tagId, ct);
        await DeleteTagInternalAsync(tag, ct);
    }

    // ── Entity tagging ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<TagDto>> GetEntityTagsAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var links = OwnedOnly(await GetEntityTagLinksAsync(entityType, entityId, ct), l => l.TenantGuid);
        var tags = new List<TagDto>();
        foreach (var link in links)
        {
            // By-identity, so it throws: a link that survived the tenant filter yet points at another
            // tenant's tag is corrupt data, not a row to skip quietly.
            var tag = await LoadOwnedTagAsync(link.TagId, ct);
            if (tag is not null) tags.Add(ToDto(tag));
        }
        return tags;
    }

    public async Task AttachTagAsync(string entityType, Guid entityId, Guid tagId, CancellationToken ct = default)
    {
        var links = OwnedOnly(await GetEntityTagLinksAsync(entityType, entityId, ct), l => l.TenantGuid);
        if (links.Any(l => l.TagId == tagId)) return; // already attached

        await CreateEntityTagAsync(new EntityTag
        {
            TenantGuid = GetCurrentTenantId(),
            TagId = tagId,
            EntityId = entityId,
            EntityType = entityType,
        }, ct);
    }

    public async Task DetachTagAsync(string entityType, Guid entityId, Guid tagId, CancellationToken ct = default)
    {
        var links = OwnedOnly(await GetEntityTagLinksAsync(entityType, entityId, ct), l => l.TenantGuid);
        var link = links.FirstOrDefault(l => l.TagId == tagId);
        if (link is not null) await DeleteEntityTagAsync(link, ct);
    }

    public async Task SetEntityTagsAsync(string entityType, Guid entityId, IReadOnlyList<Guid> tagIds, CancellationToken ct = default)
    {
        var links = OwnedOnly(await GetEntityTagLinksAsync(entityType, entityId, ct), l => l.TenantGuid);
        var currentTagIds = links.Select(l => l.TagId).ToHashSet();
        var desiredTagIds = tagIds.ToHashSet();

        // Remove extra
        foreach (var link in links.Where(l => !desiredTagIds.Contains(l.TagId)))
            await DeleteEntityTagAsync(link, ct);

        // Add missing. CR-M172: create the link directly rather than routing through
        // AttachTagAsync — the `!currentTagIds.Contains(id)` filter already proves the link is
        // absent, so AttachTagAsync's own GetEntityTagLinksAsync re-query is redundant I/O (one
        // extra link query per added tag, i.e. N+1).
        var tenantId = GetCurrentTenantId();
        foreach (var tagId in desiredTagIds.Where(id => !currentTagIds.Contains(id)))
            await CreateEntityTagAsync(new EntityTag
            {
                TenantGuid = tenantId,
                TagId = tagId,
                EntityId = entityId,
                EntityType = entityType,
            }, ct);
    }

    public async Task<TagDto> AttachTagByNameAsync(string entityType, Guid entityId, string tagName, string? color = null, CancellationToken ct = default)
    {
        var tag = await FindOwnedTagByNameAsync(tagName.Trim(), ct);
        if (tag is null)
        {
            // Deliberately routes through CreateTagAsync, which re-runs FindTagByNameAsync before
            // inserting. The repeated lookup narrows the TOCTOU window: if a concurrent request
            // created the same tag between our miss above and this call, CreateTagAsync returns the
            // existing tag instead of inserting a duplicate (this layer has no unique-name constraint
            // to fall back on). Don't "optimize" this to CreateTagInternalAsync.
            var dto = await CreateTagAsync(tagName, color, ct: ct);
            await AttachTagAsync(entityType, entityId, dto.Id, ct);
            return dto;
        }

        await AttachTagAsync(entityType, entityId, tag.Guid!.Value, ct);
        return ToDto(tag);
    }

    // ── Batch loading ────────────────────────────────────────────────────

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<TagDto>>> GetEntityTagsBatchAsync(
        string entityType, IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        if (entityIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<TagDto>>();

        var links = OwnedOnly(await GetEntityTagLinksBatchAsync(entityType, entityIds, ct), l => l.TenantGuid);

        // Collect unique tag IDs and load all at once
        var tagIds = links.Select(l => l.TagId).Distinct().ToList();
        var tagMap = new Dictionary<Guid, Tag>();
        foreach (var tagId in tagIds)
        {
            // By-identity, so guarded and throwing — same rule as the single-entity GetEntityTagsAsync.
            // The ids come from links already filtered to this tenant, so a foreign tag here means the
            // link table and the tag table disagree about ownership; that is corruption worth surfacing,
            // not a row to drop from a batch and leave the caller none the wiser.
            var tag = await LoadOwnedTagAsync(tagId, ct);
            if (tag is not null) tagMap[tagId] = tag;
        }

        // Group by entity
        var result = new Dictionary<Guid, IReadOnlyList<TagDto>>();
        var grouped = links.GroupBy(l => l.EntityId);
        foreach (var group in grouped)
        {
            result[group.Key] = group
                .Where(l => tagMap.ContainsKey(l.TagId))
                .Select(l => ToDto(tagMap[l.TagId]))
                .ToList();
        }

        // Fill in empty lists for entities with no tags
        foreach (var id in entityIds)
        {
            if (!result.ContainsKey(id))
                result[id] = [];
        }

        return result;
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static TagDto ToDto(Tag t) => new(t.Guid!.Value, t.Name, t.Color, t.TagGroup);
}
