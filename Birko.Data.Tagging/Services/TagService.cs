namespace Birko.Data.Tagging;

/// <summary>
/// Base tag service with shared logic. Platform implementations provide the data access.
/// This class is abstract — Symbio provides the concrete implementation via its IRepository.
/// </summary>
public abstract class TagServiceBase : ITagService
{
    // ── Abstract data access (implemented by platform) ───────────────────

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

    // ── Tag CRUD ──────────────────────────────────────────────────────────

    public async Task<TagDto> CreateTagAsync(string name, string? color = null, string? group = null, CancellationToken ct = default)
    {
        var existing = await FindTagByNameAsync(name.Trim(), ct);
        if (existing is not null) return ToDto(existing);

        var tag = new Tag
        {
            TenantGuid = GetCurrentTenantId(),
            Name = name.Trim(),
            Color = color,
            Group = group,
        };
        var created = await CreateTagInternalAsync(tag, ct);
        return ToDto(created);
    }

    public async Task<TagDto?> GetTagAsync(Guid tagId, CancellationToken ct = default)
    {
        var tag = await GetTagByIdAsync(tagId, ct);
        return tag is null ? null : ToDto(tag);
    }

    public async Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken ct = default)
    {
        var tags = await ListAllTagsAsync(ct);
        return tags.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TagDto>> SearchTagsAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var tags = await SearchTagsByNameAsync(query.Trim(), limit, ct);
        return tags.Select(ToDto).ToList();
    }

    public async Task UpdateTagAsync(Guid tagId, string? name = null, string? color = null, string? group = null, CancellationToken ct = default)
    {
        var tag = await GetTagByIdAsync(tagId, ct)
                  ?? throw new InvalidOperationException($"Tag {tagId} not found.");

        if (name is not null) tag.Name = name.Trim();
        if (color is not null) tag.Color = color;
        if (group is not null) tag.Group = group;

        await UpdateTagInternalAsync(tag, ct);
    }

    public async Task DeleteTagAsync(Guid tagId, CancellationToken ct = default)
    {
        var tag = await GetTagByIdAsync(tagId, ct);
        if (tag is null) return;

        await DeleteAllEntityTagsForTagAsync(tagId, ct);
        await DeleteTagInternalAsync(tag, ct);
    }

    // ── Entity tagging ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<TagDto>> GetEntityTagsAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var links = await GetEntityTagLinksAsync(entityType, entityId, ct);
        var tags = new List<TagDto>();
        foreach (var link in links)
        {
            var tag = await GetTagByIdAsync(link.TagId, ct);
            if (tag is not null) tags.Add(ToDto(tag));
        }
        return tags;
    }

    public async Task AttachTagAsync(string entityType, Guid entityId, Guid tagId, CancellationToken ct = default)
    {
        var links = await GetEntityTagLinksAsync(entityType, entityId, ct);
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
        var links = await GetEntityTagLinksAsync(entityType, entityId, ct);
        var link = links.FirstOrDefault(l => l.TagId == tagId);
        if (link is not null) await DeleteEntityTagAsync(link, ct);
    }

    public async Task SetEntityTagsAsync(string entityType, Guid entityId, IReadOnlyList<Guid> tagIds, CancellationToken ct = default)
    {
        var links = await GetEntityTagLinksAsync(entityType, entityId, ct);
        var currentTagIds = links.Select(l => l.TagId).ToHashSet();
        var desiredTagIds = tagIds.ToHashSet();

        // Remove extra
        foreach (var link in links.Where(l => !desiredTagIds.Contains(l.TagId)))
            await DeleteEntityTagAsync(link, ct);

        // Add missing
        foreach (var tagId in desiredTagIds.Where(id => !currentTagIds.Contains(id)))
            await AttachTagAsync(entityType, entityId, tagId, ct);
    }

    public async Task<TagDto> AttachTagByNameAsync(string entityType, Guid entityId, string tagName, string? color = null, CancellationToken ct = default)
    {
        var tag = await FindTagByNameAsync(tagName.Trim(), ct);
        if (tag is null)
        {
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

        var links = await GetEntityTagLinksBatchAsync(entityType, entityIds, ct);

        // Collect unique tag IDs and load all at once
        var tagIds = links.Select(l => l.TagId).Distinct().ToList();
        var tagMap = new Dictionary<Guid, Tag>();
        foreach (var tagId in tagIds)
        {
            var tag = await GetTagByIdAsync(tagId, ct);
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

    private static TagDto ToDto(Tag t) => new(t.Guid!.Value, t.Name, t.Color, t.Group);
}
