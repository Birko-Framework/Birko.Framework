---
area: core-model-contracts
generated-at: 50828c68d6f35ca2096600381c2d0c4481132330
generated-on: 2026-10-21
sources:
  - ../Birko.Contracts/Models/ICopyable.cs
  - ../Birko.Contracts/Models/IDefault.cs
  - ../Birko.Contracts/Models/IGuidEntity.cs
  - ../Birko.Contracts/Models/ILoadable.cs
  - ../Birko.Contracts/Models/ILogEntity.cs
  - ../Birko.Contracts/Models/ITimestamped.cs
  - ../Birko.Data.Core/Models/AbstractLogModel.cs
  - ../Birko.Data.Core/Models/AbstractModel.cs
  - ../Birko.Data.Core/Models/ICopyable.cs
  - ../Birko.Data.Core/Models/IDefault.cs
  - ../Birko.Data.Core/Models/ILoadable.cs
  - ../Birko.Data.Core/Models/ITimestamped.cs
  - ../Birko.Data.CosmosDB/Serialization/CosmosGuidIdSerializer.cs
  - ../Birko.Data.MongoDB/Models/MongoDBLogModel.cs
  - ../Birko.Data.MongoDB/Models/MongoDBModel.cs
  - ../Birko.Data.MongoDB/Serialization/MongoSerialization.cs
  - ../Birko.Data.SQL/Models/AbstractDatabasePercentage.cs
  - ../Birko.Data.SQL/Models/AbstractDatabaseTree.cs
  - ../Birko.Data.SQL/Models/AbstractLogModel.cs
  - ../Birko.Data.SQL/Models/AbstractModel.cs
  - ../Birko.Data.SQL/Models/DatabaseValueData.cs
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-21 (TASK-696) over 376 feature-linked tasks: 117 resolved, 259 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 1 commit leads with the id of a task the state gate refuses. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 259
---
# Zero-dependency model contracts (loadable, copyable, default, timestamped)

## Purpose

This capability is the bottom of the Birko dependency graph: a handful of dependency-free interfaces
(`IGuidEntity`, `ITimestamped`, `ILogEntity`, `ICopyable<T>`, `ILoadable<T>`, `IDefault`) plus the two abstract
base classes every persisted Birko entity derives from (`AbstractModel`, `AbstractLogModel`). The interfaces
exist so that entities and their view-model counterparts can be mapped onto each other in both directions
without either side referencing the other's type, and so that generic infrastructure can recognise a capability
structurally rather than by concrete type — store decorators stamp timestamps by testing for `ITimestamped`,
enforce a single default row by testing for `IDefault`, and filters address entities by `IGuidEntity.Guid`. The
base classes supply the identity property, the state-copy operation and the state-load operation that the rest
of the framework (stores, repositories, sync, view models) assumes is present on every model.

Around that core sit the backend-specific model bases and wire adapters that consume it: the SQL bases
(`AbstractDatabaseModel`, `AbstractDatabaseLogModel`) that mark the identity as the primary key, the SQL
storage-shape overrides of three `Birko.Models` types (`DatabaseValueData`, `AbstractDatabasePercentage`,
`AbstractDatabaseTree`), the MongoDB bases (`MongoDBModel`, `MongoDBLogModel`) and the one-time MongoDB
driver registration for the canonical identity, and the Cosmos DB serializer that projects the identity onto
the document `id`.

Two structural facts shape everything below. First, all of the core types live in the namespace
`Birko.Data.Models` even though they are split across two shared projects. Second, `AbstractLogModel`
**overrides** the `AbstractModel` copy/load virtuals and also keeps its typed overloads, so the copy/load
behaviour is the same through a base-typed reference as through the derived one (TASK-323 — before it, the
derived methods were overloads only, and the behaviour depended on the *static* type of the reference).

## Requirements

### Requirement: Nullable Guid identity

The system SHALL expose entity identity as a nullable `Guid?` named `Guid` through `IGuidEntity`, and
`AbstractModel` SHALL implement it as a `virtual` auto-property initialised to `null`.

#### Scenario: A freshly constructed model has no identity

- **Given** a concrete class deriving from `AbstractModel` with no constructor logic
- **When** an instance is created with `new`
- **Then** its `Guid` property is `null`, because `AbstractModel` declares `public virtual Guid? Guid { get; set; } = null;`

#### Scenario: Identity is settable through the interface

