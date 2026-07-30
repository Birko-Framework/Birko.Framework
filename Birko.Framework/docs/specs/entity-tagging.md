---
area: entity-tagging
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Tagging/Extensions/TaggingExtensions.cs
  - ../Birko.Data.Tagging/Models/EntityTag.cs
  - ../Birko.Data.Tagging/Models/ITaggable.cs
  - ../Birko.Data.Tagging/Models/Tag.cs
  - ../Birko.Data.Tagging/Services/ITagService.cs
  - ../Birko.Data.Tagging/Services/TagService.cs
shaped-by: []
---

# Polymorphic, tenant-scoped entity tagging

## Purpose

`Birko.Data.Tagging` provides a reusable, free-form labelling capability that any entity type can opt
into without schema changes of its own. A `Tag` is a named, optionally coloured and grouped label that
belongs to one tenant; an `EntityTag` is a junction record that links one tag to one entity, identified
by a `(EntityType, EntityId)` pair where `EntityType` is a plain string discriminator — so a single
junction collection serves every taggable entity in the application.

The capability ships **no persistence of its own**. `TagServiceBase` is an abstract template that owns
all the tagging *logic* — name trimming, name de-duplication, attach/detach idempotency, set
reconciliation, cascade-on-delete, DTO projection, and tenant stamping on inserts — while delegating
every read, write and delete to abstract hooks that a consuming platform implements over its own
repositories. Consumers depend on the `ITagService` interface and its `TagDto` projection; the
`ITaggable` marker interface exists for entities to declare their own discriminator string. Tenant
isolation is split: this layer stamps the tenant on inserts, but *reads and deletes carry no tenant
parameter at all*, so isolation on those paths rests entirely on the platform implementation.

## Requirements

### Requirement: Tenant-scoped tag record shape

The system SHALL model a tag as `Tag : AbstractLogModel` carrying `TenantGuid` (`Guid`), `Name`
(non-nullable `string`, defaulting to `string.Empty`), `Color` (nullable `string`) and `TagGroup`
(nullable `string`), inheriting `Guid?` (defaulting to `null`), `CreatedAt`, `UpdatedAt` and
`PrevUpdatedAt` from the log-model base, and SHALL carry no persistence attributes, no uniqueness
declaration and no index declaration of its own.

#### Scenario: Freshly constructed tag

- **Given** a caller executes `new Tag()`
- **When** the instance is inspected before any store or repository has touched it
- **Then** `Name` is `string.Empty`, `Color` is `null`, `TagGroup` is `null`, `TenantGuid` is
  `Guid.Empty` and the inherited `Guid` is `null`

#### Scenario: No storage-level uniqueness backstop for tag names

- **Given** `Tag` as declared in `Models/Tag.cs`
- **When** the type is examined for `[Table]`, `[NamedField]`, `[IndexedField]` or `[CompositeIndex]`
  attributes
- **Then** none are present, so nothing at this layer prevents two rows with the same
  `(TenantGuid, Name)` — de-duplication exists only as the service-level read-then-write in
  `CreateTagAsync`

### Requirement: Polymorphic junction record keyed by a string discriminator

The system SHALL model the tag-to-entity link as `EntityTag : AbstractLogModel` carrying `TenantGuid`,
`TagId`, `EntityId` (both `Guid`) and `EntityType` (non-nullable `string`, defaulting to
`string.Empty`), so that one junction collection serves all entity types, and SHALL NOT constrain
`EntityType` to any enumeration, registry or set of known values.

#### Scenario: Two different entity types share the junction

- **Given** an entity of type `"Building"` with id `B` and an entity of type `"Device"` with id `D`
- **When** both are tagged with the same tag `T`
- **Then** two `EntityTag` records exist, distinguished only by their `EntityType` / `EntityId` values,
  both referencing `TagId = T`

#### Scenario: Arbitrary discriminator string is accepted

