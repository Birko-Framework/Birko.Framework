# Birko.Data.Tagging

Entity tagging system with tenant-scoped tags, polymorphic entity-tag junction, and async service layer.

## Components

### Models
- **ITaggable** — Marker interface with `static abstract string TagEntityType` discriminator for entities that support tagging
- **Tag** — Reusable tag entity (AbstractLogModel) with TenantGuid, Name, Color, TagGroup
- **EntityTag** — Junction entity linking Tag to any entity via EntityType string discriminator + EntityId

### Services
- **TagDto** — Sealed record DTO (Id, Name, Color, TagGroup)
- **ITagService** — Full CRUD + attach/detach/sync/attach-by-name operations, all async with CancellationToken
- **TagServiceBase** — Template Method base class: concrete business logic (deduplication, idempotent attach, reconciliation) with abstract data access methods for platform implementations

### Extensions
- **TaggingExtensions** — `AddTagService<TImpl>()` DI registration

## Dependencies
- Birko.Data.Core (AbstractModel)
- Microsoft.Extensions.DependencyInjection.Abstractions

## Patterns
- Template Method (TagServiceBase)
- String discriminator for polymorphic many-to-many
- Tenant-scoped data isolation — **layered, and the base class now enforces it (SH-H019):** the base
  stamps `TenantGuid` on inserts, and although the abstract read/delete hooks still carry no tenant
  parameter, **the base re-checks the tenant of every record they return.** Implementations SHOULD
  still filter every read (including `GetTagByIdAsync`) — for correctness and for the query plan — but
  the base no longer *depends* on it. Two rules, chosen by failure mode:
  - **By-identity loads throw `CrossTenantTagAccessException`.** `GetTagAsync` / `UpdateTagAsync` /
    `DeleteTagAsync` / `GetEntityTagsAsync` all reach their target through one `GetTagByIdAsync`, and
    the by-name lookup behind `CreateTagAsync` / `AttachTagByNameAsync` is guarded the same way. Not
    downgraded to "not found": the caller named one record, so a null would hide a breach as a miss.
    The `DeleteTagAsync` guard runs **before** `DeleteAllEntityTagsForTagAsync`, so a foreign tag never
    reaches the cascade.
  - **Collections filter.** `ListTagsAsync` / `SearchTagsAsync` / the entity-link paths drop foreign
    records rather than raising — one leaked row must not blank a whole tag picker. Accepted trade: a
    broken hook is quieter here than on the by-identity paths.
  - **`Guid.Empty` is a tenant value, not a wildcard** — a record stamped `Guid.Empty` is visible only
    while `GetCurrentTenantId()` also returns `Guid.Empty`.
  - This was previously written as *"the base class cannot guard a hook that forgets"* (CR-L228). It
    could; it just didn't. Everything else followed from that sentence being accepted as fixed — one
    implementation omitting one filter exposed cross-tenant reads, writes and a cascade delete across
    every implementation of the base.
- Idempotent operations (attach checks for duplicates)
- Reconciliation (SetEntityTags syncs desired vs current)

## Namespace
`Birko.Data.Tagging`