- **Given** an instance of a class deriving from `AbstractModel`, referenced as `IGuidEntity`
- **When** `Guid` is assigned `Guid.NewGuid()` through the interface reference
- **Then** the assignment succeeds, because `IGuidEntity.Guid` declares both a getter and a setter

#### Scenario: A derived model may replace the identity property

- **Given** a class deriving from `AbstractModel`
- **When** it declares `public override Guid? Guid { get; set; }` with backing logic of its own
- **Then** the override compiles and is dispatched to, because the base property is declared `virtual`

### Requirement: Store-assigned identity is produced by one method that keeps a caller's Guid

The system SHALL expose `AbstractModel.EnsureGuid()`, a public non-virtual method that assigns
`System.Guid.NewGuid()` to `Guid` when `Guid` is `null` or `Guid.Empty`, leaves a caller-supplied non-empty
`Guid` unchanged, and returns the resulting non-null value (TASK-556). `Guid.Empty` SHALL therefore never be
kept as an identity by this method.

#### Scenario: A model with no identity is given a new one

- **Given** a model deriving from `AbstractModel` whose `Guid` is `null`
- **When** `EnsureGuid()` is called
- **Then** `Guid` is set to a newly generated, non-empty value and that same value is returned

#### Scenario: Guid.Empty is treated as no identity

- **Given** a model whose `Guid` is `Guid.Empty`
- **When** `EnsureGuid()` is called
- **Then** `Guid` is replaced with a newly generated value — `Guid.Empty` is not kept as a key

#### Scenario: A caller-supplied identity is kept

- **Given** a model whose `Guid` was set by the caller to a non-empty value `g`
- **When** `EnsureGuid()` is called, once or repeatedly
- **Then** `Guid` remains `g` and `g` is returned each time

#### Scenario: The identity is read and written through the virtual property

- **Given** a derived model that overrides `Guid` with backing logic of its own
- **When** `EnsureGuid()` runs
- **Then** both the null/empty test and the assignment go through the overridden property, because the method accesses `Guid` rather than a backing field

### Requirement: Timestamp tracking contract

The system SHALL define `ITimestamped` as exactly three mutable members — a non-nullable `DateTime CreatedAt`,
a non-nullable `DateTime UpdatedAt`, and a **nullable** `DateTime? PrevUpdatedAt` — and SHALL NOT initialise or
maintain them in the contract itself.

#### Scenario: An unstamped log model carries default DateTime values, not null

- **Given** a concrete class deriving from `AbstractLogModel`
- **When** an instance is created and never stamped
- **Then** `CreatedAt` and `UpdatedAt` are `default(DateTime)` (`0001-01-01`) and `PrevUpdatedAt` is `null`, because only `PrevUpdatedAt` is declared nullable

#### Scenario: The previous-update slot is distinguishable from "never updated"

- **Given** an entity whose `UpdatedAt` has been set once
- **When** `PrevUpdatedAt` is inspected
- **Then** it can hold `null` to mean "there was no prior update", a state `CreatedAt`/`UpdatedAt` cannot express

### Requirement: ILogEntity is a pure composition

The system SHALL define `ILogEntity` as the composition of `IGuidEntity` and `ITimestamped` that declares no
members of its own, so that any type satisfying both parent interfaces satisfies it structurally.

#### Scenario: The composed interface adds nothing

- **Given** the declaration `public interface ILogEntity : IGuidEntity, ITimestamped { }`
- **When** a type implements `Guid`, `CreatedAt`, `UpdatedAt` and `PrevUpdatedAt`
- **Then** declaring `ILogEntity` on that type requires no additional members

#### Scenario: A log entity is usable wherever a guid entity is expected

- **Given** an `AbstractLogModel` instance
- **When** it is passed to a parameter typed `IGuidEntity`
- **Then** the conversion is implicit, because `ILogEntity` derives from `IGuidEntity`

### Requirement: Default-flag contract

The system SHALL define `IDefault` as a single mutable `bool IsDefault` property and SHALL NOT constrain how
many instances in a collection may carry it.

#### Scenario: The flag is readable and writable

- **Given** a model implementing `IDefault`
- **When** `IsDefault` is set to `true` through the interface
- **Then** the assignment succeeds, because `IDefault` declares `bool IsDefault { get; set; }`

#### Scenario: Uniqueness is not enforced by the contract

- **Given** two model instances both implementing `IDefault`
- **When** both have `IsDefault` set to `true`
- **Then** neither the interface nor any base class rejects it — the contract carries no invariant, so single-default enforcement must come from a consumer of this interface

