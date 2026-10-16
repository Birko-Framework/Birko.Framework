---
area: schema-index-and-ddl
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.Data.CosmosDB/IndexManagement/CosmosDBIndexManager.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/IndexInfo.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs
  - ../Birko.Data.MongoDB/IndexManagement/MongoDBIndexManager.cs
  - ../Birko.Data.Patterns/IndexManagement/IIndexManager.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexDeclarationDrift.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexDefinition.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexInfo.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexManagementException.cs
  - ../Birko.Data.Patterns/Schema/FieldDescriptor.cs
  - ../Birko.Data.Patterns/Schema/FieldType.cs
  - ../Birko.Data.Patterns/Schema/ICollectionBuilder.cs
  - ../Birko.Data.Patterns/Schema/IIndexBuilder.cs
  - ../Birko.Data.Patterns/Schema/ISchemaBuilder.cs
  - ../Birko.Data.Patterns/Schema/IndexBuilderSupport.cs
  - ../Birko.Data.Patterns/Schema/SchemaBuildLedger.cs
  - ../Birko.Data.RavenDB/IndexManagement/RavenDBIndexManager.cs
  - ../Birko.Data.SQL.MSSql/IndexManagement/MSSqlIndexManager.cs
  - ../Birko.Data.SQL.MySQL/IndexManagement/MySqlIndexManager.cs
  - ../Birko.Data.SQL.PostgreSQL/IndexManagement/PostgreSqlIndexManager.cs
  - ../Birko.Data.SQL.SqLite/IndexManagement/SqLiteIndexManager.cs
  - ../Birko.Data.SQL/Attributes/Field.cs
  - ../Birko.Data.SQL/Attributes/Table.cs
  - ../Birko.Data.SQL/SQL/DataBase_Field.cs
  - ../Birko.Data.SQL/SQL/DataBase_Table.cs
  - ../Birko.Data.SQL/SQL/Fields/AbstractField.cs
  - ../Birko.Data.SQL/SQL/Fields/BinaryField.cs
  - ../Birko.Data.SQL/SQL/Fields/BooleanField.cs
  - ../Birko.Data.SQL/SQL/Fields/CharField.cs
  - ../Birko.Data.SQL/SQL/Fields/DateTimeField.cs
  - ../Birko.Data.SQL/SQL/Fields/DecimalField.cs
  - ../Birko.Data.SQL/SQL/Fields/DoubleField.cs
  - ../Birko.Data.SQL/SQL/Fields/FloatField.cs
  - ../Birko.Data.SQL/SQL/Fields/GuidField.cs
  - ../Birko.Data.SQL/SQL/Fields/IntegerField.cs
  - ../Birko.Data.SQL/SQL/Fields/LongField.cs
  - ../Birko.Data.SQL/SQL/Fields/ShortField.cs
  - ../Birko.Data.SQL/SQL/Fields/StringField.cs
  - ../Birko.Data.SQL/SQL/Fields/TimeOnlyField.cs
  - ../Birko.Data.SQL/SQL/Fields/UtcDateTimeField.cs
  - ../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs
  - ../Birko.Data.SQL/SQL/Tables/IndexDefinition.cs
  - ../Birko.Data.SQL/SQL/Tables/Table.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Schema descriptors, index management and attribute-driven DDL

## Purpose

This capability is how Birko learns the *shape* of stored data and how it manages the physical structures
that make queries fast. It has three layers that meet but do not fully overlap.

First, a **backend-agnostic vocabulary** (`Birko.Data.Patterns.Schema` and
`Birko.Data.Patterns.IndexManagement`): a small set of portable descriptors — a field with a portable
`FieldType`, an index with named fields and flags, and fluent builder interfaces a migration uses to say
"create this collection / this index" without naming a database. Migration backends implement those
builders; anything that wants to inspect or manipulate indexes at runtime talks to `IIndexManager`.

Second, **attribute-driven SQL discovery**: `Birko.Data.SQL.DataBase` reflects over a model type, reads
`[Table]` / `[NamedField]` / `[PrimaryField]` / `[IndexedField]` / `[CompositeIndex]` (and a handful of
`System.ComponentModel.DataAnnotations` equivalents), and produces a cached `Tables.Table` with typed
`AbstractField` columns plus a dictionary of `IndexDefinition`s. Everything the SQL stores do — SELECT
column lists, INSERT/UPDATE parameter binding, reader materialisation, CREATE TABLE / CREATE INDEX DDL —
is driven off that one reflected model. Because framework projects are *shared projects*, the same
attribute type can be compiled into several assemblies, so every attribute read has a
reflection-by-full-name fallback next to the direct cast.

Third, **provider index managers**: one `SqlIndexManager` with per-dialect catalog queries (MSSQL, MySQL,
PostgreSQL, SQLite) and an ElasticSearch pair where "index" means a *container*, not a secondary index —
`IndexManager` (native: aliases, templates, settings, refresh/flush) plus
`ElasticSearchIndexManagerAdapter` that squeezes it into the portable `IIndexManager` shape, and
`ReindexHelper` for server-side and zero-downtime alias-swap reindexing. The document backends carry their
own `IIndexManager` implementations — `MongoDBIndexManager`, `RavenDBIndexManager` and
`CosmosDBIndexManager` — each translating the portable definition into that backend's native index shape.

The layers are deliberately *not* isomorphic, and the divergences below are load-bearing: the portable
`FieldType` enum names types the SQL field factory cannot map, and the portable `IndexDefinition` carries
flags several providers cannot honour and therefore refuse.

## Requirements

### Requirement: Portable field descriptor

The system SHALL expose `FieldDescriptor` as a mutable, backend-neutral description of one column/field,
carrying name, `FieldType`, optional physical column name, the flags `IsPrimary` / `IsUnique` /
`IsRequired` / `IsIgnored` / `IsAutoIncrement`, the optional sizing hints `MaxLength` / `Precision` /
`Scale`, a `DefaultValue`, and inline index participation via `IndexName` / `IndexOrder` /
`IndexDescending`; all flags SHALL default to `false`, all nullable hints to `null`, and `IndexOrder` to `0`.

#### Scenario: Default-constructed descriptor

- **Given** `new FieldDescriptor()`
- **When** its properties are inspected without assignment
- **Then** `IsPrimary`, `IsUnique`, `IsRequired`, `IsIgnored`, `IsAutoIncrement` and `IndexDescending` are
  `false`, `ColumnName` / `MaxLength` / `Precision` / `Scale` / `DefaultValue` / `IndexName` are `null`,
  `IndexOrder` is `0`, and `Type` is `FieldType.String` (the zero value of the enum)

#### Scenario: Name-only construction

- **Given** the convenience constructor
- **When** `new FieldDescriptor("Number")` is created
- **Then** `Name` is `"Number"` and every other property retains its default

### Requirement: Portable field type vocabulary

The system SHALL define the portable field types as exactly `String`, `Integer`, `Long`, `Decimal`,
`Double`, `Boolean`, `DateTime`, `Guid`, `Binary`, `Json`, in that declaration order, with `String` as the
default (zero) value.

#### Scenario: String is the default

- **Given** a `FieldDescriptor` whose `Type` was never assigned
- **When** `Type` is read
- **Then** it equals `FieldType.String`

#### Scenario: Portable vocabulary is matched by the SQL mapper except for Json

- **Given** `FieldType.Long`, `FieldType.Double`, `FieldType.Binary` and `FieldType.Json` exist in the
  portable enum
- **When** the corresponding CLR types (`long`, `double`, `byte[]`, an object graph) appear as properties on
  a `[Table]`-annotated model handed to `AbstractField.CreateAbstractField`
- **Then** `long`, `double` and `byte[]` produce `LongField`, `DoubleField` and `BinaryField` carrying
  `DbType.Int64`, `DbType.Double` and `DbType.Binary` — the same `DbType`s `SchemaField.MapFieldType`
  assigns to `FieldType.Long` / `.Double` / `.Binary`, so the portable vocabulary and the attribute-driven
  mapper now agree — while an object graph (`FieldType.Json`) has no CLR-type arm and raises
  `FieldAttributeException` at table load

### Requirement: Schema builder surface

The system SHALL expose `ISchemaBuilder` as the migration-facing DDL surface with exactly these operations:
`CreateCollection(name)` returning an `ICollectionBuilder`, `DropCollection(name)`, `CollectionExists(name)`,
`CreateIndex(collectionName, indexName)` returning an `IIndexBuilder`, `DropIndex(collectionName, indexName)`,
`AddField(collectionName, FieldDescriptor)`, `DropField(collectionName, fieldName)` and
`RenameField(collectionName, oldName, newName)`; all are synchronous and none returns a result object. It
SHALL additionally expose the read-only `PendingBuilds` — the `CreateCollection` / `CreateIndex` chains the
builder handed out and never finished with `Build()` — as a default interface member returning an empty list
for a builder that does not track them.

#### Scenario: Collection creation is a builder chain, not a single call

- **Given** an `ISchemaBuilder` implementation
- **When** `CreateCollection("Invoice")` is called
- **Then** an `ICollectionBuilder` is returned and no field information has yet been supplied

#### Scenario: Field mutation is direct, not chained

- **Given** an existing collection
- **When** `AddField("Invoice", new FieldDescriptor("Note") { Type = FieldType.String })` is called
- **Then** the operation returns `void` — there is no builder to terminate

### Requirement: Builder chains require a terminal Build()

The system SHALL declare `Build()` on both `ICollectionBuilder` and `IIndexBuilder` as a **default interface
method with an empty body**, so providers that create eagerly inherit a no-op while providers that must know
the whole definition first (SQL, which emits a single `CREATE TABLE` / `CREATE INDEX`) override it; a chain
left unterminated by `Build()` therefore SHALL perform no work on those providers, and is detected only through
a builder that tracks its chains in a `SchemaBuildLedger` and reports them in `PendingBuilds`.

#### Scenario: Eager provider needs no terminator

- **Given** a schemaless provider whose `CreateCollection` already created the container
- **When** a migration calls `CreateCollection("x").WithField("a", FieldType.String)` and never calls `Build()`
- **Then** the collection still exists, because the inherited `Build()` is a no-op

#### Scenario: Deferred provider does nothing without Build(), and a tracking builder reports it

- **Given** a provider that overrides `Build()` to emit the accumulated DDL
- **When** a migration calls `CreateIndex("Invoice", "IX_Number").WithField("Number")` and omits `Build()`
- **Then** no `CREATE INDEX` is emitted and the call returns normally; a builder that tracks its chains lists
  `CreateIndex("Invoice", "IX_Number")` in `PendingBuilds`, which is how a migration runner refuses to record
  the migration, while a builder that does not track keeps the default empty `PendingBuilds` and the omission
  stays undetected

#### Scenario: Index builder accumulators

- **Given** an `IIndexBuilder`
- **When** `WithField(name, descending, fieldType)`, `Unique()`, `Sparse()` and `WithProperty(key, value)`
  are called
- **Then** each returns the same builder type for chaining, and `WithField` defaults to
  `descending: false` and `fieldType: IndexFieldType.Standard`

### Requirement: A build ledger records which chains were finished

The system SHALL provide `SchemaBuildLedger`, which wraps an `IIndexBuilder` (`Track(inner, collectionName,
indexName)`) or an `ICollectionBuilder` (`Track(inner, collectionName)`) in a tracking builder that forwards every
accumulator call to the inner builder and returns itself for chaining, and SHALL report in `Pending` the
description — `CreateIndex("<collection>", "<index>")` or `CreateCollection("<collection>")` — of every tracked
chain whose `Build()` was never called. The tracking builder's `Build()` SHALL mark the chain built **before**
invoking the inner `Build()`, so a `Build()` that throws is not also reported pending. A collection chain SHALL be
pending until built even on a provider whose `CreateCollection` creates eagerly, so the rule is the same on every
provider. Recording and reading are synchronised on one lock.

#### Scenario: An unbuilt chain is pending, a built one is not

- **Given** a ledger tracking `CreateIndex("Invoice", "IX_Number")` and `CreateCollection("Audit")`
- **When** only the collection chain calls `Build()`
- **Then** `Pending` is exactly `["CreateIndex(\"Invoice\", \"IX_Number\")"]`

#### Scenario: A collection created eagerly is still pending without Build()

- **Given** a provider whose `CreateCollection` already created the container, its builder tracked by a ledger
- **When** the migration calls `.WithField(...)` and never `Build()`
- **Then** the collection exists and the chain is nevertheless listed in `Pending`

#### Scenario: A failing Build is not pending

- **Given** a tracked index chain whose inner `Build()` throws
- **When** `Build()` is called
- **Then** the inner exception propagates and the chain is absent from `Pending`

### Requirement: Index builder refusals have one producer

The system SHALL provide `IndexBuilderSupport` as the single producer of the refusals an index builder or
index lane raises when it cannot honour part of a declaration: `Unsupported(backend, what, why, instead)`
SHALL return a `NotSupportedException` whose message is `"{backend}: {what} cannot be honoured here —
{why}. {instead}"`, and `NotImplementedHere(backend, indexName, instead)` SHALL return a
`NotSupportedException` stating that index `'{indexName}'` was declared through a migration but the
backend's schema builder does not create indexes and would silently do nothing, followed by `instead`.
Both return the exception for the caller to throw.

#### Scenario: A refusal names the backend, the declaration, the reason and the way out

- **Given** `IndexBuilderSupport.Unsupported("SQL", "a TTL index ('IX_A', ExpireAfter = 01:00:00)", "SQL has no TTL index", "Delete expired rows on a schedule instead.")`
- **When** its `Message` is read
- **Then** it is `"SQL: a TTL index ('IX_A', ExpireAfter = 01:00:00) cannot be honoured here — SQL has no TTL
  index. Delete expired rows on a schedule instead."`

### Requirement: Portable index definition and field factories

The system SHALL describe an index portably as `IndexDefinition` with `Name`, an `IReadOnlyList<IndexField>`
defaulting to an **empty array**, the flags `Unique` and `Sparse`, an optional `ExpireAfter` TTL, and an
optional provider-specific `Properties` dictionary; and SHALL provide the static `IndexField` factories
`Ascending`, `Descending`, `Text`, `Hashed` and `Geo2dSphere`.

