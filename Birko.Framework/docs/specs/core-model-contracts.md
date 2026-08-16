---
area: core-model-contracts
generated-at: 8f57e5eec1505d1dc829e5a3be28e6f7eb449b93
generated-on: 2026-08-16
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
  - ../Birko.Data.MongoDB/Models/MongoDBModel.cs
  - ../Birko.Data.MongoDB/Serialization/MongoSerialization.cs
source-commits:   # sibling HEADs when this spec was last written (2026-08-16 16:17:32,
                  # commit c78cfca). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Contracts: dc3575c
  ../Birko.Data.Core: 1ee0793
  ../Birko.Data.MongoDB: 4f5e095
shaped-by: []
shaped-by-derived: false
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

Two structural facts shape everything below. First, all of these types live in the namespace
`Birko.Data.Models` even though they are split across two shared projects. Second, `AbstractLogModel` extends
`AbstractModel` by **adding overloads** rather than overriding, which makes the copy/load behaviour depend on
the *static* type of the reference through which it is invoked.

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

### Requirement: Omitted copy target returns the source itself

The system SHALL, when `CopyTo` is called with a null or omitted target, return `this` — the source instance —
rather than allocating a new instance.

#### Scenario: CopyTo() with no argument yields a self-reference

- **Given** a model instance `src` deriving from `AbstractModel`
- **When** `var result = src.CopyTo();` is evaluated
- **Then** `ReferenceEquals(result, src)` is `true`, because the implementation begins `if (clone == null) { return this; }`

#### Scenario: Mutating the "copy" mutates the original

- **Given** `AbstractLogModel alias = src.CopyTo(null);`
- **When** `alias.UpdatedAt` is assigned `2099-01-01`
- **Then** `src.UpdatedAt` also reads `2099-01-01`, because `alias` and `src` are the same object — no defensive copy was made

#### Scenario: The documented nullability contract disagrees with the code

- **Given** the XML documentation on `ICopyable<T>.CopyTo` which states that when no target is supplied implementations "allocate a fresh instance"
- **When** `AbstractModel.CopyTo(null)` and `AbstractLogModel.CopyTo(null)` are executed
- **Then** neither allocates; both return `this`

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
- **Then** it compiles without ambiguity and binds to the `AbstractLogModel` overload, because overload resolution discards candidates declared in a base type of an applicable derived candidate

### Requirement: Copy dispatch depends on the static reference type

The system SHALL declare `AbstractLogModel.CopyTo` as a **new overload** taking `AbstractLogModel?`, not as an
override of the `virtual` `AbstractModel.CopyTo(AbstractModel?)`. Consequently, invoking `CopyTo` through a
reference whose static type is `AbstractModel` or `ICopyable<AbstractModel>` SHALL execute the base
implementation and copy only `Guid`, silently leaving the target's timestamps at their prior values.

#### Scenario: Copying through a base-typed reference drops all timestamps

- **Given** `AbstractModel asBase = logModelWithCreatedAt2020;` and an empty target of the same concrete type
- **When** `asBase.CopyTo(target)` is called
- **Then** `target.Guid` is populated but `target.CreatedAt` and `target.UpdatedAt` remain `0001-01-01` — no exception, no warning, and the loss is not observable at the call site

#### Scenario: Copying through the generic interface drops all timestamps

- **Given** `ICopyable<AbstractModel> ic = logModel;` where `logModel` also implements `ICopyable<AbstractLogModel>`
- **When** `ic.CopyTo(target)` is called with an `AbstractLogModel` target
- **Then** `target.CreatedAt` remains `0001-01-01`, because `ICopyable<AbstractModel>` is satisfied by the base method and the derived overload never participates

#### Scenario: The same call through the derived type is correct

- **Given** the identical source and target as the two preceding scenarios
- **When** `CopyTo(target)` is called through an `AbstractLogModel`-typed reference
- **Then** the timestamps do transfer — so the observable outcome of `CopyTo` is determined by the compile-time type of the receiver, not the runtime type

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

### Requirement: Load dispatch depends on the static reference type

The system SHALL declare `AbstractLogModel.LoadFrom(ILogEntity)` as a new overload rather than an override of
the `virtual` `AbstractModel.LoadFrom(IGuidEntity)`. Consequently, loading through an
`ILoadable<IGuidEntity>`-typed reference SHALL copy only `Guid` and SHALL discard the source's timestamps even
when the argument object carries them.

#### Scenario: Timestamps present on the argument are dropped by interface dispatch

- **Given** `ILoadable<IGuidEntity> il = target;` where `target` derives from `AbstractLogModel`, and a source that is itself an `ILogEntity` with `CreatedAt` = `2020-01-01`
- **When** `il.LoadFrom(source)` is called
- **Then** `target.Guid` is populated but `target.CreatedAt` remains `0001-01-01` — the runtime type of the argument does carry the timestamps, but the selected method has no code to read them

#### Scenario: A log model exposes two distinct load contracts simultaneously

- **Given** `AbstractLogModel` which implements both `ILoadable<IGuidEntity>` (inherited from `AbstractModel`) and `ILoadable<ILogEntity>` (declared on itself)
- **When** a generic caller resolves `ILoadable<T>` for such a model
- **Then** two different implementations are reachable with different fidelity, and only the `ILoadable<ILogEntity>` path preserves timestamps

### Requirement: Contract types share one namespace across two projects

The system SHALL declare every type in this capability in the namespace `Birko.Data.Models`, and the compiled
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
own Guid serializer or `AbstractModel` class map before constructing a store keeps its own choice.

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

### Requirement: MongoDBModel declares no state of its own

The system SHALL keep `MongoDBModel` free of declared members. It exists solely as the type constraint of the
synchronous `MongoDBStore<T>` and its repositories; any member it declares that shadows an `AbstractModel`
member makes the driver's class map unfreezable, and it does so at the first serialization attempt rather
than at compile time.

#### Scenario: The class contributes no declared property

- **Given** `typeof(MongoDBModel)` queried for public instance properties declared on that type only
- **When** the result is counted
- **Then** it is empty