### Requirement: Copy of identity state

The system SHALL implement `AbstractModel.CopyTo(AbstractModel? clone = null)` such that, given a non-null
target, it copies **only** the `Guid` property onto the target and returns that same target instance.

#### Scenario: Guid is transferred to the supplied target

- **Given** a source model with `Guid` set and a distinct target instance with `Guid` null
- **When** `source.CopyTo(target)` is called through an `AbstractModel`-typed reference
- **Then** `target.Guid` equals the source `Guid` and the returned reference is the same object as `target`

#### Scenario: No other state is copied by the base implementation

- **Given** a source and target both deriving from `AbstractModel` and declaring extra properties of their own
- **When** `AbstractModel.CopyTo(target)` runs
- **Then** only `Guid` is assigned — the base method body contains exactly `clone.Guid = Guid;` — so derived state must be copied by a derived `CopyTo`

### Requirement: An omitted copy target is refused by the abstract bases

The system SHALL, when `AbstractModel.CopyTo` or `AbstractLogModel.CopyTo` is called with a null or omitted
target, throw `ArgumentNullException` (parameter `clone`): an abstract base cannot allocate the concrete type, and
returning `this` would hand back the source as its own copy. The optional parameter is kept, so a concrete model
that overrides `CopyTo` may allocate its own type when no target is supplied (SH-M424, fixed by TASK-323; it used
to return `this`).

#### Scenario: CopyTo() with no argument on a model with no allocating override

- **Given** a model instance `src` deriving from `AbstractModel` or `AbstractLogModel` that does not override `CopyTo`
- **When** `src.CopyTo()` is evaluated, through its own type or an `AbstractModel` reference
- **Then** `ArgumentNullException` is thrown — no alias of `src` is returned

#### Scenario: A concrete model that allocates its own copy

- **Given** a model that overrides `CopyTo(AbstractModel? clone = null)` and allocates its type when `clone` is null, then calls `base.CopyTo(target)`
- **When** `src.CopyTo()` is evaluated
- **Then** a new instance carrying `src`'s state is returned, never `src` itself

#### Scenario: The documented nullability contract matches the code

- **Given** the XML documentation on `ICopyable<T>.CopyTo`
- **When** it is read beside `AbstractModel.CopyTo`
- **Then** both say the abstract bases throw on a null target and a concrete override may allocate

### Requirement: Copy of timestamped state

The system SHALL implement `AbstractLogModel.CopyTo(AbstractLogModel? clone = null)` such that, given a non-null
target, it first delegates to `base.CopyTo(clone)` to transfer `Guid` and then copies `CreatedAt`, `UpdatedAt`
and `PrevUpdatedAt` onto the target, returning that target.

#### Scenario: All four fields transfer through the derived overload

- **Given** a source with `Guid` set, `CreatedAt` = `2020-01-01`, `UpdatedAt` = `2021-01-01`, `PrevUpdatedAt` = `2019-01-01`, and an empty target
- **When** `source.CopyTo(target)` is called through an `AbstractLogModel`-typed reference
- **Then** the target carries the source `Guid`, `CreatedAt` = `2020-01-01`, `UpdatedAt` = `2021-01-01` and `PrevUpdatedAt` = `2019-01-01`

#### Scenario: The no-argument call resolves to the derived overload

- **Given** an instance whose static type is `AbstractLogModel` (or a class derived from it), so that both `CopyTo(AbstractModel? = null)` and `CopyTo(AbstractLogModel? = null)` are applicable with zero arguments
- **When** `src.CopyTo()` is evaluated
- **Then** it compiles without ambiguity and binds to the `AbstractLogModel` overload — an override is not a candidate declared on the derived type — which throws `ArgumentNullException` for the missing target

#### Scenario: The typed overload does not dispatch back through the override

- **Given** a model derived from `AbstractLogModel` that overrides `CopyTo(AbstractModel?)` and calls `base.CopyTo(target)` with a target of its own type
- **When** the override runs
- **Then** `base.CopyTo(target)` binds to the typed overload, which copies `Guid` and the timestamps without a virtual call — so it does not recurse into the override

### Requirement: Copy dispatch does not depend on the static reference type

The system SHALL make `AbstractLogModel` override the `virtual` `AbstractModel.CopyTo(AbstractModel?)`, copying
`CreatedAt`, `UpdatedAt` and `PrevUpdatedAt` whenever the target is an `AbstractLogModel`, so invoking `CopyTo`
through an `AbstractModel` or `ICopyable<AbstractModel>` reference transfers the timestamps (SH-M422, fixed by
TASK-323; the derived method used to be an overload only, and a base-typed call left them at `0001-01-01`).