#### Scenario: Fields default to empty, not null

- **Given** `new IndexDefinition { Name = "IX_A" }`
- **When** `Fields` is read
- **Then** it is an empty list, so a definition can be inspected without a null check

#### Scenario: Direction and specialised types come from factories

- **Given** the `IndexField` factory methods
- **When** `IndexField.Descending("CreatedAt")` and `IndexField.Geo2dSphere("Location")` are created
- **Then** the first has `IsDescending = true` and `FieldType = IndexFieldType.Standard`, and the second has
  `IsDescending = false` and `FieldType = IndexFieldType.Geo2dSphere`

#### Scenario: Specialised field types are declared but unused by the SQL and ElasticSearch managers

- **Given** `IndexFieldType` declares `Standard`, `Text`, `Geo2d`, `Geo2dSphere` and `Hashed`
- **When** such a definition is passed to `SqlIndexManager.CreateAsync` or
  `ElasticSearchIndexManagerAdapter.CreateAsync`
- **Then** `FieldType` is ignored by both — SQL emits a plain column list, ES ignores `Fields` entirely

### Requirement: Portable index info defaults

The system SHALL report an existing index portably as `IndexInfo` with `Fields` defaulting to an empty
array, `SizeInBytes` defaulting to `-1` to mean "not available", `State` defaulting to the string `"ready"`,
and `Properties` defaulting to an empty dictionary.

#### Scenario: SQL never populates size or state

- **Given** a table with indexes on any SQL provider
- **When** `SqlIndexManager.ListAsync(tableName)` returns
- **Then** every `IndexInfo` has `SizeInBytes == -1` and `State == "ready"` — neither is queried from the
  catalog, they are the class defaults

### Requirement: Index management failures carry index and scope

The system SHALL raise `IndexManagementException` for index-management failures, exposing the nullable
`IndexName` and `Scope` of the failed operation; the message-only and message-plus-inner constructors SHALL
leave both properties `null`.

#### Scenario: Wrapped SQL failure names the index and table

- **Given** a `CREATE INDEX` that the database rejects
- **When** `SqlIndexManager.CreateAsync(definition, "Invoice")` runs
- **Then** an `IndexManagementException` is thrown with message
  `"Failed to create index '<name>' on table 'Invoice'."`, `IndexName` set to the definition name, `Scope`
  set to `"Invoice"`, and the provider exception as `InnerException`

#### Scenario: Message-only construction leaves context null

- **Given** `new IndexManagementException("boom")`
- **When** `IndexName` and `Scope` are read
- **Then** both are `null`

### Requirement: Scope means a different thing per provider

The system SHALL define `IIndexManager`'s `scope` parameter as provider-interpreted: SQL SHALL require it
(the table name) and reject a null/whitespace scope; ElasticSearch SHALL ignore it entirely; and per the
interface contract RavenDB SHALL ignore it because its indexes are database-wide.

#### Scenario: SQL rejects a missing scope

- **Given** a `SqlIndexManager`
- **When** `ExistsAsync("IX_A")`, `CreateAsync(def)`, `DropAsync("IX_A")` or `ListAsync()` is called with a
  null, empty or whitespace `scope`
- **Then** an `ArgumentException` with message
  `"Table name (scope) is required for SQL index management."` and parameter name `scope` is thrown before
  any connection is opened

#### Scenario: ElasticSearch accepts and discards any scope

- **Given** an `ElasticSearchIndexManagerAdapter`
- **When** `ExistsAsync("invoices", scope: "anything")` is called
- **Then** the scope is never read and existence is checked for the ES index `"invoices"`

### Requirement: Declared indexes are compared with what an index manager reports

The system SHALL provide `IndexDeclarationDriftDetector.DetectAsync(manager, declared, scope, ct)`, which looks
up every declared `IndexDefinition` with the manager's own `GetInfoAsync(name, scope)` and returns an
`IndexDeclarationReport` carrying the scope, the declared count, the `Drifts` and the `Unverified` entries; it
SHALL create nothing. A null manager or declared list SHALL throw `ArgumentNullException`, and a declared entry
that is null or has a blank name SHALL throw `ArgumentException("Every declared index needs a name.")`. Per
declared index it SHALL record: `Missing` when the lookup answers null; `Unverified` ("the lookup failed: …") when
the lookup throws anything but `OperationCanceledException`, which propagates; `Unverified` ("the store reports no
fields for it") when the stored index has no fields; `ColumnsDiffer` when the field lists differ in count or, by
position, in ordinal name, direction or `FieldType`; and, independently of the field comparison, `UniquenessDiffers`
when `Unique` disagrees. `IsClean` SHALL be true only when there are neither drifts nor unverified entries. Each
shape SHALL be described as `[UNIQUE ](Name[ DESC][ <FieldType>], …)`, with the field type shown only when not
`Standard`.

#### Scenario: An absent index is missing

- **Given** a declared `IX_Code` over `Code` that the manager's `GetInfoAsync` answers null for
- **When** `DetectAsync` runs
- **Then** the report holds one `Missing` drift whose `Declared` is `(Code)` and `Stored` is null, and `IsClean`
  is false

#### Scenario: Uncomparable is never clean

- **Given** a declared index the store reports with no fields (a RavenDB map it did not generate, a Cosmos path no
  name registered), or a lookup that throws
- **When** `DetectAsync` runs
- **Then** the index is listed in `Unverified` with the reason and not in `Drifts`, and `IsClean` is false

#### Scenario: Uniqueness is compared even when the fields could not be

- **Given** a declared unique index whose stored entry reports no fields and `Unique == false`
- **When** `DetectAsync` runs
- **Then** it is listed both in `Unverified` and as a `UniquenessDiffers` drift

### Requirement: SQL table discovery from attributes

The system SHALL resolve a model type to a `Tables.Table` by scanning the type's attributes (including
inherited ones) for `Birko.Data.SQL.Attributes.Table`, `System.ComponentModel.DataAnnotations.Schema.TableAttribute`,
or **any attribute whose full type name is `"Birko.Data.SQL.Attributes.Table"`** (the cross-assembly
shared-project case, read via reflection), taking the first attribute that yields a non-empty name; and
SHALL fall back to a name registered through `RegisterTableName` only when no such attribute produced a
table. `LoadTable` SHALL throw `Exceptions.TableAttributeException` for a type that maps to no table, and
`TryLoadTable` SHALL answer `null` for it; both answers SHALL be cached.

#### Scenario: Birko attribute wins directly

- **Given** `[Table("Invoice")] class Invoice` with at least one mappable property
- **When** `DataBase.LoadTable(typeof(Invoice))` is called
- **Then** a `Table` with `Name == "Invoice"`, `Type == typeof(Invoice)` and a `Fields` dictionary keyed by
  column name is returned, and every field's `Table` back-reference points at that table

#### Scenario: Cross-assembly attribute identity is handled by name

- **Given** the `Table` attribute compiled into a *different* assembly (shared-project duplication), so the
  direct `is Birko.Data.SQL.Attributes.Table` test fails
- **When** `LoadTable` runs
- **Then** the attribute is still matched by `GetType().FullName == "Birko.Data.SQL.Attributes.Table"` and
  its `Name` and `Schema` properties are read reflectively

#### Scenario: Fluent registration is a fallback, not an override

- **Given** `[Table("Invoice")] class Invoice` and a prior call to
  `RegisterTableName(typeof(Invoice), "tbl_invoice")`
- **When** `LoadTable(typeof(Invoice))` is called
- **Then** the resulting table is named `"Invoice"` — the attribute branch returns first and the override is
  never consulted

#### Scenario: Registration invalidates the cache for that type only

- **Given** `LoadTable(typeof(Foo))` has already cached a table
- **When** `RegisterTableName(typeof(Foo), "other")` is called
- **Then** the entry is removed from `_tableCache` — and so is a cached "maps to no table" answer and the
  type's recorded schema — so the next `LoadTable` recomputes, while the separate `_fieldsCache` entry for
  `Foo` is left intact

#### Scenario: An unmapped type is refused by LoadTable and answered null by TryLoadTable

- **Given** a type with neither a table attribute nor a registered name (or one whose `Fields` come out
  empty)
- **When** `DataBase.LoadTable(type)` is called
- **Then** a `TableAttributeException` is thrown stating that the type maps to no table and must carry a
  `[Table("name")]` attribute (or a registered name) and at least one mapped property
- **And** `DataBase.TryLoadTable(type)` returns `null`, and that answer is remembered in `_unmappedTypes` so the
  type is not recomputed until a `RegisterTableName` for it clears the entry

#### Scenario: Duplicate column names abort table load

- **Given** two properties on the same model that map to the same column name (e.g. `[NamedField("Code")]`
  on one and a property literally named `Code` on the other)
- **When** `LoadTable` builds `LoadFields(type).ToDictionary(x => x.Name)`
- **Then** an `ArgumentException` for the duplicate key propagates out of table load

### Requirement: Bulk table load rejects an empty input but tolerates unmapped types

The system SHALL throw `Exceptions.TableAttributeException("Types enumerable is empty or null")` when
`LoadTables` is given a null or empty sequence, and SHALL silently omit any supplied type that resolves to
no table or to a table with no fields.

#### Scenario: Empty input is an error

- **Given** an empty `IEnumerable<Type>`
- **When** `DataBase.LoadTables(types)` is called
- **Then** a `TableAttributeException` is thrown

#### Scenario: Unmapped members of a non-empty list are dropped without notice

- **Given** `LoadTables(new[] { typeof(Invoice), typeof(PlainPoco) })` where `PlainPoco` has no table
  attribute
- **When** the call completes
- **Then** the returned array contains only the `Invoice` table and no error is reported

### Requirement: An entity's table can live in a database schema

The system SHALL read a table's schema from `Birko.Data.SQL.Attributes.Table.Schema`, from DataAnnotations'
`TableAttribute.Schema`, from a cross-assembly `Table` attribute's `Schema` property, or — for a registered name —
from `RegisterTableName(type, name, schema)` (the two-argument overload registering no schema), and SHALL carry it
on `Tables.Table.Schema` while `Tables.Table.Name` stays the **logical** name every column qualifier resolves
against; an empty schema SHALL mean none. A schema SHALL be a plain identifier — an ASCII letter or underscore,
then ASCII letters, digits or underscores — else `TableAttributeException`. Each loaded table SHALL be recorded by
logical name (case-insensitively) so `DataBase.TableSchema(name)` answers the schema a loaded entity declared, or
null; and table load SHALL refuse with `TableAttributeException` a logical name declared in two different schemas
by two types, and two types whose tables would be the same relation where a schema is stored as the
`DataBase.PrefixedTableName` form `schema__name` (MySQL, SQLite), checked on every provider at once.

#### Scenario: Both attribute families carry the schema

- **Given** `[Table("evts", Schema = "reporting")]` on one entity and DataAnnotations'
  `[Table("evts2", Schema = "reporting")]` on another
- **When** the tables are loaded
- **Then** both have `Schema == "reporting"`, their `Name` is the declared logical name, and
  `DataBase.TableSchema("evts")` answers `"reporting"`

#### Scenario: A schema that is not a plain identifier is refused

- **Given** `[Table("evts", Schema = "rep-orting")]`
- **When** the table is loaded
- **Then** `TableAttributeException` is thrown stating that the schema is not a plain identifier

#### Scenario: A prefixed name colliding with a schema-qualified one is refused

- **Given** one entity declared as `reporting.evts` and another declared as the plain table `reporting__evts`
- **When** the second is loaded
- **Then** `TableAttributeException` is thrown naming both types and the table `reporting__evts` they would share
  on MySQL and SQLite

#### Scenario: One logical name in two schemas is refused

- **Given** two types both declaring logical name `evts`, one in schema `a` and one in schema `b`
- **When** the second is loaded
- **Then** `TableAttributeException` is thrown, because the logical name is the alias its columns are qualified
  with and can live in one schema only

### Requirement: A migration's column name resolves through the loaded entity

The system SHALL expose `DataBase.TableNamed(name)`, answering the loaded table whose logical name is `name` (or
null), and `DataBase.MigrationColumn(tableName, name)`, answering the stored column name of the mapped field whose
property name — else whose column name — equals `name` case-insensitively, and otherwise `name` itself after
`ValidateColumnIdentifier` accepts it as a plain identifier.

#### Scenario: A property name finds its remapped column

- **Given** a loaded table `Invoice` whose property `Number` carries `[NamedField("inv_no")]`
- **When** `MigrationColumn("Invoice", "Number")` is called
- **Then** `"inv_no"` is returned

#### Scenario: A name nothing maps is validated, not quoted

- **Given** no loaded entity named `Legacy`
- **When** `MigrationColumn("Legacy", "Col; DROP")` is called
- **Then** `ValidateColumnIdentifier` refuses the name with `ArgumentException`; a plain name such as `"Col"` is
  returned unchanged

### Requirement: Table metadata helpers

The system SHALL expose on `Tables.Table` a positional select-field map (`GetSelectFields`), primary-field
enumeration, column-name lookup (`GetField`) and property-name lookup (`GetFieldByPropertyName`) backed by a
lazily built reverse index, and SHALL render aggregate fields as `FUNC(args) as <ViewProperty>` while
rendering plain fields as an optionally table-qualified column name.

The aggregate alias SHALL be taken from `AbstractField.Property.Name` — the aggregate's view property —
falling back to the `Fields` dictionary key only when `Property` is unset. It is the same name
`View.GetPersistentViewSelectFields()` queries back and `DataBase.ViewOrderFieldName()` sorts by; all three
SHALL read it from that one place, so they agree by construction rather than by each view builder keying the
field identically.

The system SHALL additionally accept an `aggregateAlias` flag (default true) that suppresses the
`as <alias>` suffix entirely. The view-DDL builder passes false and appends its own **quoted** alias, because
the column it creates is read back quoted; with both emitting, an aggregate carried two aliases and the
statement was a syntax error on every provider.

The system SHALL further accept an optional `quoteTable` delegate on `AbstractField.GetSelectName` (and
through `GetSelectFields`) that quotes the **table** half of a qualified reference, defaulting to null,
which keeps the historical bare form. The **column** half SHALL stay bare in every case.

