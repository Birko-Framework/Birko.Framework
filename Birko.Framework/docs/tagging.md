# Tagging Guide

## Overview

Birko.Data.Tagging provides a reusable entity tagging system. Tags are tenant-scoped and can be attached to any entity type through a polymorphic junction table using string discriminators.

## Models

### ITaggable

Marker interface for entities that support tagging:

```csharp
public interface ITaggable
{
    static abstract string TagEntityType { get; }
}

// Implement on your entity:
public class Building : AbstractModel, ITaggable
{
    public static string TagEntityType => "Building";
    public string Name { get; set; } = string.Empty;
}
```

### Tag

Reusable tag entity, tenant-scoped:

```csharp
public class Tag : AbstractModel
{
    public Guid TenantGuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }    // e.g. "#FF5733"
    public string? Group { get; set; }    // e.g. "Priority", "Department"
}
```

### EntityTag

Junction record linking tags to entities:

```csharp
public class EntityTag : AbstractModel
{
    public Guid TenantGuid { get; set; }
    public Guid TagId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;  // discriminator
}
```

## Service

### ITagService

All operations are async, tenant-scoped, and support cancellation:

```csharp
// Tag CRUD
await tagService.CreateTagAsync("urgent", color: "#FF0000", group: "Priority");
await tagService.UpdateTagAsync(tagId, name: "critical");
await tagService.DeleteTagAsync(tagId);  // cascades to entity-tag links

var tags = await tagService.ListTagsAsync();
var results = await tagService.SearchTagsAsync("urg", limit: 10);

// Entity tagging
await tagService.AttachTagAsync("Building", buildingId, tagId);
await tagService.DetachTagAsync("Building", buildingId, tagId);

// Sync — sets tags to exactly the given set
await tagService.SetEntityTagsAsync("Building", buildingId, new[] { tag1, tag2 });

// Quick-tag — creates the tag if it doesn't exist
await tagService.AttachTagByNameAsync("Building", buildingId, "new-tag", color: "#00FF00");

// Get entity's tags
var buildingTags = await tagService.GetEntityTagsAsync("Building", buildingId);
```

### Implementing TagServiceBase

Create a platform-specific implementation by overriding the abstract data access methods:

```csharp
public class SqlTagService : TagServiceBase
{
    private readonly IAsyncBulkStore<Tag> _tagStore;
    private readonly IAsyncBulkStore<EntityTag> _entityTagStore;
    private readonly ITenantContext _tenantContext;

    protected override Guid GetCurrentTenantId() => _tenantContext.TenantId;

    protected override async Task<Tag> CreateTagInternalAsync(Tag tag, CancellationToken ct)
    {
        await _tagStore.CreateAsync(tag);
        return tag;
    }

    protected override Task<Tag?> GetTagByIdAsync(Guid tagId, CancellationToken ct)
        => _tagStore.ReadAsync(tagId, ct);

    protected override Task<Tag?> FindTagByNameAsync(string name, CancellationToken ct)
        => _tagStore.ReadAsync(t => t.Name == name && t.TenantGuid == GetCurrentTenantId(), ct);

    // ... override remaining abstract methods
}
```

## DI Registration

```csharp
services.AddTagService<SqlTagService>();
```

## Key Design Decisions

- **String discriminator** — `EntityType` enables one junction table for all entity types, avoiding per-entity join tables
- **Tenant isolation** — All queries are scoped to the current tenant
- **Idempotent attach** — Attaching an already-attached tag is a no-op
- **Reconciliation** — `SetEntityTagsAsync` computes the diff and only adds/removes what changed
- **Cascade delete** — Deleting a tag removes all its entity-tag links first

## See Also

- [Data Patterns Guide](patterns.md)
- [Multi-Tenancy Guide](tenant.md)