- **Given** a caller invoking `AttachTagAsync("not-a-real-type", entityId, tagId)`
- **When** the call completes
- **Then** an `EntityTag` with `EntityType = "not-a-real-type"` is created — no validation rejects an
  unknown or misspelled discriminator

### Requirement: Taggable-entity marker declares its own discriminator

The system SHALL expose `ITaggable` as a marker interface whose sole member is a **static abstract**
`string TagEntityType { get; }`, letting an implementing entity type declare the discriminator used in
the junction record.

#### Scenario: Entity declares its discriminator

- **Given** `public class Building : ITaggable { public static string TagEntityType => "Building"; }`
- **When** a caller reads `Building.TagEntityType`
- **Then** `"Building"` is returned, available without an instance

#### Scenario: The marker is not enforced by the service

- **Given** the whole of `ITagService` and `TagServiceBase`
- **When** they are searched for any reference to `ITaggable` or a generic constraint such as
  `where T : ITaggable`
- **Then** there is none — every entity-facing method takes `entityType` as a raw `string`, so no
  compile-time link exists between an entity's declared `TagEntityType` and the value actually passed

### Requirement: Tag creation trims the name and de-duplicates by name

The system SHALL, in `CreateTagAsync`, first call `FindTagByNameAsync(name.Trim())` and, when a tag is
found, return that existing tag's DTO **without inserting anything**; only on a miss SHALL it construct
a `Tag` with `TenantGuid = GetCurrentTenantId()` and `Name = name.Trim()` and persist it via
`CreateTagInternalAsync`.

#### Scenario: Creating a genuinely new tag

- **Given** no tag named `"urgent"` exists for the current tenant
- **When** a caller invokes `CreateTagAsync("  urgent  ", "#ff0000", "priority")`
- **Then** a `Tag` with `Name = "urgent"` (trimmed), `Color = "#ff0000"`, `TagGroup = "priority"` and
  `TenantGuid` equal to `GetCurrentTenantId()` is passed to `CreateTagInternalAsync`, and the DTO of the
  value that hook *returns* is returned to the caller

#### Scenario: Creating a tag whose name already exists

- **Given** a tag `"urgent"` already exists with `Color = "#ff0000"` and `TagGroup = "priority"`
- **When** a caller invokes `CreateTagAsync("urgent", "#00ff00", "workflow")`
- **Then** no insert occurs and the DTO of the **pre-existing** tag is returned — the supplied
  `"#00ff00"` and `"workflow"` are silently discarded and the stored tag is left unchanged

#### Scenario: Blank name is accepted

- **Given** any tenant state
- **When** a caller invokes `CreateTagAsync("   ")`
- **Then** `FindTagByNameAsync("")` is consulted and, on a miss, a tag with `Name = string.Empty` is
  created — there is no whitespace or emptiness rejection

#### Scenario: Null name faults

- **Given** any tenant state
- **When** a caller invokes `CreateTagAsync(null!)`
- **Then** `name.Trim()` throws `NullReferenceException` before any hook is called

### Requirement: Tag lookup by id projects to a DTO or null

The system SHALL, in `GetTagAsync`, delegate to `GetTagByIdAsync(tagId)` and return `null` when the hook
returns `null`, otherwise the tag projected to `TagDto`.

#### Scenario: Known tag id

- **Given** `GetTagByIdAsync` resolves `tagId` to a tag named `"urgent"`
- **When** a caller invokes `GetTagAsync(tagId)`
- **Then** a `TagDto(tagId, "urgent", color, group)` is returned

#### Scenario: Unknown tag id

- **Given** `GetTagByIdAsync` returns `null` for `tagId`
- **When** a caller invokes `GetTagAsync(tagId)`
- **Then** `null` is returned and no exception is raised

### Requirement: Tag listing and search delegate with a trimmed query

