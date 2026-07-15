# Birko.Data.Tagging

Entity tagging system for the Birko Framework. Provides tenant-scoped tags that can be attached to any entity type via a polymorphic junction table.

## Features

- Reusable tags with optional color and group metadata
- Polymorphic entity-tag junction (one table serves all entity types)
- Tenant-scoped tag namespaces
- Full tag CRUD (create, read, search, update, delete with cascade)
- Attach/detach tags to entities
- Sync entity tags (set to exact desired state)
- Quick-tag by name (auto-creates tag if needed)
- Template Method base service for platform-specific implementations

## Models

| Type | Description |
|------|-------------|
| `ITaggable` | Marker interface — entities implement `static abstract string TagEntityType` |
| `Tag` | Tag entity with TenantGuid, Name, Color, Group |
| `EntityTag` | Junction record: TenantGuid, TagId, EntityId, EntityType |
| `TagDto` | Immutable record for API responses |

## Service

`ITagService` provides all operations. Implement `TagServiceBase` for your platform by overriding the abstract data access methods:

```csharp
public class SqlTagService : TagServiceBase
{
    // Override: CreateTagInternalAsync, GetTagByIdAsync, FindTagByNameAsync,
    //          ListAllTagsAsync, SearchTagsByNameAsync, UpdateTagInternalAsync,
    //          DeleteTagInternalAsync, GetEntityTagLinksAsync, CreateEntityTagAsync,
    //          DeleteEntityTagAsync, DeleteAllEntityTagsForTagAsync, GetCurrentTenantId
}
```

> **Tenant-scoping contract:** `TagServiceBase` stamps `TenantGuid` on every insert, but the abstract
> read/delete hooks receive no tenant parameter — your implementation **must scope every hook**
> (including `GetTagByIdAsync`) to the ambient tenant, or it will return/delete other tenants' data.

## Registration

```csharp
services.AddTagService<SqlTagService>();
```

## Dependencies

- Birko.Data.Core
- Microsoft.Extensions.DependencyInjection.Abstractions