#### Scenario: Copying through a base-typed reference keeps the timestamps

- **Given** `AbstractModel asBase = logModelWithCreatedAt2020;` and an empty target of the same concrete type
- **When** `asBase.CopyTo(target)` is called
- **Then** `target.Guid`, `target.CreatedAt`, `target.UpdatedAt` and `target.PrevUpdatedAt` all equal the source's

#### Scenario: Copying through the generic interface keeps the timestamps

- **Given** `ICopyable<AbstractModel> ic = logModel;`
- **When** `ic.CopyTo(target)` is called with an `AbstractLogModel` target
- **Then** the target's timestamps equal the source's

### Requirement: Load of identity state with null tolerance

The system SHALL implement `AbstractModel.LoadFrom(IGuidEntity data)` as a `virtual` method that copies `Guid`
from the source when the source is non-null, and performs no operation at all when the source is null.

#### Scenario: Guid is read from any IGuidEntity source

- **Given** any object implementing `IGuidEntity` with `Guid` set, and a target deriving from `AbstractModel`
- **When** `target.LoadFrom(source)` is called
- **Then** `target.Guid` equals the source `Guid`, regardless of the source's concrete type

#### Scenario: A null source is silently ignored

- **Given** a target deriving from `AbstractLogModel`
- **When** `target.LoadFrom((ILogEntity)null!)` is called
- **Then** no exception is thrown — not `ArgumentNullException` and not `NullReferenceException` — and the target is left unmodified, because both `LoadFrom` bodies are wrapped in `if (data != null)`

#### Scenario: Passing null requires suppressing a compiler diagnostic

- **Given** the declaration `void LoadFrom(IGuidEntity data)` with a non-nullable parameter
- **When** a caller passes a literal `null`
- **Then** the compiler reports the nullable diagnostic at the call site even though the implementation tolerates it, because the parameter is not annotated `IGuidEntity?` despite the runtime null check

### Requirement: Load of timestamped state

The system SHALL implement `AbstractLogModel.LoadFrom(ILogEntity data)` such that it delegates to
`base.LoadFrom(data)` and then, when the source is non-null, copies `CreatedAt`, `UpdatedAt` and
`PrevUpdatedAt` from the source.

#### Scenario: All four fields load through the derived overload

- **Given** an `ILogEntity` source carrying a `Guid`, `CreatedAt` = `2020-01-01`, `UpdatedAt` = `2021-01-01`, `PrevUpdatedAt` = `2019-01-01`
- **When** `target.LoadFrom(source)` is called on an `AbstractLogModel`-typed target
- **Then** all four values are present on the target

#### Scenario: base.LoadFrom is invoked before the null check

- **Given** a null source
- **When** `AbstractLogModel.LoadFrom(null)` runs
- **Then** `base.LoadFrom(null)` is entered unconditionally and returns without effect thanks to its own guard, then the derived guard skips the timestamp copy — so the outer method has no null check preceding the base call and relies on the base method's guard

### Requirement: Load dispatch does not depend on the static reference type

The system SHALL make `AbstractLogModel` override the `virtual` `AbstractModel.LoadFrom(IGuidEntity)`, loading
`CreatedAt`, `UpdatedAt` and `PrevUpdatedAt` whenever the source is an `ILogEntity`, so loading through an
`ILoadable<IGuidEntity>` reference keeps the timestamps the argument carries (SH-M423, fixed by TASK-323; the
derived method used to be an overload only). A source that is only an `IGuidEntity` loads the `Guid` and leaves
the timestamps unchanged.

#### Scenario: Timestamps present on the argument survive interface dispatch

- **Given** `ILoadable<IGuidEntity> il = target;` where `target` derives from `AbstractLogModel`, and a source that is itself an `ILogEntity` with `CreatedAt` = `2020-01-01`
- **When** `il.LoadFrom(source)` is called
- **Then** `target.Guid` and `target.CreatedAt` = `2020-01-01` are both loaded

#### Scenario: A guid-only source leaves the timestamps alone

- **Given** a log model with `CreatedAt` set, loaded through `ILoadable<IGuidEntity>`
- **When** the source implements only `IGuidEntity`
- **Then** `Guid` is loaded and `CreatedAt` keeps its value