The system SHALL, in `ListTagsAsync`, return every tag from `ListAllTagsAsync` projected to DTOs in the
hook's own order, and SHALL, in `SearchTagsAsync`, pass `query.Trim()` and the caller's `limit` (default
`20`) verbatim to `SearchTagsByNameAsync`, applying no additional filtering, ordering or truncation.

#### Scenario: Search trims the query

- **Given** a caller invokes `SearchTagsAsync("  urg ", 5)`
- **When** the call reaches the platform hook
- **Then** `SearchTagsByNameAsync("urg", 5, ct)` is invoked and its results are projected to DTOs
  unchanged in count and order

#### Scenario: Nonsensical limit is passed through unchecked

- **Given** a caller invokes `SearchTagsAsync("urg", -1)`
- **When** the call reaches the platform hook
- **Then** `SearchTagsByNameAsync("urg", -1, ct)` is invoked — the base class validates neither the sign
  nor an upper bound of `limit`

#### Scenario: Null query faults

- **Given** any tenant state
- **When** a caller invokes `SearchTagsAsync(null!)`
- **Then** `query.Trim()` throws `NullReferenceException` before the hook is called

### Requirement: Tag update is a partial patch with asymmetric blank handling

The system SHALL, in `UpdateTagAsync`, load the tag, then apply only the arguments that are **not
`null`**: `Name` is set to `name.Trim()`; `Color` and `TagGroup` are each set to `null` when the supplied
value is null-or-whitespace and to the supplied value otherwise; and SHALL then persist the mutated
instance via `UpdateTagInternalAsync`.

#### Scenario: Patching only the colour

- **Given** a tag `"urgent"` with `Color = "#ff0000"` and `TagGroup = "priority"`
- **When** a caller invokes `UpdateTagAsync(tagId, color: "#00ff00")`
- **Then** `Color` becomes `"#00ff00"` while `Name` and `TagGroup` retain their stored values

#### Scenario: Clearing colour and group with a blank string

- **Given** a tag with `Color = "#ff0000"` and `TagGroup = "priority"`
- **When** a caller invokes `UpdateTagAsync(tagId, color: "  ", group: "")`
- **Then** both `Color` and `TagGroup` become `null` — a blank string is the clearing idiom, distinct
  from `null` which means "leave alone"

#### Scenario: Blank name is stored rather than rejected or treated as a clear

- **Given** a tag named `"urgent"`
- **When** a caller invokes `UpdateTagAsync(tagId, name: "   ")`
- **Then** `Name` becomes `string.Empty` and is persisted — `Name` receives no null-or-whitespace
  treatment, so blank behaves differently for `name` than for `color` / `group`

#### Scenario: Updating a tag that does not exist

- **Given** `GetTagByIdAsync` returns `null` for `tagId`
- **When** a caller invokes `UpdateTagAsync(tagId, name: "x")`
- **Then** `InvalidOperationException` is thrown with message `"Tag {tagId} not found."` and
  `UpdateTagInternalAsync` is never called

### Requirement: Tag deletion cascades to links, is idempotent and is not transactional

The system SHALL, in `DeleteTagAsync`, load the tag and return silently when it is absent; when present,
it SHALL first call `DeleteAllEntityTagsForTagAsync(tagId)` and only then `DeleteTagInternalAsync(tag)`,
as two independent awaited calls with no surrounding transaction or compensation.

#### Scenario: Deleting a tag that is attached to entities

- **Given** tag `T` is attached to three entities
- **When** a caller invokes `DeleteTagAsync(T)`
- **Then** `DeleteAllEntityTagsForTagAsync(T)` runs first, then `DeleteTagInternalAsync`, leaving no
  dangling `EntityTag` rows

#### Scenario: Deleting an unknown tag

- **Given** `GetTagByIdAsync` returns `null` for `tagId`
- **When** a caller invokes `DeleteTagAsync(tagId)`
- **Then** the call completes with no exception and neither delete hook is invoked

#### Scenario: Tag delete fails after the links were removed

