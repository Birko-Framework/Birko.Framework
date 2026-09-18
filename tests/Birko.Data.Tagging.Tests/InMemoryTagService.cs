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

    /// <summary>
    /// Invoked after every <see cref="FindTagByNameAsync"/> lookup (with the queried name) — lets a
    /// test inject a concurrently-created tag between AttachTagByNameAsync's miss and CreateTagAsync's
    /// re-check (the CR-L226 TOCTOU-narrowing double lookup).
    /// </summary>
    public Action<string>? AfterFindTagByName { get; set; }

    /// <summary>Inserts a tag directly, bypassing the service (simulates a concurrent writer).</summary>
    /// <summary>
    /// Seeds a tag that belongs to THIS service's tenant — the normal case, matching what
    /// <c>CreateTagAsync</c> does (it stamps <c>TenantGuid = GetCurrentTenantId()</c> on every insert).
    /// </summary>
    /// <remarks>
    /// SH-H019: the tenant defaulting was added with the base-class guard. Before it, seeds left
    /// <c>TenantGuid</c> at <c>Guid.Empty</c> while the ambient tenant was a real Guid — a state no real
    /// implementation can produce, which the guard now (correctly) rejects. Use <see cref="SeedForeignTag"/>
    /// to seed the leak deliberately.
    /// </remarks>
    public void SeedTag(Tag tag)
    {
        tag.Guid ??= Guid.NewGuid();
        if (tag.TenantGuid == Guid.Empty) tag.TenantGuid = _tenant;
        _tags.Add(tag);
    }

    /// <summary>
    /// Seeds a tag exactly as given, tenant included — the only way to simulate a data-access hook that
    /// forgot its tenant filter and handed the base class another tenant's row.
    /// </summary>
    public void SeedForeignTag(Tag tag)
    {
        tag.Guid ??= Guid.NewGuid();
        _tags.Add(tag);
    }

    /// <summary>This service's ambient tenant, so tests can assert against it.</summary>
    public Guid Tenant => _tenant;

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
    {
        var result = _tags.FirstOrDefault(t => t.Name == name);
        AfterFindTagByName?.Invoke(name);
        return Task.FromResult(result);
    }

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

    /// <summary>
    /// Counts cascade invocations. SH-H019: `DeleteTagAsync` calls this BEFORE deleting the tag, so
    /// "did the cascade run?" is the assertion that distinguishes a guard placed early enough from one
    /// that fires after another tenant's links are already gone.
    /// </summary>
    public int DeleteAllEntityTagsCalls { get; private set; }

    protected override Task DeleteAllEntityTagsForTagAsync(Guid tagId, CancellationToken ct)
    {
        DeleteAllEntityTagsCalls++;
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