That asymmetry is the framework-wide identifier rule, not a local choice: `CreateTable` quotes the table
name and emits column definitions **bare**, so on PostgreSQL — the one supported provider that case-folds
an unquoted identifier — every base column is stored folded while every table keeps its declared case. A
column reference must therefore be bare to resolve, and a table reference quoted. Before the delegate
existed the view DDL emitted `AvPersons.Name` against a `FROM "AvPersons"`, PostgreSQL folded the
qualifier to `avpersons`, and the statement failed with *missing FROM-clause entry for table "avpersons"*
— measured against PostgreSQL 16.4.

> The read path solves the same disagreement differently, and deliberately: `CreateSelectCommand` emits
> `FROM "T" AS T`, so a *bare* qualifier resolves against the alias. A delegate works for the view DDL
> because that projection has one metadata-driven producer; the read path's qualifiers can arrive
> function-wrapped (`LOWER(T.Col)`, `COALESCE`, the `.Date` rewrite), each its own producer, so quoting
> them one by one would mean enumerating producers and a missed one is a silent empty result.

#### Scenario: Select list is keyed by ordinal position

- **Given** a table with three fields
- **When** `GetSelectFields()` is called
- **Then** the returned dictionary has keys `0`, `1`, `2` in `Fields.Keys` order, so a reader can be read
  positionally

#### Scenario: Aggregate fields are aliased by their view property, plain fields are not

- **Given** an aggregate field (`IsAggregate == true`) whose `Property` is the view property `TotalSpent`,
  stored under the `Fields` key `"SUM"`
- **When** `GetSelectFields(withName: true)` is called
- **Then** its value ends with `" as TotalSpent"` — the property, not the key — whereas a non-aggregate
  field's value is `"<TableName>.<ColumnName>"` with no alias

#### Scenario: The table half of a qualifier is quoted only when a delegate asks for it

- **Given** a plain field on table `AvPersons` mapped to column `Name`
- **When** `GetSelectName(withName: true)` is called with no `quoteTable`, and again with a delegate that
  applies the provider's quoting
- **Then** the first yields `AvPersons.Name` and the second `"AvPersons".Name` — the column half is bare in
  both, because base-table DDL created that column bare and a quoted column name would be a case-sensitive
  miss on PostgreSQL

#### Scenario: A function field's parameters take the same qualifier treatment

- **Given** an aggregate `FunctionField` whose parameters are column names on the same table
- **When** `GetSelectName(withName: true, quoteTable: q)` is called
- **Then** every parameter carries the same quoted-table/bare-column prefix as a plain field, rather than
  the prefix being applied to the outer reference only

#### Scenario: An aggregate field with no view property falls back to its dictionary key

- **Given** an aggregate field whose `Property` is unset
- **When** `GetSelectFields(withName: true)` is called
- **Then** the alias is the `Fields` dictionary key, so the read-path projection is still valid SQL rather
  than raising `NullReferenceException`. This covers `GetSelectFields` only — the view-DDL builder
  dereferences `Property.Name` unconditionally when appending its own alias, so a `Property`-less aggregate
  still raises there (pre-existing; the view builders always assign `Property`)

#### Scenario: Aggregate-only omission

- **Given** a table mixing aggregate and plain fields
- **When** `GetSelectFields(notAggregate: true)` or `GetTableFields(notAggregate: true)` is called
- **Then** aggregate fields are excluded, and the ordinal keys of the remaining fields keep their original
  positions (they are not renumbered)

#### Scenario: Property lookup builds its index once

- **Given** a table whose `GetFieldByPropertyName` has never been called
- **When** it is called with `"Guid"`
- **Then** `_propertyNameIndex` is built from all fields with a non-null `Property`, keyed by property name,
  and reused on subsequent calls; the build is not synchronised, so concurrent first calls may each build a
  dictionary

#### Scenario: Unknown names return null rather than throwing

- **Given** a loaded table
- **When** `GetField("NoSuchColumn")` or `GetFieldByPropertyName("NoSuchProperty")` is called
- **Then** `null` is returned

#### Scenario: A post-load column rename desynchronises the keys

- **Given** a loaded table whose field for property `Sku` was keyed — and whose index columns were resolved —
  under the column name `"Sku"`, and a fluent mapping that afterwards assigns `field.Name = "sku_code"` on the
  cached `AbstractField` (as `ModelMapRegistry.ApplyToDatabase` does for `HasColumnName`, `AbstractField.Name`
  being a public setter)
- **When** the table is used after that assignment
- **Then** `Fields` is still keyed `"Sku"` and `Table.Indexes` still names the column `"Sku"`, while the
  field's own `Name` reads `"sku_code"` — neither the dictionary key nor the index columns are recomputed,
  because both were fixed when `ComputeTable` ran

### Requirement: Column exclusion attributes

The system SHALL skip a property entirely — producing no field and therefore no column — when it carries
`[IgnoreField]` (Birko) or `[NotMapped]` (DataAnnotations), and SHALL not consider a property that has no setter
(`CanWrite == false`) at all when enumerating a type's fields; a private setter still counts.

#### Scenario: IgnoreField removes the column

- **Given** `[IgnoreField] public string Scratch { get; set; }`
- **When** `LoadField` runs for that property
- **Then** `CreateAbstractField` returns `null` and `LoadField` yields an empty sequence, so the column is
  absent from `Table.Fields`

#### Scenario: NotMapped is honoured even without Birko attributes

- **Given** `[NotMapped] public string Scratch { get; set; }` with no Birko field attributes at all
- **When** `LoadField` runs
- **Then** the property is still skipped, because the `[NotMapped]` check reads the property's attributes
  directly

#### Scenario: A computed get-only property is not a column

- **Given** `public string Display => First + " " + Last;` on a `[Table]` model
- **When** `LoadFields` enumerates the type
- **Then** `Display` produces no field — it could never be set from a row — while a property with a private
  setter is still mapped

### Requirement: An unmappable property fails table load rather than vanishing

The system SHALL, when a property's CLR type matches no arm of the type dispatch and the property carries
neither `[IgnoreField]` nor `[NotMapped]`, throw `Exceptions.FieldAttributeException` from
`CreateAbstractField` — naming the declaring type, the property, its CLR type, and both opt-out attributes
— instead of returning `null` and producing a table without that column.

#### Scenario: A collection property is reported, not dropped

- **Given** a `[Table]`-annotated model with `public IEnumerable<string> Items { get; set; }`
- **When** `DataBase.LoadTable(type)` is called
- **Then** a `FieldAttributeException` propagates out of table load, and its message contains the property
  name, `IEnumerable`, `IgnoreField` and `NotMapped`

#### Scenario: An indexer is skipped rather than reported

- **Given** a `[Table]`-annotated model declaring `public string this[string key] { get; set; }`, which
  `GetProperties` enumerates like any other public instance property
- **When** `LoadTable` runs
- **Then** the indexer produces no field and no exception, while the model's ordinary properties map
  normally — an indexer has no single value to store and cannot be read through `GetValue(obj, null)`, so
  no mapping could ever cover it and the unmapped-type failure does not apply

#### Scenario: The exclusion attributes are checked before the dispatch, so opting out never throws

- **Given** the same unmappable property carrying `[IgnoreField]`, and a second carrying `[NotMapped]`
- **When** `LoadTable` runs
- **Then** neither property appears in `Table.Fields`, no exception is raised, and the model's mapped
  properties load normally — the failure fires on silence, not on an explicit instruction

### Requirement: Column name resolution precedence

The system SHALL name a column after the property by default, override it with `[NamedField(name)]` when the
supplied name is non-empty, and apply `[Column(Name)]` from DataAnnotations **only when no non-empty
`[NamedField]` name was present**.

#### Scenario: Birko naming beats DataAnnotations naming

- **Given** a property carrying both `[NamedField("b_code")]` and `[Column("da_code")]`
- **When** the field is created
- **Then** its `Name` is `"b_code"`

#### Scenario: Empty NamedField falls through to the property name

- **Given** `[NamedField]` with a null name and no `[Column]`
- **When** the field is created
- **Then** its `Name` is the property's own name

#### Scenario: DataAnnotations naming applies alone

- **Given** a property carrying only `[Column("da_code")]`
- **When** the field is created
- **Then** its `Name` is `"da_code"`

### Requirement: Column flag resolution

The system SHALL set `IsPrimary` from `[PrimaryField]` or `[Key]`, `IsUnique` from `[UniqueField]`,
`IsAutoincrement` from `[IncrementField]` or `[DatabaseGenerated(Identity)]`, and treat `[RequiredField]` or
`[Required]` as forcing NOT NULL; and SHALL take `MaxLength` from `[MaxLengthField]` first, then — only if
still unset — from `[MaxLength]` and then `[StringLength]`, with `[PrecisionField]` and `[ScaleField]`
supplying decimal precision/scale.

#### Scenario: Either primary-key attribute works

- **Given** a property with `[Key]` and no `[PrimaryField]`
- **When** the field is created
- **Then** `IsPrimary` is `true`

#### Scenario: Required overrides C# nullability

- **Given** `[RequiredField] public string? Name { get; set; }` (a nullable reference type)
- **When** the field is created
- **Then** the computed `effectiveNotNull` is `true` and the resulting `StringField` has
  `IsNotNull == true`, even though the CLR type is nullable

#### Scenario: Non-nullable value types are NOT NULL without any attribute

- **Given** `public int Count { get; set; }`
- **When** the field is created
- **Then** an `IntegerField` (not `NullableIntegerField`) is produced with `IsNotNull == true`

#### Scenario: Nullable value types are nullable

- **Given** `public DateTime? Closed { get; set; }`
- **When** the field is created
- **Then** a `NullableDateTimeField` with `IsNotNull == false` is produced

#### Scenario: A zero MaxLengthField suppresses the DataAnnotations fallback

- **Given** a `string` property carrying both `[MaxLengthField]` (default `0`) and `[MaxLength(50)]`
- **When** the field is created
- **Then** `maxLength` is set to `0` by the Birko attribute, so the `maxLength == null` guard blocks the
  DataAnnotations value, and because `0` is not `> 0` a length-less `StringField` is produced instead of a
  50-character `CharField`

#### Scenario: PrecisionField doubles as a string length for backwards compatibility

- **Given** a `string` property with `[PrecisionField(32)]` and no `[MaxLengthField]`
- **When** the field is created
- **Then** a `CharField` with `Lenght == 32` is produced

### Requirement: Cross-assembly field attributes are rebuilt by short type name

The system SHALL, when a property carries no directly castable `Birko.Data.SQL.Attributes.Field`, collect
the attributes whose full type name starts with `"Birko.Data.SQL.Attributes."` and rebuild local equivalents
by short type name: `PrimaryField`, `UniqueField`, `RequiredField`, `IncrementField`, `IgnoreField`,
`UtcField`, `NamedField` (name), `MaxLengthField`, `PrecisionField`, `ScaleField` and `IndexedField` (name,
order, descending, unique, `WhereNotNull`, `WhereNull`). Every carried value SHALL be read as whichever public
instance member the foreign attribute declares — a property, else a field — and a member it lacks SHALL throw
`Exceptions.FieldAttributeException` stating it was compiled from different Birko.Data.SQL sources. Any other
subtype under that namespace SHALL throw `FieldAttributeException` naming the property, the attribute type and its
assembly, rather than being skipped.

#### Scenario: A foreign-assembly UtcField keeps the column an instant

- **Given** a `DateTime` property carrying a `UtcField` compiled into a different assembly
- **When** `LoadField` runs
- **Then** a local `UtcField` is rebuilt and the property maps to a `UtcDateTimeField`

#### Scenario: A foreign-assembly MaxLengthField keeps its width

- **Given** a `string` property carrying `[MaxLengthField(50)]` compiled into a different assembly
- **When** `LoadField` runs
- **Then** the value is read from the attribute's public field, the rebuilt `MaxLengthField` has
  `MaxLength == 50`, and a 50-character `CharField` is produced

#### Scenario: An attribute the rebuild does not know fails table load

- **Given** a property carrying, from another assembly, an attribute under `Birko.Data.SQL.Attributes.` that is not
  one of the rebuilt types
- **When** `LoadField` runs
- **Then** `FieldAttributeException` is thrown, advising to compile both from the same Birko.Data.SQL sources

### Requirement: CLR-type to SQL field mapping

The system SHALL map property CLR types to field classes as: `bool`/`bool?` → `BooleanField` /
`NullableBooleanField`; `DateTime`/`DateTime?` → `DateTimeField` / `NullableDateTimeField`, or — when the
property carries `[UtcField]` — `UtcDateTimeField` / `NullableUtcDateTimeField` carrying
`DbType.DateTimeOffset`; `TimeOnly`/`TimeOnly?` → `TimeOnlyField` / `NullableTimeOnlyField` carrying
`DbType.String`;
`decimal`/`decimal?` → `DecimalField` / `NullableDecimalField` (carrying precision and scale);
`Guid`/`Guid?` → `GuidField` / `NullableGuidField`; `int`/`int?` → `IntegerField` /
`NullableIntegerField`; `long`/`long?` → `LongField` / `NullableLongField`; `short`/`short?` →
`ShortField` / `NullableShortField`; `double`/`double?` → `DoubleField` / `NullableDoubleField`;
`float`/`float?` → `FloatField` / `NullableFloatField`; `byte[]` → `BinaryField`, whose `MaxLength` is the
`[MaxLengthField]` width when greater than zero, else the `[PrecisionField]` value when greater than zero,
else `null`; `char` → `CharField` of
length 1; `string` → `CharField` when an effective length greater than zero is known else `StringField`;
any enum or `Nullable<enum>` whose underlying type is not `long`, `ulong` or `uint` → `IntegerField` /
`NullableIntegerField`, an enum backed by one of those three raising `Exceptions.FieldAttributeException`
because its values may not fit the 32-bit column; and SHALL throw
`Exceptions.FieldAttributeException` naming the declaring type, the property and its CLR type — rather
than returning `null` and silently skipping the property — for every other type. A `[UtcField]` on any
property whose type is not `DateTime` / `DateTime?` SHALL raise `FieldAttributeException` instead of being
ignored.