- **Given** tag `T` is attached to entities and `DeleteTagInternalAsync` throws (for example a store
  failure)
- **When** a caller invokes `DeleteTagAsync(T)`
- **Then** the exception propagates while the entity links have **already** been deleted — the tag
  survives with all of its attachments lost, and nothing restores them

### Requirement: Attaching a tag is idempotent per link but does not validate the tag

The system SHALL, in `AttachTagAsync`, read the entity's existing links via
`GetEntityTagLinksAsync(entityType, entityId)` and return without writing when any link already has the
given `TagId`; otherwise it SHALL create an `EntityTag` stamped with
`TenantGuid = GetCurrentTenantId()`, `TagId`, `EntityId` and `EntityType`. It SHALL NOT verify that the
tag exists, nor that it belongs to the current tenant.

#### Scenario: Attaching a tag twice

- **Given** entity `("Building", B)` already has a link to tag `T`
- **When** a caller invokes `AttachTagAsync("Building", B, T)` again
- **Then** no new `EntityTag` is created and the call returns successfully

#### Scenario: Attaching a nonexistent tag id

- **Given** no tag with id `X` exists anywhere
- **When** a caller invokes `AttachTagAsync("Building", B, X)`
- **Then** an `EntityTag` referencing `TagId = X` is created — the dangling link is accepted and is only
  filtered out later, at read time, by `GetEntityTagsAsync` / `GetEntityTagsBatchAsync`

#### Scenario: Concurrent attaches of the same tag

- **Given** two concurrent requests both invoke `AttachTagAsync("Building", B, T)` and both complete
  their `GetEntityTagLinksAsync` read before either writes
- **When** both proceed past the `links.Any(...)` guard
- **Then** both create an `EntityTag`, yielding two duplicate links — the check is read-then-write with
  no lock and no uniqueness constraint behind it

### Requirement: Detaching removes at most one link

The system SHALL, in `DetachTagAsync`, read the entity's links, select the **first** whose `TagId`
matches via `FirstOrDefault`, and delete only that one via `DeleteEntityTagAsync`; when no link matches
it SHALL return silently.

#### Scenario: Detaching an attached tag

- **Given** entity `("Building", B)` has exactly one link to tag `T`
- **When** a caller invokes `DetachTagAsync("Building", B, T)`
- **Then** that `EntityTag` is passed to `DeleteEntityTagAsync` and the entity no longer carries the tag

#### Scenario: Detaching a tag that is not attached

- **Given** entity `("Building", B)` has no link to tag `T`
- **When** a caller invokes `DetachTagAsync("Building", B, T)`
- **Then** the call completes with no exception and `DeleteEntityTagAsync` is never invoked

#### Scenario: Detaching when duplicate links exist

- **Given** entity `("Building", B)` has **two** links to tag `T` (produced by a concurrent attach)
- **When** a caller invokes `DetachTagAsync("Building", B, T)`
- **Then** only the first link is deleted and the entity still reads as tagged with `T`; a second
  `DetachTagAsync` call is required to fully remove it

### Requirement: Set-entity-tags reconciles to exactly the desired set

The system SHALL, in `SetEntityTagsAsync`, read the current links once, materialise the current and
desired tag ids as hash sets, delete every link whose `TagId` is not in the desired set, and then create
one `EntityTag` (stamped with a single `GetCurrentTenantId()` reading) for each desired id not present in
the current set — creating links directly rather than routing through `AttachTagAsync`, and without any
enclosing transaction.

#### Scenario: Replacing a tag set

- **Given** entity `("Building", B)` is linked to tags `{T1, T2}`
- **When** a caller invokes `SetEntityTagsAsync("Building", B, [T2, T3])`
- **Then** the link to `T1` is deleted, the link to `T2` is left untouched, and one new link to `T3` is
  created

#### Scenario: Clearing all tags

