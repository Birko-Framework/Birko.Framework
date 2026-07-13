using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Tagging;

namespace Birko.Data.Tagging.Tests;

/// <summary>
/// In-memory <see cref="TagServiceBase"/> for exercising the shared tag logic (CR-H107) without a
/// real store. Each abstract data-access hook is backed by a simple list/dictionary.
/// </summary>
public sealed class InMemoryTagService : TagServiceBase
{
    private readonly List<Tag> _tags = new();
    private readonly List<EntityTag> _links = new();
    private readonly Guid _tenant = Guid.NewGuid();

    public int CreateTagCalls { get; private set; }

    /// <summary>Counts calls to the single-entity link query (CR-M172 N+1 regression).</summary>
    public int GetEntityTagLinksCalls { get; private set; }

    protected override Task<Tag> CreateTagInternalAsync(Tag tag, CancellationToken ct)
    {
        CreateTagCalls++;
        tag.Guid ??= Guid.NewGuid();
        _tags.Add(tag);
        return Task.FromResult(tag);
    }

    protected override Task<Tag?> GetTagByIdAsync(Guid tagId, CancellationToken ct)
        => Task.FromResult(_tags.FirstOrDefault(t => t.Guid == tagId));

    protected override Task<Tag?> FindTagByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(_tags.FirstOrDefault(t => t.Name == name));

    protected override Task<IReadOnlyList<Tag>> ListAllTagsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Tag>>(_tags.ToList());

    protected override Task<IReadOnlyList<Tag>> SearchTagsByNameAsync(string query, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Tag>>(
            _tags.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList());

    protected override Task UpdateTagInternalAsync(Tag tag, CancellationToken ct) => Task.CompletedTask;

    protected override Task DeleteTagInternalAsync(Tag tag, CancellationToken ct)
    {
        _tags.Remove(tag);
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<EntityTag>> GetEntityTagLinksAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        GetEntityTagLinksCalls++;
        return Task.FromResult<IReadOnlyList<EntityTag>>(
            _links.Where(l => l.EntityType == entityType && l.EntityId == entityId).ToList());
    }

    protected override Task CreateEntityTagAsync(EntityTag link, CancellationToken ct)
    {
        link.Guid ??= Guid.NewGuid();
        _links.Add(link);
        return Task.CompletedTask;
    }

    protected override Task DeleteEntityTagAsync(EntityTag link, CancellationToken ct)
    {
        _links.Remove(link);
        return Task.CompletedTask;
    }

    protected override Task DeleteAllEntityTagsForTagAsync(Guid tagId, CancellationToken ct)
    {
        _links.RemoveAll(l => l.TagId == tagId);
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<EntityTag>> GetEntityTagLinksBatchAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EntityTag>>(
            _links.Where(l => l.EntityType == entityType && entityIds.Contains(l.EntityId)).ToList());

    protected override Guid GetCurrentTenantId() => _tenant;

    // Test helpers
    public int LinkCount => _links.Count;
    public int TagCount => _tags.Count;
}
