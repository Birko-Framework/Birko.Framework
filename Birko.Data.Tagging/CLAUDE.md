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
- Tenant-scoped data isolation — **split responsibility (CR-L228):** the base class stamps
  `TenantGuid` on inserts, but the abstract read/delete hooks carry no tenant parameter, so
  **implementations MUST filter every read (including `GetTagByIdAsync`) to the ambient tenant**;
  the base class cannot guard a hook that forgets
- Idempotent operations (attach checks for duplicates)
- Reconciliation (SetEntityTags syncs desired vs current)

## Namespace
`Birko.Data.Tagging`