- **Given** entity `("Building", B)` is linked to tags `{T1, T2}`
- **When** a caller invokes `SetEntityTagsAsync("Building", B, [])`
- **Then** both links are deleted and no link is created

#### Scenario: Duplicate ids in the desired list collapse

- **Given** entity `("Building", B)` has no links
- **When** a caller invokes `SetEntityTagsAsync("Building", B, [T1, T1, T1])`
- **Then** `desiredTagIds.ToHashSet()` collapses the input and exactly one `EntityTag` for `T1` is
  created

#### Scenario: Pre-existing duplicate links to a desired tag are not repaired

- **Given** entity `("Building", B)` has two links to `T1`
- **When** a caller invokes `SetEntityTagsAsync("Building", B, [T1])`
- **Then** neither link is deleted (both are in the desired set) and none is created — the duplicate
  survives; reconciliation removes only *undesired* links, it does not deduplicate

#### Scenario: No round-trip per added tag

- **Given** entity `("Building", B)` has no links and three new tags are requested
- **When** `SetEntityTagsAsync` adds them
- **Then** `GetEntityTagLinksAsync` is invoked exactly **once** for the whole operation (not once per
  added tag), because the add path calls `CreateEntityTagAsync` directly

### Requirement: Reading an entity's tags resolves each link individually and drops unresolvable ones

The system SHALL, in `GetEntityTagsAsync`, fetch the entity's links and then call `GetTagByIdAsync` once
per link in link order, appending the DTO when the tag resolves and **silently skipping** the link when
`GetTagByIdAsync` returns `null`.

#### Scenario: Entity with two resolvable tags

- **Given** entity `("Building", B)` has links to `T1` then `T2`, both resolvable
- **When** a caller invokes `GetEntityTagsAsync("Building", B)`
- **Then** two DTOs are returned in link order, and `GetTagByIdAsync` was called twice — one query per
  link, an N+1 read pattern

#### Scenario: Entity with a dangling link