#### Scenario: Enums are stored as integers

- **Given** `public InvoiceState State { get; set; }` where `InvoiceState` is an `int`-backed enum
- **When** the field is created
- **Then** an `IntegerField` with `DbType.Int32` and `IsNotNull == true` is produced

#### Scenario: Nullable enums map to the nullable integer field

- **Given** `public InvoiceState? State { get; set; }`
- **When** the field is created
- **Then** a `NullableIntegerField` is produced, resolved via `Nullable.GetUnderlyingType(...).IsEnum`

#### Scenario: A long-backed enum is refused

- **Given** `public Flags Mask { get; set; }` where `enum Flags : long`
- **When** the field is created
- **Then** `FieldAttributeException` is thrown naming the property, the enum and its `Int64` backing type and
  advising to back it by `int` or smaller; a `byte`-backed enum maps to an `IntegerField`

#### Scenario: long, double and byte[] properties map to their own columns

- **Given** a `[Table]`-annotated model with `public long Ticks { get; set; }`,
  `public double Ratio { get; set; }` and `public byte[] Blob { get; set; }`
- **When** `LoadFields` runs
- **Then** all three appear in `Table.Fields` as a `LongField`, a `DoubleField` and a `BinaryField`, so the
  columns exist and the values round-trip through `Write` and `Read`

#### Scenario: A wide integer is not narrowed through the integer arm

- **Given** `public long Ticks { get; set; }`
- **When** the field is created
- **Then** it is a `LongField` carrying `DbType.Int64` and is not an `IntegerField` — an `Int32` column
  would silently truncate every value past 2^31

#### Scenario: Binary floating point is not routed through the decimal arm

- **Given** `public double Ratio { get; set; }`
- **When** the field is created
- **Then** it is a `DoubleField` carrying `DbType.Double` and is not a `DecimalField` — `decimal` is exact
  base-10 and `double` is binary floating point, so the two map to different provider column types

#### Scenario: byte[] is nullable by default and honours a required marker

- **Given** `public byte[] Blob { get; set; }` with no required marker
- **When** the field is created
- **Then** a `BinaryField` with `IsNotNull == false` is produced, following `StringField`'s reference-type
  convention rather than the value-type `Nullable*` pairing, and `[RequiredField]` / `[Required]` sets
  `IsNotNull` to true

#### Scenario: Nullable char raises the unmapped-type failure

- **Given** `public char? Flag { get; set; }`
- **When** the field is created
- **Then** the `property.PropertyType == typeof(char)` test fails (the type is `Nullable<char>`), the
  underlying type is not an enum, and `FieldAttributeException` is thrown — where the property was
  previously skipped in silence, its absence from the table is now reported

#### Scenario: UtcField makes a DateTime column an instant

- **Given** `[UtcField] public DateTime CreatedAt { get; set; }` and `public DateTime NoticeDate { get; set; }`
  on the same model
- **When** the fields are created
- **Then** `CreatedAt` is a `UtcDateTimeField` with `DbType.DateTimeOffset` and `NoticeDate` a plain
  `DateTimeField` with `DbType.DateTime`; on write the instant field converts a `Local` value to UTC, treats an
  `Unspecified` one as UTC, and stores a `DateTimeOffset`

#### Scenario: UtcField on a non-DateTime property is refused

- **Given** `[UtcField] public string Stamp { get; set; }`
- **When** the field is created
- **Then** `FieldAttributeException` is thrown stating that `[UtcField]` is only valid on a `DateTime` or
  `DateTime?` property and naming the declaring type, the property and its CLR type

#### Scenario: TimeOnly is stored as fixed-width text

- **Given** `public TimeOnly OpensAt { get; set; }` holding `08:05:00`
- **When** the field writes it
- **Then** a `TimeOnlyField` with `DbType.String` stores the invariant-culture text `"08:05:00"` (format
  `HH:mm:ss`)

#### Scenario: A byte[] column carries a declared width

- **Given** `[MaxLengthField(16)] public byte[] Hash { get; set; }`
- **When** the field is created
- **Then** a `BinaryField` with `MaxLength == 16` is produced; without a positive `[MaxLengthField]` or
  `[PrecisionField]` its `MaxLength` is `null`

### Requirement: Reader materialisation per field type

The system SHALL materialise a column into its property through a type-specific `Read` override —
`GetBoolean`, `GetDateTime`, `GetDecimal`, `GetGuid`, `GetInt32`, `GetInt64`, `GetInt16`, `GetDouble`,
`GetFloat`, `GetFieldValue<byte[]>`, `GetString`, `GetFieldValue<DateTimeOffset>(…).UtcDateTime` for the
instant fields — where the nullable variants first test
`reader.IsDBNull(index)` and assign `null`, while the non-nullable variants perform no null test at all;
and `AbstractField.Read` SHALL by default assign `reader.GetValue(index)` unconverted. `TimeOnlyField` SHALL
instead parse whatever the provider returns (`TimeOnly`, `DateTime`, `TimeSpan`, or text in `HH:mm:ss` or
any invariant time format), its non-nullable variant assigning `default(TimeOnly)` for a NULL or unparseable
value rather than throwing.

#### Scenario: Nullable field reads a database NULL

- **Given** a `NullableDecimalField` and a row whose column is NULL
- **When** `Read(entity, reader, index)` is called
- **Then** the property is set to `null`

#### Scenario: Non-nullable field over a NULL column throws

- **Given** a `BooleanField` (non-nullable variant) and a row whose column is unexpectedly NULL — e.g. a
  legacy row predating a `[RequiredField]` addition
- **When** `Read` is called
- **Then** `reader.GetBoolean(index)` is invoked with no `IsDBNull` guard and the provider's cast/NULL
  exception propagates

#### Scenario: Integer field converts to the enum type on read

- **Given** an `IntegerField` bound to an enum-typed property and a column value of `2`
- **When** `Read` is called
- **Then** `Enum.ToObject(enumType, 2)` is assigned, resolving `Nullable<TEnum>` to `TEnum` first

#### Scenario: Enum write unwraps to int

- **Given** an `IntegerField` bound to an enum property whose value is `InvoiceState.Paid`
- **When** `Write(entity)` is called
- **Then** the boxed enum is converted with `Convert.ToInt32(value, InvariantCulture)` and the integer
  returned, so an enum backed by `byte`, `short` or `int` writes alike; a null property value returns `null`
  before any conversion

#### Scenario: String and char fields both null-check

- **Given** a `StringField` or a `CharField` and a NULL column
- **When** `Read` is called
- **Then** `null` is assigned (both implement the `IsDBNull` branch, `CharField` overriding `StringField`)

#### Scenario: CharField reads a char property as the first character

- **Given** `public char Flag { get; set; }`, which `CreateAbstractField` maps to `CharField` with length 1
- **When** `CharField.Read` runs over a non-NULL column
- **Then** the property receives the column string's first character, or `'\0'` for an empty string, while a
  `string` property mapped to `CharField` (a declared length) still receives the whole string

#### Scenario: An instant reads back as UTC

- **Given** a `UtcDateTimeField` over a stored timestamp
- **When** `Read` is called
- **Then** the column is read as `DateTimeOffset` and its `UtcDateTime` — `DateTimeKind.Utc` — is assigned

#### Scenario: A NULL in a non-nullable TimeOnly column reads as midnight

- **Given** a `TimeOnlyField` (non-nullable) and a NULL column
- **When** `Read` is called
- **Then** `default(TimeOnly)` (`00:00:00`) is assigned and no exception is raised

### Requirement: Row write projection

The system SHALL project an entity to a column-name → value dictionary by calling each field's `Write`, and
SHALL insert the result with the null-forgiving operator so a null property value is stored as a null entry
rather than being skipped.

#### Scenario: Null values are present as null entries

- **Given** an entity with a nullable property set to `null`
- **When** `DataBase.Write(entity)` is called
- **Then** the returned dictionary contains that column's key mapped to `null` (added via
  `tableField.Write(data)!`), not an absent key

#### Scenario: Fields are read from a per-type cache

- **Given** repeated `DataBase.Write(entity)` / `DataBase.Read(reader, entity)` calls for the same CLR type
- **When** they run
- **Then** `LoadFields` serves the field list from the static `_fieldsCache` `ConcurrentDictionary`, which is
  never invalidated for the process lifetime

### Requirement: A field reports the stored form of an unassigned value

The system SHALL route both `AbstractField.Write` and `AbstractField.DefaultStoredValue` through the
overridable `ToStorage` conversion, and SHALL compute `DefaultStoredValue` as `ToStorage(default(T))` of the
property's underlying (non-nullable) type when that type is a value type, `null` for a reference type, and
`null` for a field without a CLR property.

#### Scenario: A TimeOnly default is stored text, not an empty string

- **Given** a `TimeOnlyField`
- **When** `DefaultStoredValue` is read
- **Then** it is `"00:00:00"`, the same form `Write` produces for an unassigned property

#### Scenario: A required nullable value type defaults to its underlying default

- **Given** `[Required] public int? Count { get; set; }`
- **When** `DefaultStoredValue` is read
- **Then** it is `0`; for a `string` property it is `null`

### Requirement: Primary-key resolution with a Guid fallback

The system SHALL return the fields marked `IsPrimary` for a type, and — when none is marked — SHALL fall
back to the field whose property is named `"Guid"`, returning an empty sequence only when neither exists (or
the type maps to no table). At table load, an `AbstractModel` descendant whose fields declare no primary key
SHALL have its `Guid` field marked `IsPrimary` and `IsImplicitPrimary`; `Tables.Table.DeclarePrimary(field)`
SHALL make a field a declared key and clear every implicit key on the table, so a key declared after load
replaces the promoted `Guid` rather than joining it in a composite.

#### Scenario: Attribute-declared primary keys win

- **Given** a model with `[PrimaryField] public int Id { get; set; }`
- **When** `DataBase.GetPrimaryFields(typeof(Model))` is called
- **Then** the `Id` field is returned

#### Scenario: Guid property is the implicit key

- **Given** an `AbstractModel` descendant with no `[PrimaryField]` anywhere
- **When** `GetPrimaryFields` is called
- **Then** the field mapped from the `Guid` property is returned, so UPDATE/DELETE can be qualified with a
  WHERE clause — and since table load promoted it, that field carries `IsPrimary == true` and
  `IsImplicitPrimary == true`, so the table's DDL declares it the `PRIMARY KEY`

#### Scenario: A key declared after load replaces the implicit one

- **Given** such a table, whose promoted `Guid` is the implicit key, and a fluent mapping declaring `Code` the key
  through `DeclarePrimary`
- **When** the primary fields are read
- **Then** only `Code` is primary; `Guid` has `IsPrimary == false` and `IsImplicitPrimary == false`

#### Scenario: Neither key nor Guid yields an empty result

- **Given** a `[Table]` model with no `[PrimaryField]` and no `Guid` property
- **When** `GetPrimaryFields` is called
- **Then** an empty array is returned (callers must handle "no key" themselves)

### Requirement: Index-key and inline-constraint placement is computed per field

The system SHALL expose on `AbstractField` three computed answers: `IsInIndexKey` = `IsIndexed || IsUnique
|| IsPrimary`; `UsesInlineUniqueConstraint` = `IsUnique && (IsNotNull || IsPrimary)`, so a nullable unique
column does not carry an inline `UNIQUE`; and `UsesInlinePrimaryConstraint` = `IsPrimary` and the field's
`Table` has at most one primary field (a null `Table` counting as one), so a table with several primary
fields renders one table-level composite key rather than one inline `PRIMARY KEY` per column.

#### Scenario: A unique column is an index key even when no index names it

- **Given** `[UniqueField] public string Code { get; set; }` with no `[IndexedField]`
- **When** `IsIndexed` and `IsInIndexKey` are read
- **Then** `IsIndexed` is `false` while `IsInIndexKey` is `true`

#### Scenario: Two primary fields form a composite key

- **Given** a table whose fields `A` and `B` both carry `[PrimaryField]`
- **When** `UsesInlinePrimaryConstraint` is read for either
- **Then** it is `false`; a table with a single primary field answers `true` for it

#### Scenario: A field outside any table keeps the inline primary key

- **Given** a primary field whose `Table` is null (the migrations path)
- **When** `UsesInlinePrimaryConstraint` is read
- **Then** it is `true`

### Requirement: Field resolution from a lambda expression

The system SHALL resolve a property access lambda to an `AbstractField` via `GetField<T,P>` and the
non-generic `GetFieldFromLambda`, accepting a `MemberExpression` body or a `UnaryExpression` wrapping one,
resolving a property declared on an **interface** to the property of that name on the lambda's parameter type
(most-derived first, keeping the interface property when none is found by name), remapping properties
reflected on `AbstractLogModel`/`AbstractModel` to their `AbstractDatabaseLogModel`/`AbstractDatabaseModel`
counterparts, and throwing `ArgumentException` when no property can be resolved.

#### Scenario: Boxed/converted member access is unwrapped

- **Given** `x => (object)x.Number`, whose body is a `UnaryExpression`
- **When** `GetField` is called
- **Then** the operand's `MemberExpression` member is used as the property

#### Scenario: Non-member body is rejected

- **Given** `x => x.Number + 1`
- **When** `GetField` is called
- **Then** an `ArgumentException` with message starting `"Unable to resolve property from expression:"` and
  parameter name `expr` is thrown

#### Scenario: An excluded property makes resolution fail late

- **Given** a lambda naming a property carrying `[IgnoreField]`
- **When** `GetField` is called
- **Then** `LoadField` returns an empty sequence and the trailing `.First()` throws
  `InvalidOperationException` rather than a descriptive error
- **And** `GetFieldFromLambda` given the same lambda instead throws `ArgumentException` stating that the named
  member is not a mapped column

#### Scenario: An interface-typed member honours the model's column name

- **Given** `x => x.UpdatedBy` where the lambda parameter is the model type but the member binds to an interface
  property, and the model's `UpdatedBy` carries `[NamedField("updated_by")]`
- **When** `GetField` or `GetFieldFromLambda` is called
- **Then** the model's own property is used and the field is named `"updated_by"`; an explicit interface
  implementation, not found by name, keeps the interface property

### Requirement: Per-property index discovery