### Requirement: Contract types share one namespace across two projects

The system SHALL declare every contract interface and both abstract bases of this capability (and the SQL
bases `AbstractDatabaseModel` / `AbstractDatabaseLogModel`) in the namespace `Birko.Data.Models`, and the compiled
definitions of `ICopyable<T>`, `ILoadable<T>`, `IDefault`, `ITimestamped`, `IGuidEntity` and `ILogEntity` SHALL
come from `Birko.Contracts`, which `Birko.Data.Core` imports.

#### Scenario: Data.Core pulls the contracts in through a guarded shared-project import

- **Given** `Birko.Data.Core.projitems`
- **When** its `Import` of `..\Birko.Contracts\Birko.Contracts.projitems` is evaluated
- **Then** it is applied only when the MSBuild property `BirkoContractsProjitemsImported` is not already `'true'`, so a consumer importing both projitems sets gets exactly one copy of the contract sources and no duplicate-type error

#### Scenario: The interface files under Birko.Data.Core/Models are not part of the build

- **Given** the files `Birko.Data.Core/Models/ICopyable.cs`, `IDefault.cs`, `ILoadable.cs` and `ITimestamped.cs` present on disk
- **When** `Birko.Data.Core.projitems` is inspected
- **Then** its `<Compile>` items under `Models\` list only `AbstractLogModel.cs` and `AbstractModel.cs`; the four interface files are unreferenced and contribute nothing to any compilation — were they included, they would collide with the `Birko.Contracts` declarations of the same names in the same namespace

#### Scenario: Only the uncompiled copy of ICopyable carries the nullability documentation

- **Given** the two `ICopyable.cs` files
- **When** they are compared
- **Then** they are identical except that the uncompiled `Birko.Data.Core` copy carries the CR-L101 nullability XML documentation block, which is therefore absent from the type that actually ships; `IDefault.cs`, `ILoadable.cs` and `ITimestamped.cs` are byte-identical between the two projects

### Requirement: Base models are open for extension

The system SHALL declare `AbstractModel` and `AbstractLogModel` as `abstract partial` classes whose every
state property (`Guid`, `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt`) is `virtual`, so consumers can add
declarations in additional parts and replace property behaviour.

#### Scenario: Neither base class can be instantiated directly

- **Given** the declaration `public abstract partial class AbstractModel`
- **When** `new AbstractModel()` is attempted
- **Then** it does not compile — models are reachable only through a concrete derived type

#### Scenario: The partial keyword currently has a single part each

- **Given** a search of the framework for additional `partial class AbstractModel` / `partial class AbstractLogModel` declarations
- **When** the results are counted
- **Then** exactly one declaration of each exists (`Birko.Data.Core/Models/AbstractModel.cs` and `AbstractLogModel.cs`), so `partial` is presently an unused extension point reserved for consumer-side parts

#### Scenario: Nullable-widening on CopyTo is warning-free

- **Given** `ICopyable<AbstractModel>` declaring `AbstractModel CopyTo(AbstractModel clone)` with a non-nullable parameter, and `AbstractModel` implementing it as `CopyTo(AbstractModel? clone = null)`
- **When** the code is compiled with `<Nullable>enable</Nullable>`
- **Then** no nullability diagnostic is produced, because accepting a nullable argument where the interface promises non-null widens the accepted input and is permitted

### Requirement: The MongoDB wire contract for the canonical identity is registered once, centrally

The system SHALL register the MongoDB driver serialization that `AbstractModel`-derived entities depend on
exactly once per process, from `Birko.Data.MongoDB.Serialization.MongoSerialization.EnsureRegistered()`, and
SHALL invoke it from the `MongoDBClient` constructor — the single point through which both
`MongoDBStore.SetSettings` and `AsyncMongoDBStore.SetSettings` obtain a client. The registration SHALL map
`AbstractModel.Guid` as the document's `_id`, represented as a BSON **string**, and SHALL install a default
`GuidSerializer` carrying `GuidRepresentation.Standard` for un-attributed `Guid` members.

This requirement replaces the `[BsonRepresentation(BsonType.String)]` override that `MongoDBModel` used to
declare. Both `TryRegisterSerializer` and `TryRegisterClassMap` are used, so a consumer that configured its
own Guid serializer, or an `AbstractModel` class map that maps `Guid` as the id, before constructing a store
keeps its own choice. When the registration is refused because the slot is already taken by something that is
not a deliberate, compatible choice — the driver's own binary `GuidSerializer` with
`GuidRepresentation.Unspecified`, or an `AbstractModel` class map whose id member is not `Guid` — the system
SHALL throw `InvalidOperationException` instead of continuing.

#### Scenario: A model deriving MongoDBModel can be class-mapped

- **Given** a document type deriving `MongoDBModel` and a process in which `EnsureRegistered()` has run
- **When** `BsonSerializer.SerializerRegistry.GetSerializer<T>()` is called for it
- **Then** a serializer is returned — the class map freezes, because `MongoDBModel` declares no member that shadows `AbstractModel.Guid`

#### Scenario: The canonical identity is the document id, round-tripping as a string

- **Given** an entity whose `Guid` is set
- **When** it is serialized to BSON and deserialized back
- **Then** the `_id` element is a BSON string holding the Guid's text form, no separate `Guid` element is written, and the deserialized entity carries the same value

#### Scenario: A null canonical identity round-trips as null

- **Given** an entity whose `Guid` is null
- **When** it is serialized to BSON and deserialized back
- **Then** the `_id` element is BSON null and the deserialized entity's `Guid` is null

#### Scenario: An un-attributed Guid member serializes as standard binary

- **Given** a model carrying an additional `Guid` property with no `[BsonRepresentation]`
- **When** it is serialized to BSON
- **Then** that member is BSON binary of subtype `UuidStandard`, the representation `ChangeStreamDocumentKeyResolver` already assumes when reading a binary `_id`

#### Scenario: An unexpected element is refused rather than dropped

- **Given** a stored document carrying an element no member of the model maps
- **When** it is read back into the model type
- **Then** a `FormatException` is raised — no Birko entity silently drops data, and a model carrying `[BsonIgnoreExtraElements(false)]` is likewise honoured, because the class map sets no framework-wide `IgnoreExtraElements`

#### Scenario: A view projecting or filtering on the canonical id agrees with storage

- **Given** a `ViewDefinition` selecting an entity's `Guid` into a view property, which `MongoViewTranslator` rewrites to `_id`
- **When** the view is queried, and filtered on that property
- **Then** the projected value deserializes as the entity's `Guid` and the filter matches that entity, because `MongoViewSerialization` maps the view type to mirror the projection — the canonical-id property string-represented, no id member, and element names equal to property names

#### Scenario: Registration is idempotent across many stores

- **Given** a process that constructs several stores, each of which constructs a `MongoDBClient`
- **When** `EnsureRegistered()` runs on each construction
- **Then** only the first performs the registration and no duplicate-registration exception is raised

#### Scenario: A pre-cached broken default Guid serializer is refused, not kept

- **Given** a process in which the driver resolved and cached its own default `GuidSerializer` (binary representation, `GuidRepresentation.Unspecified`) before the first `MongoDBClient` was constructed
- **When** `EnsureRegistered()` runs and `TryRegisterSerializer` is refused
- **Then** `InvalidOperationException` is thrown naming the cause, because `IsBrokenDefaultGuidSerializer` treats a binary-encoded serializer with no representation chosen as never deliberate

#### Scenario: A consumer string Guid serializer is not mistaken for the broken default

- **Given** a consumer registered `new GuidSerializer(BsonType.String)` before the first store, which also reports `GuidRepresentation.Unspecified`
- **When** `EnsureRegistered()` runs
- **Then** no exception is thrown and the consumer's serializer stays in effect, because only a `BsonType.Binary` serializer with an unspecified representation counts as broken

#### Scenario: A pre-existing AbstractModel class map that disagrees on identity is refused

- **Given** an `AbstractModel` class map registered before the first store whose `IdMemberMap` is absent or names a member other than `Guid`
- **When** `EnsureRegistered()` runs and `TryRegisterClassMap` is refused
- **Then** `InvalidOperationException` is thrown; a pre-existing map whose id member is `Guid` is accepted silently

#### Scenario: A convention that unmaps the canonical id fails registration loudly

- **Given** a convention pack registered before the first store that filters `AbstractModel.Guid` out of `AutoMap`
- **When** the framework's class map is configured
- **Then** `InvalidOperationException` is thrown stating that `AbstractModel.Guid` has no member map, rather than registering a map without an id

### Requirement: MongoDBModel declares no state of its own

The system SHALL keep `MongoDBModel` free of declared members. It exists solely as the type constraint of the
synchronous `MongoDBStore<T>` and its repositories; any member it declares that shadows an `AbstractModel`
member makes the driver's class map unfreezable, and it does so at the first serialization attempt rather
than at compile time.

#### Scenario: The class contributes no declared property

- **Given** `typeof(MongoDBModel)` queried for public instance properties declared on that type only
- **When** the result is counted
- **Then** it is empty

### Requirement: MongoDBLogModel is a timestamped Mongo base outside the log-model hierarchy

The system SHALL provide `MongoDBLogModel` as an `abstract partial` class deriving `MongoDBModel` (not
`AbstractLogModel`) that declares `virtual` `CreatedAt` and `UpdatedAt` initialised to `DateTime.UtcNow` at
construction and a `virtual` nullable `PrevUpdatedAt` initialised to `null`, and that implements
`ICopyable<AbstractLogModel>` and `ILoadable<LogViewModel>` but neither `ITimestamped` nor `ILogEntity`.

#### Scenario: A freshly constructed Mongo log model is pre-stamped

- **Given** a concrete class deriving `MongoDBLogModel`
- **When** an instance is created with `new`
- **Then** `CreatedAt` and `UpdatedAt` hold the UTC construction time (not `0001-01-01` as on `AbstractLogModel`) and `PrevUpdatedAt` is `null`

#### Scenario: Timestamp infrastructure does not recognise it

- **Given** a `MongoDBLogModel` instance
- **When** it is tested with `is ITimestamped` or `is ILogEntity`
- **Then** both tests are false, because the class declares the three properties without implementing the contracts — so a decorator that stamps by testing for `ITimestamped` does not stamp it

#### Scenario: The typed copy transfers identity and timestamps onto an AbstractLogModel

- **Given** a `MongoDBLogModel` source and a non-null `AbstractLogModel` target
- **When** `source.CopyTo(target)` binds to `CopyTo(AbstractLogModel? clone = null)`
- **Then** `base.CopyTo(target)` copies `Guid`, then `CreatedAt`, `UpdatedAt` and `PrevUpdatedAt` are copied, and the target is returned

#### Scenario: The typed copy with no target throws

- **Given** a `MongoDBLogModel` source
- **When** `source.CopyTo((AbstractLogModel?)null)` is called
- **Then** `ArgumentNullException` is thrown by `AbstractModel.CopyTo` before any timestamp is touched (SH-M424; it used to return the null target)

#### Scenario: A base-typed copy moves only the identity

- **Given** a `MongoDBLogModel` source held as `AbstractModel`, and a target of the same concrete type
- **When** `CopyTo(target)` is called through the base reference
- **Then** only `Guid` is copied, because `MongoDBLogModel` does not override `CopyTo(AbstractModel?)` and its own type cannot be passed to the `AbstractLogModel` overload

#### Scenario: Loading from a LogViewModel takes identity and timestamps, tolerating null

- **Given** a `MongoDBLogModel` target and a `LogViewModel` source carrying `Guid`, `CreatedAt`, `UpdatedAt` and `PrevUpdatedAt`
- **When** `target.LoadFrom(source)` is called, and separately with a null `LogViewModel`
- **Then** all four values are loaded in the first case, and the null call throws nothing and leaves the target unmodified

### Requirement: SQL base models mark the identity as the primary key

The system SHALL provide `AbstractDatabaseModel` (`abstract partial`, deriving `AbstractModel`) and
`AbstractDatabaseLogModel` (`abstract`, deriving `AbstractLogModel` directly, not `AbstractDatabaseModel`),
each overriding `Guid` as a `Guid?` initialised to `null` and attributed `[UniqueField]` and `[PrimaryField]`, so
the SQL field mapper treats the identity as a unique primary-key column. `AbstractDatabaseLogModel` SHALL
also override `CreatedAt` and `UpdatedAt` with initial value `DateTime.UtcNow` and `PrevUpdatedAt` with `null`.

#### Scenario: The identity column is primary and unique

- **Given** a concrete class deriving `AbstractDatabaseModel` or `AbstractDatabaseLogModel`
- **When** the SQL field mapper loads its `Guid` property
- **Then** the field is created with `primary = true` and `unique = true` from the two attributes, as a Guid field

#### Scenario: An SQL log model is pre-stamped at construction

- **Given** a concrete class deriving `AbstractDatabaseLogModel`
- **When** an instance is created with `new`
- **Then** `CreatedAt` and `UpdatedAt` hold the UTC construction time, `PrevUpdatedAt` is `null`, and `Guid` is `null`

#### Scenario: Copy and load behaviour is inherited unchanged

- **Given** an `AbstractDatabaseLogModel` instance
- **When** `CopyTo` or `LoadFrom` is called
- **Then** the `AbstractLogModel` behaviour applies, because neither SQL base declares copy or load members

#### Scenario: The SQL log base is not an AbstractDatabaseModel

- **Given** an `AbstractDatabaseLogModel` instance
- **When** it is tested with `is AbstractDatabaseModel`
- **Then** the test is false — the two SQL bases are siblings that each re-declare the attributed `Guid`

### Requirement: SQL storage shape of shared domain value types

The system SHALL declare, for the SQL backends, `DatabaseValueData : ValueData` overriding `Price`, `PriceVAT`
and `VAT`, and `AbstractDatabasePercentage : AbstractPercentage` overriding `Percentage`, each attributed
`[PrecisionField(ValueData.StoreDecimalPrecision)]` (22) and `[ScaleField(ValueData.StoreDecimalPlaces)]` (6); and
`AbstractDatabaseTree : AbstractTree` overriding `Path` attributed `[PrecisionField(1024)]` with initial value
`string.Empty`. These three types live in the namespace `Birko.Models`.

#### Scenario: Monetary and percentage columns are DECIMAL(22,6)

- **Given** a `DatabaseValueData` or a concrete `AbstractDatabasePercentage`
- **When** the SQL field mapper loads `Price`, `PriceVAT`, `VAT` or `Percentage`
- **Then** a decimal field is created with precision 22 and scale 6 (nullable for the three `decimal?` prices, non-null for `Percentage`)

#### Scenario: A tree path is a bounded 1024-character column through the attribute

- **Given** a concrete `AbstractDatabaseTree`
- **When** the SQL field mapper loads `Path`, a `string` carrying `[PrecisionField(1024)]` and no `[MaxLengthField]`
- **Then** a bounded character field of length 1024 is created, because for a string the mapper falls back to `PrecisionField` when no max length is declared — the attribute path is not refused the way a fluent `HasPrecision` on a string is (TASK-692)

#### Scenario: A new SQL tree node has an empty, non-null path

- **Given** a concrete `AbstractDatabaseTree`
- **When** an instance is created with `new`
- **Then** `Path` is `string.Empty`, whereas the base `AbstractTree` initialises it to `null!`

### Requirement: Cosmos DB documents carry an id derived from the canonical identity

The system SHALL configure every Cosmos DB client created from `Settings.GetCosmosClientOptions()` with
`CosmosGuidIdSerializer`, which serializes with System.Text.Json (default options when none are given) and, on
write, adds a string `id` equal to `Guid.ToString()` to the JSON object of any `AbstractModel` whose `Guid` has a
value and whose serialized form has no non-null `id` — while still emitting the model's own `Guid` property
(CR-C04).

#### Scenario: A model with a Guid gains an id equal to it

- **Given** an `AbstractModel`-derived entity whose `Guid` is `g`
- **When** it is written through `CosmosGuidIdSerializer.ToStream`
- **Then** the JSON carries `"id": "<g as text>"` and also the PascalCase `Guid` property, so point operations keyed by `g.ToString()` against a `/id` partition key resolve

#### Scenario: A model without a Guid gets no id

- **Given** an `AbstractModel`-derived entity whose `Guid` is `null`
- **When** it is written
- **Then** no `id` is injected — the serializer does not assign an identity itself

#### Scenario: An existing id is not overwritten

- **Given** a model whose serialized form already has a non-null `id` element
- **When** it is written
- **Then** that `id` is kept unchanged

#### Scenario: Non-model payloads and streams pass through

- **Given** a payload that is not an `AbstractModel` (a query response, settings object), or a `Stream`
- **When** it is written
- **Then** the non-model is serialized normally with no `id` added, and a `Stream` input is returned as-is

#### Scenario: Reading ignores the injected id and tolerates an empty body

- **Given** a stored document carrying both `id` and `Guid`, or an empty seekable stream
- **When** `FromStream<T>` reads it
- **Then** the model deserializes with System.Text.Json and the extra `id` is ignored as an unmapped member; an empty seekable stream yields `default(T)`

#### Scenario: A Stream result is returned from inside the disposing block

- **Given** a `FromStream<T>` call where `T` is assignable to `Stream`
- **When** it returns
- **Then** the same stream object is returned, but from inside `using (stream)`, so it is disposed by the time the caller receives it