- **Given** entity `("Building", B)` has links to `T1` (resolvable) and `X` (deleted, or belonging to
  another tenant and filtered out by the implementation's read hook)
- **When** a caller invokes `GetEntityTagsAsync("Building", B)`
- **Then** only `T1`'s DTO is returned; the unresolvable link produces neither an entry nor an error

#### Scenario: Entity with duplicate links to the same tag

- **Given** entity `("Building", B)` has two links to tag `T1`
- **When** a caller invokes `GetEntityTagsAsync("Building", B)`
- **Then** the same tag appears **twice** in the returned list — the result is not deduplicated

#### Scenario: Untagged entity

- **Given** entity `("Building", B)` has no links
- **When** a caller invokes `GetEntityTagsAsync("Building", B)`
- **Then** an empty list is returned and `GetTagByIdAsync` is never called

### Requirement: Attach-by-name creates the tag on a miss via the de-duplicating create path

The system SHALL, in `AttachTagByNameAsync`, look the tag up by `tagName.Trim()`; on a hit it SHALL
attach `tag.Guid!.Value` and return that tag's DTO; on a miss it SHALL call `CreateTagAsync(tagName,
color)` — deliberately re-running the name lookup inside that call to narrow the create-create race —
then attach the resulting id and return the created DTO. It SHALL NOT pass a tag group.

#### Scenario: Quick-tagging with an existing name

- **Given** a tag `"urgent"` exists with `Color = "#ff0000"`
- **When** a caller invokes `AttachTagByNameAsync("Building", B, "  urgent ", "#00ff00")`
- **Then** the existing tag is attached, its unchanged DTO (still `"#ff0000"`) is returned, and no tag is
  created — the supplied colour is ignored

#### Scenario: Quick-tagging with a new name

- **Given** no tag named `"needs-review"` exists
- **When** a caller invokes `AttachTagByNameAsync("Device", D, "needs-review", "#0000ff")`
- **Then** `CreateTagAsync("needs-review", "#0000ff")` runs (performing its own `FindTagByNameAsync`
  re-check), the new tag is attached to `("Device", D)`, and the created DTO is returned with
  `TagGroup = null`

#### Scenario: Concurrent quick-tagging of the same new name

- **Given** two concurrent calls to `AttachTagByNameAsync(..., "needs-review")` both miss on their first
  lookup
- **When** the second reaches `CreateTagAsync` after the first has inserted
- **Then** `CreateTagAsync`'s own `FindTagByNameAsync` finds the tag and returns it instead of inserting
  a duplicate — the window is narrowed but not closed, since there is no unique-name constraint at this
  layer to fall back on

### Requirement: Batch tag loading returns an entry for every requested entity

The system SHALL, in `GetEntityTagsBatchAsync`, return an empty dictionary immediately when `entityIds`
is empty (issuing no query at all); otherwise it SHALL fetch links via `GetEntityTagLinksBatchAsync`,
load each **distinct** `TagId` once via `GetTagByIdAsync`, group the links by `EntityId`, project each
group's resolvable tags to DTOs, and finally add an empty list for every requested entity id absent from
the grouping.

#### Scenario: Mixed batch

- **Given** entities `B1` (tags `T1`, `T2`), `B2` (tag `T1`) and `B3` (no tags)
- **When** a caller invokes `GetEntityTagsBatchAsync("Building", [B1, B2, B3])`
- **Then** the result has three keys: `B1 → [T1, T2]`, `B2 → [T1]`, `B3 → []`, and `GetTagByIdAsync` was
  called **twice** (once per distinct tag, not once per link)

#### Scenario: Empty request short-circuits

- **Given** any tenant state
- **When** a caller invokes `GetEntityTagsBatchAsync("Building", [])`
- **Then** an empty dictionary is returned and `GetEntityTagLinksBatchAsync` is never invoked

#### Scenario: Dangling links in a batch

- **Given** entity `B1` has links to `T1` (resolvable) and `X` (unresolvable)
- **When** the batch is loaded
- **Then** `B1` maps to `[T1]` only; if *all* of an entity's links are unresolvable the entity's key is
  still present from the grouping, mapping to an empty list

#### Scenario: Hook returns links for entities that were not requested

- **Given** a platform implementation whose `GetEntityTagLinksBatchAsync` returns links for entity `B9`
  in addition to the requested `[B1]`
- **When** the batch is loaded
- **Then** the returned dictionary contains a `B9` key as well — the base class groups whatever links the
  hook returned and never intersects the grouping back against `entityIds`

### Requirement: DTO projection requires a persisted identity

The system SHALL project a `Tag` to the immutable `sealed record TagDto(Guid Id, string Name, string?
Color, string? TagGroup)` by dereferencing `t.Guid!.Value`, exposing neither `TenantGuid` nor any
timestamp, and SHALL therefore throw when a tag whose inherited `Guid` is still `null` is projected.

#### Scenario: Projecting a persisted tag

- **Given** a `Tag` whose `Guid` has been assigned by the store
- **When** any service method projects it
- **Then** a `TagDto` carrying that `Guid` as `Id`, plus `Name`, `Color` and `TagGroup`, is returned, with
  `TenantGuid`, `CreatedAt`, `UpdatedAt` and `PrevUpdatedAt` deliberately not exposed

#### Scenario: Implementation hook returns a tag with no assigned Guid

- **Given** a platform whose `CreateTagInternalAsync` returns the `Tag` it was handed without the store
  having populated `Guid` (which defaults to `null` on `AbstractModel`)
- **When** `CreateTagAsync` projects that result
- **Then** `t.Guid!.Value` throws `InvalidOperationException` ("Nullable object must have a value") —
  there is no null guard and no clearer diagnostic

### Requirement: Tenant scoping is stamped on inserts only, never enforced on reads or deletes

The system SHALL stamp `TenantGuid = GetCurrentTenantId()` on every record it inserts — the `Tag` in
`CreateTagAsync` and the `EntityTag` in `AttachTagAsync` and `SetEntityTagsAsync` — and SHALL declare
every read and delete hook (`GetTagByIdAsync`, `FindTagByNameAsync`, `ListAllTagsAsync`,
`SearchTagsByNameAsync`, `UpdateTagInternalAsync`, `DeleteTagInternalAsync`, `GetEntityTagLinksAsync`,
`DeleteEntityTagAsync`, `DeleteAllEntityTagsForTagAsync`, `GetEntityTagLinksBatchAsync`) **without any
tenant parameter**, performing no comparison of a loaded record's `TenantGuid` against the current
tenant anywhere in `TagServiceBase`.

#### Scenario: Inserts are stamped

- **Given** `GetCurrentTenantId()` returns tenant `A`
- **When** a caller invokes `CreateTagAsync("urgent")` and then `AttachTagAsync("Building", B, tagId)`
- **Then** both the `Tag` and the `EntityTag` handed to the platform hooks carry `TenantGuid = A`

#### Scenario: Cross-tenant read is not blocked by the base class

- **Given** `GetCurrentTenantId()` returns tenant `A`, and a platform implementation whose
  `GetTagByIdAsync` looks up by primary key alone without a tenant filter
- **When** a caller invokes `GetTagAsync(tagIdOwnedByTenantB)`
- **Then** tenant `B`'s tag is returned to tenant `A` — `TagServiceBase` performs no
  `tag.TenantGuid == GetCurrentTenantId()` check to catch the implementation's omission

#### Scenario: Cross-tenant update and delete are not blocked either

- **Given** the same unfiltered implementation and current tenant `A`
- **When** a caller invokes `UpdateTagAsync(tagIdOwnedByTenantB, name: "hijacked")` or
  `DeleteTagAsync(tagIdOwnedByTenantB)`
- **Then** the operation proceeds against tenant `B`'s tag, because both methods reach it through the
  same unguarded `GetTagByIdAsync` and neither compares its `TenantGuid`

#### Scenario: A link may reference another tenant's tag while carrying this tenant's stamp

- **Given** current tenant `A` and a tag id `T` owned by tenant `B`
- **When** a caller invokes `AttachTagAsync("Building", B_entity, T)`
- **Then** an `EntityTag` with `TenantGuid = A` and `TagId = T` is created, since attach validates
  neither the tag's existence nor its ownership

### Requirement: Scoped DI registration of the tag service

The system SHALL provide `TaggingExtensions.AddTagService<TImpl>()` which registers `TImpl` as the
`ITagService` implementation with **scoped** lifetime via `services.AddScoped<ITagService, TImpl>()` and
returns the same `IServiceCollection` for chaining, constraining `TImpl` to `class, ITagService`, and
SHALL NOT register the `Tag` / `EntityTag` repositories the implementation depends on.

#### Scenario: Registering a platform implementation

- **Given** an `IServiceCollection` and a `SymbioTagService : TagServiceBase`
- **When** `services.AddTagService<SymbioTagService>()` is called
- **Then** resolving `ITagService` from a scope yields a `SymbioTagService`, one instance per scope, and
  the same collection instance is returned for further chaining

#### Scenario: Repositories must be registered separately

- **Given** a service collection where only `AddTagService<SymbioTagService>()` has been called
- **When** `ITagService` is resolved and the implementation's constructor requires repositories for `Tag`
  and `EntityTag`
- **Then** resolution fails with the container's missing-dependency error — the extension registers only
  the service itself

#### Scenario: Repeated registration is not idempotent

- **Given** `services.AddTagService<ImplA>()` followed by `services.AddTagService<ImplB>()`
- **When** the descriptors are inspected
- **Then** two `ITagService` descriptors exist (`AddScoped`, not `TryAddScoped`), and a single-service
  resolution returns `ImplB` — the last registration wins while `IEnumerable<ITagService>` yields both