The system SHALL build the table's index dictionary by scanning all public instance properties (including
inherited ones) for `[IndexedField]` — both the directly castable attribute and any attribute whose full
name is `"Birko.Data.SQL.Attributes.IndexedField"` from another assembly, read reflectively — grouping the
contributions by index name into one `Tables.IndexDefinition` per name, and SHALL mark the whole index
`Unique` if **any** contributing attribute sets `IsUnique`. It SHALL throw `Exceptions.TableAttributeException`
for an `[IndexedField]` on a property declared by a base class of the loaded type, and for an `[IndexedField]` on
a property that is not a mapped column.

#### Scenario: Two properties form one composite index

- **Given** `[IndexedField("IX_TN", 0)] TenantGuid` and `[IndexedField("IX_TN", 1)] Number` on the same class
- **When** `LoadIndexes` runs
- **Then** one `IndexDefinition` named `"IX_TN"` is produced with two `IndexColumn`s ordered `TenantGuid`
  then `Number`

#### Scenario: Unique is promoted from a single contributor

- **Given** `[IndexedField("IX_TN", 0, IsUnique: true)] TenantGuid` and `[IndexedField("IX_TN", 1)] Number`
- **When** `LoadIndexes` runs
- **Then** the resulting definition has `Unique == true`

#### Scenario: Cross-assembly duplicates are not double-counted

- **Given** the `IndexedField` attribute type available both directly and as a distinct cross-assembly type
- **When** attributes are collected
- **Then** the cross-assembly query excludes anything that `is Attributes.IndexedField`, so the same
  attribute instance contributes at most one column

#### Scenario: Column name is remapped through field metadata

- **Given** `[NamedField("tenant_guid")] [IndexedField("IX_TN")] public Guid TenantGuid { get; set; }`
- **When** `LoadIndexes` runs
- **Then** the index column name is `"tenant_guid"`, resolved by matching `field.Property.Name` against the
  property name

#### Scenario: An index on an unmapped property is refused

- **Given** `[IgnoreField] [IndexedField("IX_Bad")] public string Scratch { get; set; }`
- **When** `LoadIndexes` runs
- **Then** no matching field is found and `TableAttributeException` is thrown stating that the property is not a
  mapped column, instead of an index over a column the table does not have

#### Scenario: A base-class IndexedField is refused

- **Given** `[IndexedField("IX_Tenant")]` declared on a shared base class property
- **When** `LoadIndexes` runs for a derived `[Table]` type
- **Then** `TableAttributeException` is thrown naming the base class and property and advising a
  table-specific `[CompositeIndex]` on each derived class — index names are database-wide on PostgreSQL and
  SQLite, so every derived table declaring the same name would leave all but the first without the index

### Requirement: Class-level composite index discovery

The system SHALL additionally read class-level `[CompositeIndex(name, params properties)]` attributes —
direct and cross-assembly-by-name — **without walking base classes** (`GetCustomAttributes(..., false)`,
matching the attribute's `Inherited = false`), resolving each listed property name to its mapped column and
assigning columns `Order` equal to their position in the list; and SHALL **throw**
`Exceptions.TableAttributeException` when a listed property is not a mapped column.

#### Scenario: Composite over an inherited discriminator

- **Given** `[CompositeIndex("UX_Tenant_Number", nameof(TenantGuid), nameof(Number), IsUnique = true)]` on a
  derived entity whose `TenantGuid` is declared on a base class
- **When** `LoadIndexes` runs
- **Then** one unique index is produced with columns in the declared order, `TenantGuid` resolved through
  the same fields map that already contains inherited properties

#### Scenario: Typo fails fast at table load

- **Given** `[CompositeIndex("UX_A", "TenantGuid", "Numbr")]` where `Numbr` does not exist or is not mapped
- **When** `LoadIndexes` runs
- **Then** a `TableAttributeException` is thrown with message
  `"CompositeIndex 'UX_A' on <FullName>: property 'Numbr' is not a mapped column"` — the constraint is
  never silently dropped

#### Scenario: Subclasses do not inherit the declaration

- **Given** `[CompositeIndex("UX_A", ...)]` on class `Base` and a subclass `Derived : Base`
- **When** `LoadIndexes(typeof(Derived), ...)` runs
- **Then** `"UX_A"` is absent from `Derived`'s indexes, avoiding a collision on the database-global index
  name

### Requirement: Index column ordering

The system SHALL order each index's columns stably by their `Order` value, ties kept in the order the
columns were contributed, after all contributions are collected; `[IndexedField]` supplies `Order` from the
attribute (default `0`) and `[CompositeIndex]` supplies the positional index.

#### Scenario: Explicit orders are honoured

- **Given** contributions with `Order` `1` then `0` discovered in that reflection order
- **When** sorting completes
- **Then** the `Order == 0` column precedes the `Order == 1` column

#### Scenario: Tied orders keep their contribution order

- **Given** a composite index declared as `[IndexedField("IX_AB")]` on two properties, both defaulting to
  `Order == 0`
- **When** the columns are ordered by `Order`, then by position
- **Then** the two keep the order in which they were contributed — the order `Type.GetProperties()` enumerated
  the properties — rather than an unstable sort's arbitrary order

#### Scenario: Mixing both attribute styles on one index name merges the columns

- **Given** an `[IndexedField("IX_A")]` on a property *and* a `[CompositeIndex("IX_A", ...)]` on the class
- **When** `LoadIndexes` runs
- **Then** both contributions accumulate into the single `"IX_A"` definition, with the composite's positional
  orders competing against the per-property orders — the columns are appended, not deduplicated

### Requirement: Partial index predicates are declared as column lists

The system SHALL read `WhereNotNull` and `WhereNull` property-name lists from both `[IndexedField]` and
`[CompositeIndex]` (directly and cross-assembly by reflection), merge them across every attribute
contributing to the same index name with duplicates collapsed, and resolve them once after both attribute
forms have contributed: each name SHALL resolve to a mapped column, else `TableAttributeException`; a name in
both lists SHALL throw `TableAttributeException` because the index would contain no rows; and a name whose
field is `IsNotNull` or `IsPrimary` SHALL throw `TableAttributeException`. Resolved predicates SHALL be
appended to `IndexDefinition.Predicates` as all `IS NOT NULL` terms then all `IS NULL` terms, each group
sorted ordinally by property name.

#### Scenario: Unique among live rows only

- **Given** `[CompositeIndex("UX_TN", nameof(TenantGuid), nameof(Number), IsUnique = true, WhereNull = new[] { nameof(DeletedAt) })]`
  where `DeletedAt` is a nullable `DateTime?`
- **When** `LoadIndexes` runs
- **Then** the definition carries one predicate `{ ColumnName = "DeletedAt", RequireNull = true }`, and
  `DeletedAt` need not be a key column

#### Scenario: A contradiction is visible only after the merge

- **Given** two `[IndexedField("IX_A")]` contributors, one listing `Code` in `WhereNotNull` and the other in
  `WhereNull`
- **When** `LoadIndexes` runs
- **Then** `TableAttributeException` is thrown naming `Code` as being in both lists

#### Scenario: A predicate over a NOT NULL column is refused

- **Given** `WhereNotNull = new[] { nameof(Count) }` where `Count` is a non-nullable `int`
- **When** `LoadIndexes` runs
- **Then** `TableAttributeException` is thrown stating that the column is declared NOT NULL and that the
  predicate would exclude nothing

### Requirement: A nullable unique column is constrained by a synthesised filtered unique index

The system SHALL, when `LoadIndexes` receives a table name, give every field that is `IsUnique`, not
`IsPrimary` and not `UsesInlineUniqueConstraint` (i.e. nullable) a unique index named
`ux_{table}_{column}` over that single column, with a `WhereNotNull` predicate on the same column routed
through the declared-predicate path, and SHALL set that field's `IsIndexed`. An existing index with that
name that is already unique over exactly that one column SHALL be kept; any other index already using the
name SHALL raise `TableAttributeException`. When no table name is supplied no such index is synthesised.

#### Scenario: A nullable unique business key

- **Given** table `Invoice` and `[UniqueField] public string ExternalId { get; set; }` without `[RequiredField]`
- **When** the table is loaded
- **Then** its indexes contain `ux_Invoice_ExternalId`, unique over `ExternalId` with the predicate
  `ExternalId IS NOT NULL`, and the field carries `IsIndexed == true` and `UsesInlineUniqueConstraint == false`

#### Scenario: A required unique column stays inline

- **Given** `[UniqueField] [RequiredField] public string Code { get; set; }`
- **When** the table is loaded
- **Then** no `ux_…` index is synthesised, because `UsesInlineUniqueConstraint` is `true`

#### Scenario: A declared index squatting on the synthesised name is refused

- **Given** a declared `[IndexedField("ux_Invoice_ExternalId")]` on another property
- **When** the table is loaded
- **Then** `TableAttributeException` is thrown advising to rename the declared index or mark the column
  `[RequiredField]`

### Requirement: SQL index definition shape

The system SHALL represent a SQL index as `Tables.IndexDefinition` with a `Name`, a `Unique` flag, an
always-initialised `Columns` list of `IndexColumn` (column name, `Order`, `IsDescending`), and an
always-initialised `Predicates` list of `IndexPredicate` (column name, `RequireNull`) that is empty for an
ordinary index.

#### Scenario: Columns list is never null

- **Given** `new Tables.IndexDefinition { Name = "IX_A" }`
- **When** `Columns` is read
- **Then** an empty list is returned, so callers may `Add` without initialising it

### Requirement: SQL index creation

The system SHALL, on `SqlIndexManager.CreateAsync`, validate that a scope, a non-empty definition name and
at least one field are present, translate the portable definition to a `Tables.IndexDefinition` preserving
field order, descending flags **and the `Unique` flag**, validate each field name as a plain unqualified
identifier, carry `Sparse` on a single-field definition as a `WhereNotNull` predicate over that field and
refuse every portable flag the SQL emitter cannot express (`ExpireAfter`, non-empty `Properties`, `Sparse` on
a multi-field definition) with `NotSupportedException` from `IndexBuilderSupport.Unsupported`, refuse a
predicate the connector can neither express nor drop through `RequireExpressiblePredicates` before building
the statement, delegate to the connector's `CreateIndexSql`, tolerate a provider report that the
index is already present, and wrap any other execution failure in `IndexManagementException`.

#### Scenario: Index field names are validated before interpolation

- **Given** `Fields = [{ Name = "Rank); CREATE TABLE Pwned (x INTEGER); --" }]`
- **When** `ToSqlIndexDefinition` translates the definition
- **Then** `ArgumentException` is thrown by `DataBase.ValidateIndexFieldIdentifier`, because index columns
  are interpolated **bare** into `CREATE INDEX` and these names come from the caller rather than from table
  metadata
- **And** a `Table.Column` qualifier is rejected as well: the check uses an unqualified-only pattern, since a
  qualifier is invalid in an index column list on every supported provider

#### Scenario: An already-present index is not an error, on any provider

- **Given** an index that already exists
- **When** `CreateAsync` runs
- **Then** it completes without throwing on every provider — SQLite and PostgreSQL because their statement
  carries `IF NOT EXISTS`, MSSql because its statement is wrapped in a `sys.indexes` guard, and MySQL because
  `Connector.IsIndexAlreadyExistsException` recognises error 1061 and the manager tolerates it
- **And** `DropAsync` mirrors this for an already-absent index via `IsIndexMissingException` (MySQL 1091), so
  the manager is uniform across the whole verb family rather than for create alone
- **And** the connector's own `DropIndexes` deliberately does **not** tolerate a missing index: a caller that
  named a specific index is expected to fail loudly

#### Scenario: Empty field list is rejected

- **Given** `new IndexDefinition { Name = "IX_A" }` with no fields
- **When** `CreateAsync(definition, "Invoice")` is called
- **Then** an `ArgumentException` `"At least one field is required."` with parameter name `definition` is
  thrown and no SQL is executed

#### Scenario: Null definition and empty name are rejected

- **Given** `CreateAsync(null, "Invoice")` or a definition with a whitespace `Name`
- **When** the call is made
- **Then** `ArgumentNullException` / `ArgumentException("Index name is required.")` is thrown after the scope
  check

#### Scenario: The unique path goes through the connector like every other index

- **Given** a definition with `Unique == true`
- **When** `CreateAsync` runs on the base manager
- **Then** `_connector.CreateIndexSql(scope, sqlIndex)` is called with `sqlIndex.Unique == true` — there is no
  `CreateUniqueIndexSql` on the manager, and no per-dialect override of one, so the connector's emitter is the
  single producer of index DDL for every provider
- **And** the emitted statement is
  `CREATE UNIQUE INDEX IF NOT EXISTS "<index>" ON "<table>" (<col> [DESC], ...)` on the default and PostgreSQL
  dialects — index **columns bare**, index and table names quoted, the table rendered through
  `TableIdentifier` (`"<schema>"."<table>"` for a schema-qualified entity, the `schema__` prefix where the
  provider has no schemas)
- **And** MySQL's connector override emits the same statement without the `IF NOT EXISTS` clause, which that
  provider rejects outright

#### Scenario: Translation carries the Unique flag onto the SQL definition

- **Given** a portable definition with `Unique == true`
- **When** `ToSqlIndexDefinition` builds the `Tables.IndexDefinition`
- **Then** `Name`, `Columns` **and** `Unique` are copied, so the connector's emitter can render `UNIQUE`
  without the caller branching on the portable flag
- **And** the method is `protected static` rather than `private`, so the hand-off can be asserted without a
  live server

#### Scenario: Ordering and direction survive translation

- **Given** portable fields `[Descending("CreatedAt"), Ascending("Number")]`
- **When** translation runs
- **Then** the SQL columns are `CreatedAt` (Order 0, descending) and `Number` (Order 1, ascending) in that
  order

#### Scenario: A sparse single-field index becomes a partial index

- **Given** `new IndexDefinition { Name = "IX_Ext", Fields = [Ascending("ExternalId")], Sparse = true }`
- **When** `ToSqlIndexDefinition` runs
- **Then** the SQL definition carries the predicate `ExternalId IS NOT NULL`, its column name checked by
  `DataBase.ValidateIndexFieldIdentifier`

#### Scenario: Flags SQL cannot express are refused, not dropped

- **Given** a definition with `ExpireAfter` set, or with any `Properties`, or with `Sparse = true` over two
  fields
- **When** `CreateAsync` translates it
- **Then** `NotSupportedException` is thrown before any SQL is built or executed — a TTL index, index
  properties and a sparse composite index each name a different reason and alternative

#### Scenario: A sparse index on a provider without partial indexes

- **Given** a sparse single-field definition and a MySQL connector, which supports no partial index
- **When** `CreateAsync` runs
- **Then** `Connector.RequireExpressiblePredicates` passes it, because the `WhereNotNull` term is over the
  index's own key column and so can be dropped without changing what the index enforces

### Requirement: SQL index drop

The system SHALL drop an index by delegating to the connector's `DropIndexSql` with a name-only
`Tables.IndexDefinition`, and SHALL wrap any execution failure in `IndexManagementException` naming the
index and scope.

#### Scenario: Only the name is supplied to the dialect

- **Given** `DropAsync("IX_A", "Invoice")`
- **When** the SQL is built
- **Then** a `Tables.IndexDefinition { Name = "IX_A" }` with no columns is passed to `DropIndexSql`
  alongside the table name, which the base implementation uses only for the table's schema — it renders
  `DROP INDEX IF EXISTS "IX_A"`, or `DROP INDEX IF EXISTS "<schema>"."IX_A"` when the table resolves to a
  schema, with no `ON <table>` clause; `MSSqlConnector` and `MySQLConnector` override `DropIndexSql` to add one

#### Scenario: Whitespace index name is rejected

- **Given** `DropAsync("   ", "Invoice")`
- **When** the call is made
- **Then** an `ArgumentException` `"Index name is required."` is thrown

### Requirement: Read-path index queries are not wrapped

The system SHALL wrap failures in `IndexManagementException` only for `CreateAsync` and `DropAsync`; the
inspection operations `ExistsAsync`, `ListAsync` and `GetInfoAsync` SHALL let the underlying provider
exception propagate unwrapped.

#### Scenario: A catalog query failure surfaces as a provider exception

- **Given** a table name that the catalog query cannot resolve, or a connection failure
- **When** `SqlIndexManager.ListAsync("Invoice")` is called
- **Then** the raw `DbException` (or connection exception) propagates, with no `IndexManagementException`
  wrapper and therefore no `IndexName`/`Scope` context

### Requirement: SQL index listing and grouping

The system SHALL run the dialect's `ListIndexesSql`, read each row positionally as
`(index_name, column_name, is_descending, is_unique, ordinal_position)` — the three numeric columns through
`Convert.ToInt32(reader.GetValue(i))`, so a provider returning them as `tinyint` or `bigint` reads alike — group
rows by index name, order each group's fields by ordinal, and take `Unique` from the group's **first** row.

#### Scenario: Multi-column index is reassembled

- **Given** catalog rows `("IX_TN","TenantGuid",0,1,1)` and `("IX_TN","Number",0,1,2)`
- **When** `ListAsync("Invoice")` runs
- **Then** one `IndexInfo` named `"IX_TN"` with `Unique == true` and fields `[TenantGuid, Number]` in ordinal
  order is returned

#### Scenario: SQL Server's tinyint ordinal is read

- **Given** MSSQL, whose catalog returns `key_ordinal` as `tinyint`
- **When** `ListAsync` reads the rows
- **Then** the value is converted rather than read with `GetInt32`, so the listing succeeds instead of raising
  `InvalidCastException`

#### Scenario: Uniqueness is assumed uniform across a group

- **Given** a group whose rows disagree on `is_unique`
- **When** grouping runs
- **Then** the first row's value is used for the whole index and the others are discarded

#### Scenario: Info lookup is a filtered list

- **Given** `GetInfoAsync("ix_tn", "Invoice")`
- **When** it runs
- **Then** the full `ListAsync` is executed and the first index whose name matches case-insensitively
  (`OrdinalIgnoreCase`) is returned, or `null` if none matches

### Requirement: Per-dialect catalog queries diverge

The system SHALL supply dialect-specific existence and listing SQL, each resolving the scope as a **logical**
table name through the connector (`PhysicalTable` for a stored name, `RelationLiteral` for a relation literal,
so a declared or connection-default schema is honoured): the default/MySQL implementation queries
`information_schema.statistics` restricted to `table_schema = DATABASE()`, excludes the `PRIMARY` index and reads
the direction from `collation = 'D'`; MSSQL queries `sys.indexes`/`sys.index_columns` by `OBJECT_ID` of the
rendered relation, reporting real descending keys and excluding primary keys, heap rows (`type > 0`) and included
columns; PostgreSQL queries `pg_class`/`pg_index`/`pg_attribute` for the relation `to_regclass` resolves, reading
descending keys from `indoption` subscripted by the column's position in `indkey`, and excluding primary indexes;
and SQLite filters existence by `tbl_name` and lists through the base `ListAsync` from `pragma_index_list` joined
to `pragma_index_xinfo`, keeping only `CREATE INDEX` indexes (`origin = 'c'`) and their key columns.

#### Scenario: Descending direction is reported on every provider

- **Given** an index created with a descending column, not necessarily the first
- **When** `ListAsync` runs
- **Then** the column's `IndexField.IsDescending` is `true` on MySQL (`collation = 'D'`), SQLite
  (`pragma_index_xinfo.desc`), MSSQL (`ic.is_descending_key`) and PostgreSQL
  (`indoption[array_position(indkey, attnum)]`, the column's position in the index rather than in the table)

#### Scenario: Primary keys are listed by no provider

- **Given** a table with a primary key and one secondary index
- **When** `ListAsync` runs
- **Then** MSSQL (`i.is_primary_key = 0`), PostgreSQL (`NOT pg_index.indisprimary`), SQLite (`origin = 'c'`, which
  also leaves out the automatic index of an inline `UNIQUE` constraint) and the default/MySQL query
  (`index_name <> 'PRIMARY'`) return only the secondary index

#### Scenario: Existence and listing are scoped to the connection's own table

- **Given** two databases/schemas reachable from the same connection, each holding a table `Invoice` with an
  index `IX_A`
- **When** `ExistsAsync("IX_A", "Invoice")` runs on MySQL or PostgreSQL
- **Then** only the table the connection resolves counts — MySQL's query is restricted to
  `table_schema = DATABASE()`, PostgreSQL's to the oid `to_regclass` returns for the table's relation (its
  declared schema, else the search path) — so another schema's index does not answer for it

#### Scenario: SQLite existence is filtered by the table

- **Given** index `IX_A` existing on table `Other` and not on table `Invoice`
- **When** `SqLiteIndexManager.ExistsAsync("IX_A", "Invoice")` runs
- **Then** the query also requires `tbl_name = 'Invoice'` and `false` is returned

#### Scenario: SQLite reads uniqueness from the index metadata

- **Given** a non-unique index named `IX_UNIQUE_CODE`
- **When** `SqLiteIndexManager.ListAsync` classifies it from `pragma_index_list`'s `unique` column
- **Then** it is reported as `Unique == false`; the name or DDL text no longer decides it

#### Scenario: SQLite lists through the base reader

- **Given** a caller holding the manager as `IIndexManager` or `SqlIndexManager`
- **When** `ListAsync` is invoked
- **Then** the inherited `SqlIndexManager.ListAsync` runs SQLite's `ListIndexesSql` override in one query, so
  the inherited `GetInfoAsync` sees the same populated columns

#### Scenario: MSSQL guards CREATE INDEX with a catalog test, on its connector

- **Given** MSSQL, which has no `CREATE INDEX IF NOT EXISTS`
- **When** any index statement is built for that dialect
- **Then** `MSSqlConnector.CreateIndexSql` emits
  `IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='<ix>' AND object_id=OBJECT_ID('<relation>')) CREATE [UNIQUE] INDEX ...`,
  a synthesised conditional form standing in for the clause the dialect lacks, where `<relation>` is the table
  as rendered with its schema, so the guard asks about the table the index goes on
- **And** `MSSqlIndexManager` declares no `CreateUniqueIndexSql` of its own — that override was deleted as
  byte-equivalent to what the connector already emits once `Unique` survives translation
- **And** MSSQL alone keeps its index **columns bracket-quoted**, deliberately unlike the base emitter, because
  its identifiers are case-insensitive under the default collation

#### Scenario: MySQL adds no manager behaviour because its connector carries the dialect

- **Given** `MySqlIndexManager`
- **When** any operation runs
- **Then** the inherited `SqlIndexManager` implementation is used unchanged — the subclass declares only a
  constructor — and the MySQL-specific statement shapes come from `MySQLConnector` instead: `CreateIndexSql`
  emits no `IF NOT EXISTS` (that clause is a syntax error on MySQL) and `DropIndexSql` emits
  `DROP INDEX <name> ON <table>` (MySQL accepts no `IF EXISTS` and requires the `ON` clause)
- **And** `CreateAsync` therefore succeeds against MySQL, where before it emitted statements the server
  rejected and failed wrapped in `IndexManagementException`
- **And** an index over an unbounded `string` column now succeeds on MySQL: `MySQLConnector.ConvertType`
  emits `VARCHAR(255)` — see `IndexedStringColumnLength` — for a string whose field carries
  `AbstractField.IsIndexed`, because MySQL cannot index a `LONGTEXT` column without a key length (error
  1170). Unindexed strings remain `LONGTEXT` and an explicit `[MaxLengthField(n)]` still wins
- **And** that bound is applied on MySQL **only**. SQLite, PostgreSQL and MSSql index a TEXT column natively
  and ignore `IsIndexed`, so a declaration that works on them today is not narrowed to 255 characters to suit
  a different provider
- **And** `byte[]` (`LONGBLOB`) is still unindexable on MySQL for the same 1170 reason; no bound is applied
  there because nothing declares such an index

### Requirement: A declared index marks its columns, so a provider can type them accordingly

The system SHALL set `AbstractField.IsIndexed` on every field a declared index names, at **both** points
where `DataBase.LoadIndexes` resolves an index column back to a field — the per-property `[IndexedField]`
branch and the class-level `[CompositeIndex]` branch — before any DDL is emitted.

#### Scenario: Both attribute forms mark the field

- **Given** one entity declaring its index with `[IndexedField]` and another with `[CompositeIndex]`
- **When** the table is loaded
- **Then** the named columns carry `IsIndexed = true` in both cases; marking only one branch would leave
  half the declarations looking unindexed to a provider whose column type depends on the flag

#### Scenario: Only indexed columns are affected

- **Given** an entity with one indexed and one unindexed unbounded `string`
- **When** the table is created on MySQL
- **Then** the indexed column is `VARCHAR(255)` and the unindexed one remains `LONGTEXT`

### Requirement: A fluent index reaches the table's index declarations, and marks its columns

`ModelMapRegistry.ApplyToDatabase` SHALL write every fluent `HasIndex` / `HasUniqueIndex` into the loaded table's
`Indexes` (the declarations schema-ensure creates), with columns ordered by `IndexOrder` and named by their physical
column. It SHALL set `IsIndexed` on each of those fields, as `DataBase.LoadIndexes` does for the attribute path,
so a provider bounds the column type. `FieldDescriptor.IndexUnique` SHALL make the index UNIQUE across all its
columns. One index whose columns disagree on uniqueness SHALL be refused. Applying the registry twice SHALL NOT
duplicate a column. (TASK-595: before this, the fluent index was metadata nothing read, and every
`Birko.Models.Users.SQL` table had zero indexes.)

#### Scenario: A composite natural key is unique on SQLite

- **Given** `UserLoginMapping`, which declares `HasUniqueIndex("UX_UserLogin_Provider_ProviderKey")` on Provider (0)
  and ProviderKey (1)
- **When** the mapping is applied and two logins with the same (Provider, ProviderKey) are created
- **Then** the second create throws, and `sqlite_master` lists `UX_UserLogin_Provider_ProviderKey` and
  `IX_UserLogins_UserGuid`

#### Scenario: A fluent index column is an index key for the provider

- **Given** the same mapping, whose `Provider` is an unbounded `string` (`HasPrecision` is metadata only)
- **Then** after `ApplyToDatabase`, `Provider`, `ProviderKey` and `UserGuid` have `IsIndexed = true`, and an
  unindexed column does not

### Requirement: Index-management SQL is composed by string interpolation

The system SHALL guard interpolated values by doubling single quotes for string literals through the
shared `SqlLiteral.EscapeLiteral`, which refuses a null value with `ArgumentNullException`, and by the
connector's `QuoteIdentifier` for identifiers; no index-management statement SHALL use a `DbParameter`.

#### Scenario: Quotes in a table name are escaped for the literal

- **Given** `ExistsAsync("IX_A", "Inv'oice")`
- **When** the existence SQL is built
- **Then** the table name appears as the literal `'Inv''oice'`

#### Scenario: Identifiers are interpolated, not parameterised — and columns are not quoted

- **Given** an index statement built for any dialect
- **When** the statement is built
- **Then** the index name and table name pass through `QuoteIdentifier` and are embedded in the command text;
  the executed command carries no parameters
- **And** index **column** names are embedded **bare** by the default emitter, not quoted: `CreateTable` emits
  column definitions bare, so a quoted column identifier cannot resolve the case-folded column PostgreSQL
  actually stores. MSSQL's override is the sole exception and quotes them

### Requirement: Index management opens its own connection

The system SHALL execute every SQL index-management statement on a connection newly created from
`Connector.CreateConnection(Connector.Settings)`, opened, used and disposed within the call.

#### Scenario: No participation in an ambient transaction

- **Given** an open store transaction on another connection
- **When** `CreateAsync` executes its `CREATE INDEX`
- **Then** the statement runs on a separate connection and is therefore outside that transaction's scope

#### Scenario: Reader work happens before disposal

- **Given** `ExecuteReaderAsync(sql, readAction, ct)`
- **When** it runs
- **Then** the connection is opened, the command executed, `readAction(reader)` invoked synchronously while
  the reader is live, and reader/command/connection disposed on exit

### Requirement: ElasticSearch index containers are created only when absent

The system SHALL check for existence before creating an ES index and SHALL throw
`InvalidOperationException("Index '<name>' already exists.")` when it is present; index names SHALL be
validated non-empty first. Existence SHALL be decided by the HTTP status alone — `200` exists, `404` does not —
and any other outcome, a transport failure with no status included, SHALL throw `InvalidOperationException`
("Failed to check whether index '<name>' exists: …") carrying the original exception.

#### Scenario: Creating an existing index is an error, not a no-op

- **Given** an ES index `invoices` that already exists
- **When** `IndexManager.CreateIndexAsync("invoices")` is called
- **Then** `InvalidOperationException` is thrown and no create request is issued

#### Scenario: Check-then-act is not atomic

- **Given** two callers creating the same index concurrently
- **When** both pass the existence check before either creates
- **Then** the second create request reaches the server and its failure surfaces through
  `ValidateResponse` as `InvalidOperationException`, not through the pre-check

#### Scenario: An unanswered existence check throws

- **Given** an `Indices.Exists` request that fails rather than answering — transport error, auth rejection,
  unreachable node — whose response therefore carries `Exists == false`
- **When** `IndexExists` / `IndexExistsAsync` inspects its HTTP status
- **Then** a status other than `200` or `404` (or none) throws `InvalidOperationException`, so neither the
  already-exists guard nor any other caller reads the failure as "the index does not exist"

#### Scenario: Blank index name is rejected everywhere

- **Given** any `IndexManager` method taking an index name
- **When** it is called with null, empty or whitespace
- **Then** `ArgumentException("Index name cannot be null or empty.")` with parameter name `indexName` is
  thrown

#### Scenario: Typed creation auto-maps and optionally sizes shards

- **Given** `CreateIndex<Invoice>("invoices", numberOfShards: 3, numberOfReplicas: 1)`
- **When** it runs
- **Then** the descriptor sets `NumberOfShards(3)`, `NumberOfReplicas(1)` and `Map<Invoice>(m => m.AutoMap())`;
  omitted values leave the ES defaults untouched

### Requirement: ElasticSearch index deletion is idempotent

The system SHALL return without error when asked to delete an ES index that does not exist, and SHALL
validate the response otherwise.

#### Scenario: Deleting a missing index succeeds silently

- **Given** no index named `invoices`
- **When** `DeleteIndexAsync("invoices")` is called
- **Then** the existence check short-circuits and the method returns with no request and no exception

#### Scenario: A failed existence check fails the deletion

- **Given** an existence request that fails rather than answering
- **When** `DeleteIndexAsync("invoices")` runs its existence check
- **Then** `InvalidOperationException` propagates and no delete request is issued — a failure is not reported
  as a completed deletion

### Requirement: ElasticSearch responses are validated uniformly, with 404 tolerated for removals

The system SHALL throw
`InvalidOperationException($"Failed to {operation}: {response.DebugInformation}")` carrying
`response.OriginalException` whenever a NEST response is not valid; except that `DeleteAlias`,
`GetAliases` and `DeleteTemplate` SHALL treat a `404` server status as success.

#### Scenario: A failed settings update throws with debug information

- **Given** `UpdateSettingsAsync` receiving an invalid response
- **When** validation runs
- **Then** an `InvalidOperationException` whose message begins
  `"Failed to update settings for 'invoices':"` is thrown, wrapping the original exception

#### Scenario: Deleting an absent alias is not an error

- **Given** an alias that does not exist
- **When** `DeleteAliasAsync("invoices", "current")` is called and ES answers 404
- **Then** no exception is thrown

#### Scenario: A non-404 alias failure does throw

- **Given** an alias delete that fails with status 400
- **When** the response is inspected
- **Then** `InvalidOperationException` naming alias and index is thrown

### Requirement: ElasticSearch mapping update targets the named index

The system SHALL validate the `indexName` argument of `UpdateMapping`/`UpdateMappingAsync` and SHALL apply the
caller's mapping descriptor to a descriptor already pointed at that index (`d.Index(indexName)`), so the mapping
lands on the named index.

#### Scenario: Mapping lands on the named index

- **Given** `UpdateMappingAsync<Invoice>("invoices_v2", m => m.Properties(...))` where the descriptor names
  no index
- **When** the call runs
- **Then** the put-mapping request goes to `invoices_v2`, not to the client's default index for `Invoice`

### Requirement: ElasticSearch index info aggregation

The system SHALL build an ES `IndexInfo` by combining the stats, settings and alias responses — document
count and primary store size from stats, shard and replica counts and refresh interval from settings, alias
names from a valid alias response — and SHALL validate the stats and settings responses but not the alias
response. Every lookup SHALL use the **concrete** index: the requested name when the stats response holds it,
else the stats response's only index (a name such as an alias resolving to exactly one), and otherwise
`InvalidOperationException` naming how many indexes the name resolves to; the info's `Name` is that concrete
index.

#### Scenario: An alias resolves to its one index

- **Given** alias `invoices` pointing at the single index `invoices_v1`
- **When** `GetIndexInfoAsync("invoices")` runs
- **Then** the info is named `invoices_v1` and carries that index's counts, settings and aliases, instead of
  coming back all zeros

#### Scenario: A name resolving to several indexes is refused

- **Given** a stats response holding two indexes, neither named as requested
- **When** `BuildIndexInfo` runs
- **Then** `InvalidOperationException` is thrown listing both and asking for one of them

#### Scenario: Missing settings entries degrade to zero

- **Given** a settings response that does not contain the concrete index key
- **When** `BuildIndexInfo` runs
- **Then** `NumberOfShards` and `NumberOfReplicas` stay `0` and `RefreshInterval` stays `null`

#### Scenario: Health and State are never populated

- **Given** any successful `GetIndexInfoAsync` call
- **When** the result is inspected
- **Then** `Health` is the class default `"unknown"` and `State` is the class default `"open"` — neither is
  read from ES

#### Scenario: Aliases require a valid alias response

- **Given** an alias response with `IsValid == false`
- **When** `BuildIndexInfo` runs
- **Then** `Aliases` remains the empty default and no exception is raised

#### Scenario: The async path fetches the three responses concurrently

- **Given** `GetIndexInfoAsync`
- **When** it runs
- **Then** stats, settings and alias requests are started before any is awaited and joined with
  `Task.WhenAll`

### Requirement: Alias operations support zero-downtime swaps

The system SHALL create and delete aliases per index, list aliases as an index-name → alias-names map, and
swap an alias between two indexes in a **single bulk alias request** combining the remove and the add.

#### Scenario: Swap is one atomic request

- **Given** alias `invoices` pointing at `invoices_v1`
- **When** `SwapAliasAsync("invoices", "invoices_v1", "invoices_v2")` is called
- **Then** one `BulkAlias` request containing both the remove and the add is issued, so no window exists in
  which the alias points nowhere

#### Scenario: Blank names are rejected before the request

- **Given** any of alias, old index or new index being null/empty/whitespace
- **When** `SwapAlias` is called
- **Then** an `ArgumentException` naming that parameter is thrown

#### Scenario: Alias listing is keyed by index

- **Given** two indexes each carrying aliases
- **When** `GetAliasesAsync()` is called with no index filter
- **Then** a dictionary mapping each index name to its list of alias names is returned; an invalid response
  that is not a 404 throws instead

### Requirement: ElasticSearch template and maintenance operations

The system SHALL provide index-template put/delete (rejecting a blank template name and a null descriptor,
tolerating 404 on delete) and the maintenance operations `ClearCache`, `Refresh` and `Flush`, which SHALL
target all indices when no index name is supplied.

#### Scenario: Refresh with no argument refreshes everything

- **Given** `RefreshAsync()` with `indexName == null`
- **When** it runs
- **Then** the request targets `Indices.All`

#### Scenario: Null template descriptor is rejected

- **Given** `PutTemplateAsync("t", null)`
- **When** it is called
- **Then** `ArgumentNullException` for `descriptor` is thrown

### Requirement: ElasticSearch adapter maps only shard and replica properties

The system SHALL adapt `IndexManager` to `IIndexManager` such that `CreateAsync` reads only
`Properties["NumberOfShards"]` and `Properties["NumberOfReplicas"]` into ES settings — each accepted as an
`int`, a `long` from 0 to `int.MaxValue`, or a string of decimal digits, and otherwise refused with
`NotSupportedException` — SHALL refuse any other property key, and `Unique`, `Sparse` and `ExpireAfter`, with
`NotSupportedException` raised before the index is created and not wrapped, and SHALL **ignore**
`IndexDefinition.Fields` entirely.

#### Scenario: Fields do not become mappings

- **Given** `new IndexDefinition { Name = "invoices", Fields = new[] { IndexField.Ascending("Number") } }`
- **When** `ElasticSearchIndexManagerAdapter.CreateAsync(definition)` runs
- **Then** the index is created with no descriptor derived from `Fields` — no explicit field mapping is
  applied and ES dynamic mapping governs the fields

#### Scenario: A textual shard count is honoured, a non-number refused

- **Given** `Properties["NumberOfShards"] = "3"` (a string)
- **When** `CreateAsync` runs
- **Then** it is parsed and the index is created with three shards; a value such as `"three"` or `2.5` throws
  `NotSupportedException` stating it must be a whole number, and an unknown key throws naming the supported
  ones

#### Scenario: Uniqueness cannot be expressed

- **Given** a definition with `Unique == true` (or `Sparse`, or an `ExpireAfter`)
- **When** `CreateAsync` runs against ES
- **Then** `NotSupportedException` is thrown and no index is created — an ES index is a container, not a
  constraint, so a flag it cannot honour is refused rather than dropped

#### Scenario: Native escape hatch is exposed

- **Given** a caller needing aliases, templates or reindexing
- **When** it reads `adapter.Native`
- **Then** the underlying ES-specific `IndexManager` is returned

### Requirement: ElasticSearch adapter exception translation

The system SHALL wrap any non-`IndexManagementException` thrown by ES create/drop into
`IndexManagementException` naming the index and the (ignored) scope, and SHALL let an existing
`IndexManagementException` pass through unchanged.

#### Scenario: Create failure is translated

- **Given** an ES index that already exists, so the native manager throws `InvalidOperationException`
- **When** `CreateAsync` runs
- **Then** an `IndexManagementException` with message `"Failed to create ES index '<name>'."` wrapping that
  exception is thrown

#### Scenario: Already-typed exceptions are not double-wrapped

- **Given** an inner failure that is itself an `IndexManagementException`
- **When** the `when (ex is not IndexManagementException)` filter is evaluated
- **Then** the original exception propagates unwrapped

### Requirement: ElasticSearch adapter listing reports failures

The system SHALL list ES indexes through the `_cat/indices` API, skipping any index whose name starts with
`"."`, mapping store size through a suffix parser and index status into `State`, exposing docs count, health,
primary shards and replicas as string entries in `Properties`; and SHALL throw `IndexManagementException` when
the cat response is not valid.

#### Scenario: A failed cat request is an error, not an empty cluster

- **Given** a cluster that rejects the `_cat/indices` request
- **When** `ListAsync()` runs
- **Then** an `IndexManagementException` whose message begins `"Failed to list ES indexes:"` is thrown, with
  `IndexName` `"*"`, the given scope, and the response's original exception as `InnerException`

#### Scenario: System indexes are hidden

- **Given** indexes `.kibana` and `invoices`
- **When** `ListAsync()` runs
- **Then** only `invoices` is returned

#### Scenario: Listed entries carry no field or uniqueness information

- **Given** any successful `ListAsync()`
- **When** an entry is inspected
- **Then** `Fields` is the empty default and `Unique` is `false`, because ES has no secondary-index concept
  to report

#### Scenario: Store size suffixes are parsed, unknown ones are not

- **Given** `_cat` store sizes `"1.5kb"`, `"2mb"`, `"512b"`, `"1.2tb"` and `null`
- **When** `ParseSizeToBytes` runs
- **Then** the first three yield `1536`, `2097152` and `512`; `"1.2tb"` matches the `"b"` suffix, leaves
  `"1.2t"` unparseable and yields `-1`; `null` yields `-1`

### Requirement: ElasticSearch adapter info lookup answers null only for an absent index

The system SHALL return `null` from `GetInfoAsync` only when the native existence check answers that the
index does not exist, SHALL let every other failure propagate, and SHALL otherwise project the ES info into the
portable shape with document count, shard/replica counts, health and aliases carried in `Properties`.

#### Scenario: A missing index reads as null

- **Given** an index name that does not exist, so the existence check answers `404`
- **When** `GetInfoAsync("nope")` runs
- **Then** `null` is returned without the stats request being made

#### Scenario: A cluster error is not read as an absent index

- **Given** an existence check, stats or settings request that fails for an unrelated reason
- **When** `GetInfoAsync` runs
- **Then** the `InvalidOperationException` propagates, so "index absent" and "cluster failed" stay
  distinguishable to the caller

#### Scenario: Blank name still throws

- **Given** `GetInfoAsync("")`
- **When** it is called
- **Then** `ArgumentException("Index name is required.")` is thrown rather than `null` being returned

### Requirement: Server-side reindex never throws

The system SHALL perform reindexing through the ES server-side reindex API, validate that source and target
are non-empty and differ case-insensitively, refresh the target on success, and report *every* other outcome
— including thrown exceptions — as a `ReindexResult` with `Success == false`. A `Reindex` / `ReindexAsync`
started with `waitForCompletion: false` SHALL answer `ReindexResult.Started` — `Success == false`,
`IsPending == true`, the task's `node:id` as `TaskId`, and an `ErrorMessage` telling the caller to follow
`GET _tasks/{TaskId}` and refresh the target once it completes — without refreshing the target; an invalid
response, or a valid one carrying no task id, SHALL be a failed result instead.

#### Scenario: Same source and target is rejected up front

- **Given** `ReindexAsync("invoices", "INVOICES")`
- **When** validation runs
- **Then** `ArgumentException("Source and target index names must be different.")` is thrown before any
  request — this is the one failure mode that throws rather than returning a result

#### Scenario: An invalid response becomes a failed result

- **Given** a reindex request that ES rejects
- **When** the response is inspected
- **Then** `ReindexResult.Failed` is returned carrying `response.DebugInformation` as `ErrorMessage`,
  `response.Created` as `DocumentsProcessed` and the failure count (`0` when `Failures` is null)

#### Scenario: Per-document failures in an HTTP 200 are a failed result

- **Given** ES answers HTTP 200 with one document listed in `failures` (a version conflict)
- **When** the response is inspected
- **Then** `Success == false` with `Failures == 1` — NEST's `IsValid` already folds `failures` in, measured at
  [[TASK-319]], so the invalid-response branch above carries it; this is pinned, not re-implemented

#### Scenario: A reindex that stopped early is a failed result

- **Given** ES answers HTTP 200 with no failures listed but the copy stopped early — `timed_out: true`, or a task
  cancelled mid-copy (`canceled`, which NEST 7 does not surface) — with `created: 1`, `total: 3`
- **When** the response is inspected
- **Then** `ReindexResult.Failed` is returned with `"timed out after accounting for 1 of 3 document(s)"` (or
  `"stopped after …"` for the cancelled case) and `DocumentsProcessed == 1`. `IsValid` passes such a response, so
  `ReindexHelper.IncompleteReason` judges completeness by the counts — `created + updated + noops +
  version_conflicts` must reach `total` — and by the `timed_out` flag, which wins even when the counts add up
  (SH-H038, [[TASK-319]]). NEST 7 exposes no `deleted` count, so a script that deletes documents is refused as
  incomplete: a loud false alarm rather than a silent swap

#### Scenario: A thrown exception becomes a failed result

- **Given** a transport exception during reindex
- **When** the catch block runs
- **Then** `ReindexResult.Failed(source, target, ex.Message)` is returned instead of the exception
  propagating

#### Scenario: Duration is lost on failure

- **Given** any failing path in `Reindex` / `ReindexAsync` / `ReindexWithScript`
- **When** the stopwatch is stopped and `ReindexResult.Failed` is built
- **Then** the elapsed time is not passed on and `Duration` stays `TimeSpan.Zero`, while the success path
  reports the real elapsed time

#### Scenario: Successful reindex makes documents searchable

- **Given** a successful reindex
- **When** the result is built
- **Then** the target index has been refreshed first, so the copied documents are immediately visible to
  search

#### Scenario: A reindex not waited for is pending, not successful

- **Given** `ReindexAsync("invoices_v1", "invoices_v2", waitForCompletion: false)` and ES accepting it as task
  `node1:42`
- **When** the result is built
- **Then** `Success == false`, `IsPending == true`, `TaskId == "node1:42"`, and the target is not refreshed — the
  copy has not happened yet; had ES answered without a task id, the result would be `Failed` saying so

#### Scenario: Script variant requires a script

- **Given** `ReindexWithScriptAsync(source, target, "  ")`
- **When** it is called
- **Then** `ArgumentException("Script source cannot be null or empty.")` is thrown; a valid script is sent as
  the reindex `Script.Source` with `WaitForCompletion(true)` forced

### Requirement: Zero-downtime reindex via alias swap

The system SHALL implement `ReindexWithAlias` as: resolve the alias to exactly one index, create the new
index, reindex (optionally with a script) waiting for completion, atomically swap the alias, and optionally
delete the old index; returning a successful result whose `SourceIndex` is the resolved old index. A failure to
delete the old index after a completed swap SHALL NOT fail the result: it SHALL be reported in the successful
result's `CleanupError`.

#### Scenario: The alias must resolve to exactly one index

- **Given** an alias pointing at zero indexes, or at two
- **When** `ReindexWithAlias("invoices", "invoices_v2")` is called
- **Then** `InvalidOperationException` is thrown — `"does not exist or points to no index."` or
  `"points to N indices. Zero-downtime reindex requires the alias to point to exactly one index."` — and
  because the resolution happens **before** the `try`, no `ReindexResult` is produced

#### Scenario: Alias matching is case-insensitive across all indexes

- **Given** index `invoices_v1` carrying alias `Invoices`
- **When** the alias `"invoices"` is resolved
- **Then** the comparison uses `OrdinalIgnoreCase` and `invoices_v1` is found

#### Scenario: A failed reindex rolls the new index back

- **Given** the reindex step returning `Success == false`
- **When** `ReindexWithAlias` handles it
- **Then** the newly created index is deleted and the failing `ReindexResult` is returned unchanged — the
  alias still points at the old index

#### Scenario: A partial copy never moves the alias or deletes the source

- **Given** `ReindexWithAlias(alias, new, deleteOldIndex: true)` whose reindex step timed out, was cancelled, or listed failures
- **When** it completes
- **Then** no alias action is sent, the old index still exists and the new index is deleted — before SH-H038 a
  timed-out copy was `Successful`, so the alias moved to the partial index and the only complete copy was deleted

#### Scenario: A failed swap leaves the new index behind

- **Given** a successful reindex followed by a `SwapAlias` failure
- **When** the exception is caught
- **Then** `ReindexResult.Failed(old, new, ex.Message)` is returned but the new index is **not** deleted, so
  a partially migrated index is left in the cluster

#### Scenario: The old index survives unless asked otherwise

- **Given** `deleteOldIndex` left at its `false` default
- **When** the swap succeeds
- **Then** the old index remains, allowing a manual rollback

#### Scenario: A failed cleanup does not undo a completed swap

- **Given** `deleteOldIndex: true`, a completed reindex and swap, and a delete of the old index that throws
- **When** `ReindexWithAlias` completes
- **Then** a successful result is returned with the delete's message in `CleanupError`, the alias pointing at the
  new index and the old index left for manual deletion — except that on the async path an
  `OperationCanceledException` from the delete is excluded from this handling, reaches the outer catch-all and
  is reported as a `Failed` result although the swap completed

#### Scenario: Typed overload auto-maps the new index

- **Given** `ReindexWithAlias<Invoice>("invoices", "invoices_v2", numberOfShards: 2)`
- **When** it runs
- **Then** the generic overload delegates to the descriptor-based one with a descriptor applying
  `NumberOfShards(2)` and `Map<Invoice>(m => m.AutoMap())`

### Requirement: MongoDB index manager maps the portable definition onto native index options

The system SHALL require a collection-name scope on every `MongoDBIndexManager` operation, throwing
`ArgumentException("Collection name (scope) is required for MongoDB index management.")` otherwise, and on
`CreateAsync` SHALL require a name and at least one field, build keys per field (`Text`, `Hashed`, `Geo2d`,
`Geo2dSphere`, else ascending/descending), and pass `Name`, `Background = true`, `Unique`, `Sparse` and
`ExpireAfter` as native options; `Properties` SHALL be ignored. The keys SHALL come from the public static
`MongoDBIndexManager.KeysFor(fields)`, the one producer the migration schema builder also uses. A create
failure SHALL be wrapped in `IndexManagementException`; a drop of an absent index (`IndexNotFound`) SHALL succeed
silently and other drop failures SHALL be wrapped. A text index SHALL be read back from its `weights` document —
one `Text` field per weighted field — rather than from its internal `_fts` / `_ftsx` key entries.

#### Scenario: Listing reports options and key types

- **Given** a collection with a unique sparse TTL index over a descending key and a text index
- **When** `ListAsync(collection)` runs
- **Then** each `IndexInfo` reports `Unique`, `Sparse`, `ExpireAfter` from `expireAfterSeconds`, fields with
  `IsDescending` from a negative numeric key and `FieldType` from a string key (`text`, `hashed`, `2d`,
  `2dsphere`), and `State == "ready"`

#### Scenario: A text index reads back as its declared fields

- **Given** a text index created over `Title`, whose stored key is `{ _fts: "text", _ftsx: 1 }` with
  `weights: { Title: 1 }`
- **When** it is listed
- **Then** its fields are exactly `[Title]` with `FieldType == Text`, not `_fts` and `_ftsx`, so drift detection
  does not report a declared text index as `ColumnsDiffer`

#### Scenario: Size comes from collStats when available

- **Given** `GetInfoAsync("IX_A", collection)`
- **When** the index exists
- **Then** its `SizeInBytes` is read from `collStats.indexSizes` when the command succeeds, and stays `-1`
  when it fails

#### Scenario: Convenience creators

- **Given** `CreateTtlIndexAsync("events", "CreatedAt", TimeSpan.FromDays(7))`
- **When** it runs
- **Then** an index named `ttl_CreatedAt` over `CreatedAt` ascending with that expiry is created

### Requirement: RavenDB index manager deploys map definitions database-wide

The system SHALL implement `RavenDBIndexManager.CreateAsync` by building a RavenDB index definition named
after the portable definition whose map is, in order of precedence, `Properties["Map"]` (a string),
`Properties["Maps"]` (strings), or an auto-generated
`from doc in docs.{scope ?? "docs"} select new { F = doc.F, … }` whose collection and field names must each be a
plain identifier (`[A-Za-z_][A-Za-z0-9_]*`, else `ArgumentException` advising to pass the map in `"Map"`),
with an optional `Properties["Reduce"]`, marking `Text` fields for full-text search, and sending it with
`PutIndexesOperation`; `Unique`, `Sparse` and `ExpireAfter` SHALL be refused with `NotSupportedException` before
anything is sent, each naming the RavenDB alternative. Send failures of create and drop SHALL be wrapped in
`IndexManagementException`. Existence SHALL be a case-insensitive match over all index names in the database;
listing and info SHALL come from index statistics, reporting `State` as `"stale"` or the lower-cased index
state and the type, priority, entry count and map/reduce counters in `Properties`. Listing SHALL report no
`Fields`; `GetInfoAsync` SHALL answer `null` for an index the existence check does not find, and for an index
whose single map is exactly in the generated form SHALL report that map's fields — `Text` where the field's
indexing is `Search`, else `Standard` — leaving `Fields` empty for any other map.

#### Scenario: Scope names the collection only in the generated map

- **Given** `CreateAsync(new IndexDefinition { Name = "Invoices/ByNumber", Fields = [Ascending("Number")] }, "Invoices")`
- **When** it runs
- **Then** the deployed map is `from doc in docs.Invoices select new { Number = doc.Number }`

#### Scenario: A name that is not an identifier never reaches the map code

- **Given** a field named `Number } select new { X = 1` or a scope such as `Invoices; x`
- **When** `CreateAsync` would generate the map
- **Then** `ArgumentException` is thrown and nothing is sent, because the generated map is code the server
  compiles

#### Scenario: A unique flag is refused

- **Given** a definition with `Unique = true` (or `Sparse`, or an `ExpireAfter`)
- **When** `CreateAsync` runs
- **Then** `NotSupportedException` is thrown — for `Unique` advising compare-exchange values — and no index is
  deployed

#### Scenario: Info about an absent index is null, a generated map reports its fields

- **Given** no index named `Nope`, and an index deployed by `CreateAsync` over `Number` and a `Text` field `Title`
- **When** `GetInfoAsync` is called for each
- **Then** `Nope` answers `null` instead of the statistics call throwing, and the deployed one reports fields
  `[Number (Standard), Title (Text)]`; an index whose map was passed in `"Map"` reports no fields

### Requirement: Cosmos DB index manager edits the container's indexing policy

The system SHALL require a container-name scope (`ArgumentException("Scope (container name) is required for
Cosmos DB.")`) and implement `CosmosDBIndexManager.CreateAsync` by requiring a name and at least one field,
refusing with `NotSupportedException` a definition that is `Unique` (a unique key holds within a logical
partition, and the Birko stores partition on `/id`), `Sparse`, has an `ExpireAfter`, carries any `Properties`,
declares a non-`Standard` field type, or is a single descending field; then adding a single-field definition as
an included path `/{name}/?` (removing that path from the excluded paths) or a multi-field definition as a
composite index with per-field sort order, replacing the container only when the policy changed. The
definition's `Name` SHALL be recorded, with its kind, paths and orders, in one registry document per container
(id and partition key = the container name) in the container `__birko_indexes`, created on first use and written
as raw JSON with an ETag precondition, retried up to 8 times on a concurrent write. Re-creating a registered name
over other paths SHALL throw `IndexManagementException` advising to drop it first. Failures other than
`ArgumentException`, `NotSupportedException` and `IndexManagementException` SHALL be wrapped in
`IndexManagementException`. The indexing policy SHALL stay the truth: a registered index exists only while its
paths are in the policy.

#### Scenario: Existence answers by the declared name

- **Given** an index created from a definition named `IX_Number` over field `Number`
- **When** `ExistsAsync("IX_Number", container)` is called
- **Then** it returns `true`, because the registry maps the name to `/Number/?` and that path is in the policy;
  a name nothing registered is still answered by path, so `ExistsAsync("Number", container)` is also `true`

#### Scenario: Listing names registered indexes, then unclaimed policy entries by path

- **Given** a registered `IX_Number` (included path `/Number/?`), a registered composite `IX_TN`, an included
  path `/Other/?` and a composite over `/A` and `/B` that nothing registered
- **When** `ListAsync(container)` runs
- **Then** it returns `IX_Number` and `IX_TN` (ordinal name order, `State = "active"`, their fields, `Type`
  `IncludedPath` / `CompositeIndex`, `Paths` and the indexing mode in `Properties`), then `/Other/?` with no
  fields, then the composite named `(/A ASC, /B ASC)` with its fields and directions; a registered index whose
  paths left the policy is not listed

#### Scenario: Dropping a registered composite removes exactly its paths

- **Given** a registered composite `IX_TN` over `/TenantGuid` and `/Number`
- **When** `DropAsync("IX_TN", container)` is called
- **Then** that composite is removed from the policy, the container replaced and the name removed from the
  registry — unless another registered name stands for the same paths, in which case the policy keeps them

#### Scenario: Dropping an unregistered name touches only an included path

- **Given** a name nothing registered
- **When** `DropAsync(name, container)` is called
- **Then** a matching included path is moved to the excluded paths and the container replaced; a composite is
  never dropped this way, and when nothing matches the container is not replaced

#### Scenario: Flags the policy cannot express are refused

- **Given** a definition with `Unique`, `Sparse`, an `ExpireAfter`, any `Properties`, a `Geo2dSphere` field, or a
  single descending field
- **When** `CreateAsync` runs
- **Then** `NotSupportedException` is thrown before the container or the registry is read
