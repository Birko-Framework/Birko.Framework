---
area: schema-index-and-ddl
generated-at: 715caf9d0e013cdacf990f2fd80c4c0d9ea5199a
generated-on: 2026-08-14
sources:
  - ../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/IndexInfo.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs
  - ../Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs
  - ../Birko.Data.Patterns/IndexManagement/IIndexManager.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexDefinition.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexInfo.cs
  - ../Birko.Data.Patterns/IndexManagement/IndexManagementException.cs
  - ../Birko.Data.Patterns/Schema/FieldDescriptor.cs
  - ../Birko.Data.Patterns/Schema/FieldType.cs
  - ../Birko.Data.Patterns/Schema/ICollectionBuilder.cs
  - ../Birko.Data.Patterns/Schema/IIndexBuilder.cs
  - ../Birko.Data.Patterns/Schema/ISchemaBuilder.cs
  - ../Birko.Data.SQL.MSSql/IndexManagement/MSSqlIndexManager.cs
  - ../Birko.Data.SQL.MySQL/IndexManagement/MySqlIndexManager.cs
  - ../Birko.Data.SQL.PostgreSQL/IndexManagement/PostgreSqlIndexManager.cs
  - ../Birko.Data.SQL.SqLite/IndexManagement/SqLiteIndexManager.cs
  - ../Birko.Data.SQL/Attributes/Field.cs
  - ../Birko.Data.SQL/Attributes/Table.cs
  - ../Birko.Data.SQL/SQL/DataBase_Field.cs
  - ../Birko.Data.SQL/SQL/DataBase_Table.cs
  - ../Birko.Data.SQL/SQL/Fields/AbstractField.cs
  - ../Birko.Data.SQL/SQL/Fields/BooleanField.cs
  - ../Birko.Data.SQL/SQL/Fields/CharField.cs
  - ../Birko.Data.SQL/SQL/Fields/BinaryField.cs
  - ../Birko.Data.SQL/SQL/Fields/DateTimeField.cs
  - ../Birko.Data.SQL/SQL/Fields/DecimalField.cs
  - ../Birko.Data.SQL/SQL/Fields/DoubleField.cs
  - ../Birko.Data.SQL/SQL/Fields/FloatField.cs
  - ../Birko.Data.SQL/SQL/Fields/GuidField.cs
  - ../Birko.Data.SQL/SQL/Fields/IntegerField.cs
  - ../Birko.Data.SQL/SQL/Fields/LongField.cs
  - ../Birko.Data.SQL/SQL/Fields/ShortField.cs
  - ../Birko.Data.SQL/SQL/Fields/StringField.cs
  - ../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs
  - ../Birko.Data.SQL/SQL/Tables/IndexDefinition.cs
  - ../Birko.Data.SQL/SQL/Tables/Table.cs
shaped-by: []
# false, not an empty answer: every source glob in this area points into a sibling repo, so no
# task's pr: sha resolves under `git show` in this aggregator and the evidence pass cannot run
# here at all. Treat shaped-by as unknown rather than as "no feature shaped this area".
shaped-by-derived: false
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
`ReindexHelper` for server-side and zero-downtime alias-swap reindexing.

The layers are deliberately *not* isomorphic, and the divergences below are load-bearing: the portable
`FieldType` enum names types the SQL field factory cannot map, and the portable `IndexDefinition` carries
flags several providers silently ignore.

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
`RenameField(collectionName, oldName, newName)`; all are synchronous and none returns a result object.

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
left unterminated by `Build()` therefore SHALL silently perform no work on those providers.

#### Scenario: Eager provider needs no terminator

- **Given** a schemaless provider whose `CreateCollection` already created the container
- **When** a migration calls `CreateCollection("x").WithField("a", FieldType.String)` and never calls `Build()`
- **Then** the collection still exists, because the inherited `Build()` is a no-op

#### Scenario: Deferred provider silently does nothing without Build()

- **Given** a provider that overrides `Build()` to emit the accumulated DDL
- **When** a migration calls `CreateIndex("Invoice", "IX_Number").WithField("Number")` and omits `Build()`
- **Then** no `CREATE INDEX` is emitted and the migration reports success — the omission is not detected

#### Scenario: Index builder accumulators

- **Given** an `IIndexBuilder`
- **When** `WithField(name, descending, fieldType)`, `Unique()`, `Sparse()` and `WithProperty(key, value)`
  are called
- **Then** each returns the same builder type for chaining, and `WithField` defaults to
  `descending: false` and `fieldType: IndexFieldType.Standard`

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

#### Scenario: Specialised field types are declared but unused by the shipped managers

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

### Requirement: SQL table discovery from attributes

The system SHALL resolve a model type to a `Tables.Table` by scanning the type's attributes (including
inherited ones) for `Birko.Data.SQL.Attributes.Table`, `System.ComponentModel.DataAnnotations.Schema.TableAttribute`,
or **any attribute whose full type name is `"Birko.Data.SQL.Attributes.Table"`** (the cross-assembly
shared-project case, read via reflection), taking the first attribute that yields a non-empty name; and
SHALL fall back to a name registered through `RegisterTableName` only when no such attribute produced a
table.

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
  its `Name` property is read reflectively

#### Scenario: Fluent registration is a fallback, not an override

- **Given** `[Table("Invoice")] class Invoice` and a prior call to
  `RegisterTableName(typeof(Invoice), "tbl_invoice")`
- **When** `LoadTable(typeof(Invoice))` is called
- **Then** the resulting table is named `"Invoice"` — the attribute branch returns first and the override is
  never consulted

#### Scenario: Registration invalidates the cache for that type only

- **Given** `LoadTable(typeof(Foo))` has already cached a table
- **When** `RegisterTableName(typeof(Foo), "other")` is called
- **Then** the entry is removed from `_tableCache` so the next `LoadTable` recomputes, while the separate
  `_fieldsCache` entry for `Foo` is left intact

#### Scenario: Unmapped type yields null through a non-nullable signature

- **Given** a type with neither a table attribute nor a registered name (or one whose `Fields` come out
  empty)
- **When** `DataBase.LoadTable(type)` is called
- **Then** `ComputeTable` returns `null`, nothing is cached, and `LoadTable` returns that `null` through its
  non-nullable `Tables.Table` return type via `return table!`

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
`[IgnoreField]` (Birko) or `[NotMapped]` (DataAnnotations).

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

### Requirement: CLR-type to SQL field mapping

The system SHALL map property CLR types to field classes as: `bool`/`bool?` → `BooleanField` /
`NullableBooleanField`; `DateTime`/`DateTime?` → `DateTimeField` / `NullableDateTimeField`;
`decimal`/`decimal?` → `DecimalField` / `NullableDecimalField` (carrying precision and scale);
`Guid`/`Guid?` → `GuidField` / `NullableGuidField`; `int`/`int?` → `IntegerField` /
`NullableIntegerField`; `long`/`long?` → `LongField` / `NullableLongField`; `short`/`short?` →
`ShortField` / `NullableShortField`; `double`/`double?` → `DoubleField` / `NullableDoubleField`;
`float`/`float?` → `FloatField` / `NullableFloatField`; `byte[]` → `BinaryField`; `char` → `CharField` of
length 1; `string` → `CharField` when an effective length greater than zero is known else `StringField`;
any enum or `Nullable<enum>` → `IntegerField` / `NullableIntegerField`; and SHALL throw
`Exceptions.FieldAttributeException` naming the declaring type, the property and its CLR type — rather
than returning `null` and silently skipping the property — for every other type.

#### Scenario: Enums are stored as integers

- **Given** `public InvoiceState State { get; set; }` where `InvoiceState` is an `int`-backed enum
- **When** the field is created
- **Then** an `IntegerField` with `DbType.Int32` and `IsNotNull == true` is produced

#### Scenario: Nullable enums map to the nullable integer field

- **Given** `public InvoiceState? State { get; set; }`
- **When** the field is created
- **Then** a `NullableIntegerField` is produced, resolved via `Nullable.GetUnderlyingType(...).IsEnum`

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

### Requirement: Reader materialisation per field type

The system SHALL materialise a column into its property through a type-specific `Read` override —
`GetBoolean`, `GetDateTime`, `GetDecimal`, `GetGuid`, `GetInt32`, `GetInt64`, `GetInt16`, `GetDouble`,
`GetFloat`, `GetFieldValue<byte[]>`, `GetString` — where the nullable variants first test
`reader.IsDBNull(index)` and assign `null`, while the non-nullable variants perform no null test at all;
and `AbstractField.Read` SHALL by default assign `reader.GetValue(index)` unconverted.

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
- **Then** the boxed enum is cast `(int)` and the integer returned; a null property value returns `null`
  before any cast

#### Scenario: String and char fields both null-check

- **Given** a `StringField` or a `CharField` and a NULL column
- **When** `Read` is called
- **Then** `null` is assigned (both implement the `IsDBNull` branch, `CharField` overriding `StringField`)

#### Scenario: CharField reads a string into a char property

- **Given** `public char Flag { get; set; }`, which `CreateAbstractField` maps to `CharField` with length 1
- **When** `CharField.Read` runs and assigns `reader.GetString(index)` via `Property.SetValue`
- **Then** a `string` is assigned to a `char` property and reflection raises an argument/type mismatch at
  runtime — `CharField` only round-trips `string` properties, not `char` ones

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

### Requirement: Primary-key resolution with a Guid fallback

The system SHALL return the fields marked `IsPrimary` for a type, and — when none is marked — SHALL fall
back to the field whose property is named `"Guid"`, returning an empty sequence only when neither exists.

#### Scenario: Attribute-declared primary keys win

- **Given** a model with `[PrimaryField] public int Id { get; set; }`
- **When** `DataBase.GetPrimaryFields(typeof(Model))` is called
- **Then** the `Id` field is returned

#### Scenario: Guid property is the implicit key

- **Given** an `AbstractModel` descendant with no `[PrimaryField]` anywhere
- **When** `GetPrimaryFields` is called
- **Then** the field mapped from the `Guid` property is returned, so UPDATE/DELETE can be qualified with a
  WHERE clause

#### Scenario: Neither key nor Guid yields an empty result

- **Given** a `[Table]` model with no `[PrimaryField]` and no `Guid` property
- **When** `GetPrimaryFields` is called
- **Then** an empty array is returned (callers must handle "no key" themselves)

### Requirement: Field resolution from a lambda expression

The system SHALL resolve a property access lambda to an `AbstractField` via `GetField<T,P>` and the
non-generic `GetFieldFromLambda`, accepting a `MemberExpression` body or a `UnaryExpression` wrapping one,
remapping properties reflected on `AbstractLogModel`/`AbstractModel` to their
`AbstractDatabaseLogModel`/`AbstractDatabaseModel` counterparts, and throwing `ArgumentException` when no
property can be resolved.

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

### Requirement: Per-property index discovery

The system SHALL build the table's index dictionary by scanning all public instance properties (including
inherited ones) for `[IndexedField]` — both the directly castable attribute and any attribute whose full
name is `"Birko.Data.SQL.Attributes.IndexedField"` from another assembly, read reflectively — grouping the
contributions by index name into one `Tables.IndexDefinition` per name, and SHALL mark the whole index
`Unique` if **any** contributing attribute sets `IsUnique`.

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

#### Scenario: An index on an unmapped property silently names a non-existent column

- **Given** `[IgnoreField] [IndexedField("IX_Bad")] public string Scratch { get; set; }` — the deliberate
  opt-out is now the only way to reach this, because an unsupported CLR type fails table load outright
  instead of producing a fieldless property
- **When** `LoadIndexes` runs
- **Then** no matching field is found and the column name falls back to the property name `"Scratch"`,
  producing an index definition over a column that the table does not have; no exception is raised

#### Scenario: A base-class IndexedField lands on every derived table

- **Given** `[IndexedField("IX_Tenant")]` declared on a shared base class property
- **When** `LoadIndexes` runs for each derived `[Table]` type
- **Then** every derived table's index dictionary contains `"IX_Tenant"` with the same database-global index
  name, because `type.GetProperties(Public | Instance)` includes inherited properties and `[IndexedField]`
  is `Inherited = true`

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

The system SHALL sort each index's columns by their `Order` value using `List<T>.Sort` after all
contributions are collected; `[IndexedField]` supplies `Order` from the attribute (default `0`) and
`[CompositeIndex]` supplies the positional index.

#### Scenario: Explicit orders are honoured

- **Given** contributions with `Order` `1` then `0` discovered in that reflection order
- **When** sorting completes
- **Then** the `Order == 0` column precedes the `Order == 1` column

#### Scenario: Tied orders have unspecified relative order

- **Given** a composite index declared as `[IndexedField("IX_AB")]` on two properties, both defaulting to
  `Order == 0`
- **When** `idx.Columns.Sort((a, b) => a.Order.CompareTo(b.Order))` runs
- **Then** the comparison reports the two columns equal and `List<T>.Sort` (an unstable sort) may emit them
  in either order, so the physical column order of the index is not determined by the declaration

#### Scenario: Mixing both attribute styles on one index name merges the columns

- **Given** an `[IndexedField("IX_A")]` on a property *and* a `[CompositeIndex("IX_A", ...)]` on the class
- **When** `LoadIndexes` runs
- **Then** both contributions accumulate into the single `"IX_A"` definition, with the composite's positional
  orders competing against the per-property orders — the columns are appended, not deduplicated

### Requirement: SQL index definition shape

The system SHALL represent a SQL index as `Tables.IndexDefinition` with a `Name`, a `Unique` flag, and an
always-initialised `Columns` list of `IndexColumn` (column name, `Order`, `IsDescending`).

#### Scenario: Columns list is never null

- **Given** `new Tables.IndexDefinition { Name = "IX_A" }`
- **When** `Columns` is read
- **Then** an empty list is returned, so callers may `Add` without initialising it

### Requirement: SQL index creation

The system SHALL, on `SqlIndexManager.CreateAsync`, validate that a scope, a non-empty definition name and
at least one field are present, translate the portable definition to a `Tables.IndexDefinition` preserving
field order and descending flags, emit `CreateUniqueIndexSql` when `definition.Unique` is set and otherwise
delegate to the connector's `CreateIndexSql`, and wrap any execution failure in `IndexManagementException`.

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

#### Scenario: Unique path bypasses the connector

- **Given** a definition with `Unique == true`
- **When** `CreateAsync` runs on the base manager
- **Then** `CreateUniqueIndexSql` emits
  `CREATE UNIQUE INDEX IF NOT EXISTS "<index>" ON "<table>" ("<col>" [DESC], ...)` using the connector's
  `QuoteIdentifier`, and the connector's `CreateIndexSql` is not called — the `IF NOT EXISTS` clause is
  accepted by SQLite and PostgreSQL (which overrides the method with the same text) but not by MySQL, which
  inherits this implementation

#### Scenario: Translation drops the Unique flag from the SQL definition

- **Given** a portable definition with `Unique == true`
- **When** `ToSqlIndexDefinition` builds the `Tables.IndexDefinition`
- **Then** only `Name` and `Columns` are copied — `Unique` is left `false` on the translated object, which is
  harmless only because the caller already branched on the portable flag

#### Scenario: Ordering and direction survive translation

- **Given** portable fields `[Descending("CreatedAt"), Ascending("Number")]`
- **When** translation runs
- **Then** the SQL columns are `CreatedAt` (Order 0, descending) and `Number` (Order 1, ascending) in that
  order

### Requirement: SQL index drop

The system SHALL drop an index by delegating to the connector's `DropIndexSql` with a name-only
`Tables.IndexDefinition`, and SHALL wrap any execution failure in `IndexManagementException` naming the
index and scope.

#### Scenario: Only the name is supplied to the dialect

- **Given** `DropAsync("IX_A", "Invoice")`
- **When** the SQL is built
- **Then** a `Tables.IndexDefinition { Name = "IX_A" }` with no columns is passed to `DropIndexSql`
  alongside the table name, which the base implementation then ignores — it renders
  `DROP INDEX IF EXISTS "IX_A"` with no `ON <table>` clause (only `MSSqlConnector` overrides `DropIndexSql`
  to add one), so the statement is accepted by SQLite and PostgreSQL but rejected by MySQL

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
`(index_name, column_name, is_descending, is_unique, ordinal_position)`, group rows by index name, order each
group's fields by ordinal, and take `Unique` from the group's **first** row.

#### Scenario: Multi-column index is reassembled

- **Given** catalog rows `("IX_TN","TenantGuid",0,1,1)` and `("IX_TN","Number",0,1,2)`
- **When** `ListAsync("Invoice")` runs
- **Then** one `IndexInfo` named `"IX_TN"` with `Unique == true` and fields `[TenantGuid, Number]` in ordinal
  order is returned

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

The system SHALL supply dialect-specific existence and listing SQL, and these dialects SHALL differ
observably: the default/MySQL implementation queries `information_schema.statistics` and **hard-codes
`is_descending` to 0** with no schema restriction; MSSQL queries `sys.indexes`/`sys.index_columns` reporting
real descending keys and excluding primary keys, heap rows (`type > 0`) and included columns; PostgreSQL
queries `pg_class`/`pg_index`/`pg_attribute` reading descending keys from `indoption` subscripted by the
column's table `attnum` rather than by its position within the index, and excluding primary indexes; and
SQLite reads `sqlite_master` plus `PRAGMA index_info` excluding `sqlite_autoindex_%`.

#### Scenario: Descending direction is lost on MySQL and SQLite

- **Given** an index created with a descending column
- **When** `ListAsync` runs on MySQL (base implementation) or SQLite (PRAGMA implementation)
- **Then** every returned `IndexField.IsDescending` is `false`; MSSQL reports the real direction from
  `ic.is_descending_key`, while PostgreSQL evaluates `indoption[a.attnum - 1]` — the column's position in the
  *table*, not in the index — so the flag is read from the wrong slot on a multi-column index and is absent
  (yielding `false`) whenever `attnum` exceeds the index's column count

#### Scenario: Primary keys appear only on MySQL

- **Given** a table with a primary key and one secondary index
- **When** `ListAsync` runs
- **Then** MSSQL (`i.is_primary_key = 0`), PostgreSQL (`NOT pg_index.indisprimary`) and SQLite
  (`name NOT LIKE 'sqlite_autoindex_%'`) return only the secondary index, while the default/MySQL query
  applies no such filter and also returns the `PRIMARY` entry

#### Scenario: Existence check is not schema-qualified

- **Given** two databases/schemas reachable from the same connection, each holding a table `Invoice` with an
  index `IX_A`
- **When** `ExistsAsync("IX_A", "Invoice")` runs on MySQL (`information_schema.statistics` filtered only by
  `table_name` and `index_name`) or PostgreSQL (`pg_indexes` filtered only by `tablename` and `indexname`)
- **Then** the count can be satisfied by the *other* schema's index and `true` is returned even when the
  target schema has none

#### Scenario: SQLite existence ignores the table entirely

- **Given** index `IX_A` existing on table `Other` and not on table `Invoice`
- **When** `SqLiteIndexManager.ExistsAsync("IX_A", "Invoice")` runs
- **Then** the query `SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_A'` matches and
  `true` is returned — the `tableName` argument is accepted but never used

#### Scenario: SQLite infers uniqueness from the DDL text

- **Given** an index whose stored `sql` text contains the word `UNIQUE` anywhere — including inside a column
  or index identifier such as `IX_UNIQUE_CODE`
- **When** `SqLiteIndexManager.ListAsync` classifies it via `CASE WHEN sql LIKE '%UNIQUE%' THEN 1 ELSE 0 END`
- **Then** it is reported as `Unique == true` regardless of whether the index actually enforces uniqueness

#### Scenario: SQLite overrides ListAsync rather than shadowing it

- **Given** a caller holding the manager as `IIndexManager` or `SqlIndexManager`
- **When** `ListAsync` is invoked
- **Then** the SQLite `override` runs (a two-step `sqlite_master` + `PRAGMA index_info` walk), so the
  inherited `GetInfoAsync` also sees populated columns instead of the base query's empty column names

#### Scenario: MSSQL guards CREATE UNIQUE INDEX with a catalog test

- **Given** MSSQL, which has no `CREATE INDEX IF NOT EXISTS`
- **When** `MSSqlIndexManager.CreateUniqueIndexSql` builds the statement
- **Then** it emits
  `IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='<ix>' AND object_id=OBJECT_ID('<table>')) CREATE UNIQUE INDEX ...`
  instead of the base `IF NOT EXISTS` clause used by the default and PostgreSQL implementations

#### Scenario: MySQL adds no behaviour

- **Given** `MySqlIndexManager`
- **When** any operation runs
- **Then** the inherited `SqlIndexManager` implementation is used unchanged — the subclass declares only a
  constructor — so `CreateAsync` emits `CREATE [UNIQUE] INDEX IF NOT EXISTS …` and `DropAsync` emits a
  table-less `DROP INDEX IF EXISTS …`, neither of which MySQL accepts, and both therefore fail against MySQL
  wrapped in `IndexManagementException`

### Requirement: Index-management SQL is composed by string interpolation

The system SHALL guard interpolated values by doubling single quotes for string literals
(`name.Replace("'", "''")`) and by the connector's `QuoteIdentifier` for identifiers; no index-management
statement SHALL use a `DbParameter`.

#### Scenario: Quotes in a table name are escaped for the literal

- **Given** `ExistsAsync("IX_A", "Inv'oice")`
- **When** the existence SQL is built
- **Then** the table name appears as the literal `'Inv''oice'`

#### Scenario: Identifiers are quoted, not parameterised

- **Given** `CreateUniqueIndexSql("Invoice", index)`
- **When** the statement is built
- **Then** the index name, table name and every column name pass through `QuoteIdentifier` and are embedded
  in the command text; the executed command carries no parameters

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
validated non-empty first.

#### Scenario: Creating an existing index is an error, not a no-op

- **Given** an ES index `invoices` that already exists
- **When** `IndexManager.CreateIndexAsync("invoices")` is called
- **Then** `InvalidOperationException` is thrown and no create request is issued

#### Scenario: Check-then-act is not atomic

- **Given** two callers creating the same index concurrently
- **When** both pass the existence check before either creates
- **Then** the second create request reaches the server and its failure surfaces through
  `ValidateResponse` as `InvalidOperationException`, not through the pre-check

#### Scenario: The existence check does not validate its own response

- **Given** an `Indices.Exists` request that fails rather than answering — transport error, auth rejection,
  unreachable node — whose response therefore carries `Exists == false`
- **When** `IndexExists` / `IndexExistsAsync` returns `response.Exists` with no `ValidateResponse` call
- **Then** the failure is reported as "the index does not exist", so the already-exists guard is skipped and
  a caller cannot distinguish an absent index from an unanswered question

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

#### Scenario: A failed existence check also reads as missing

- **Given** an existence request that fails rather than answering, so `exists.Exists` is `false` although the
  index may well exist
- **When** `DeleteIndexAsync("invoices")` inspects it
- **Then** the method short-circuits and returns successfully with no delete request issued — the unvalidated
  check makes a destructive no-op indistinguishable from a completed deletion

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

### Requirement: ElasticSearch mapping update ignores the index argument

The system SHALL validate the `indexName` argument of `UpdateMapping`/`UpdateMappingAsync` and then **not
use it**, issuing `_client.Map(mappingDescriptor)` / `MapAsync` so the target index is determined solely by
the caller's descriptor or the client's default index.

#### Scenario: Mapping lands wherever the descriptor says

- **Given** `UpdateMappingAsync<Invoice>("invoices_v2", m => m.Properties(...))` where the descriptor names
  no index
- **When** the call runs
- **Then** the request goes to the client's default index for `Invoice`, not to `invoices_v2`; the
  `"invoices_v2"` argument only influences the exception message on failure

### Requirement: ElasticSearch index info aggregation

The system SHALL build an ES `IndexInfo` by combining the stats, settings and alias responses — document
count and primary store size from stats, shard and replica counts and refresh interval from settings, alias
names from a valid alias response — and SHALL validate the stats and settings responses but not the alias
response.

#### Scenario: Missing entries degrade to zero

- **Given** a stats or settings response that does not contain the requested index key
- **When** `BuildIndexInfo` runs
- **Then** `DocumentCount`, `SizeInBytes`, `NumberOfShards` and `NumberOfReplicas` stay `0` and
  `RefreshInterval` stays `null`

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
`Properties["NumberOfShards"]` and `Properties["NumberOfReplicas"]` (each honoured only when the value is an
`int`) into ES settings, and SHALL **ignore** `IndexDefinition.Fields`, `Unique`, `Sparse` and `ExpireAfter`
entirely.

#### Scenario: Fields do not become mappings

- **Given** `new IndexDefinition { Name = "invoices", Fields = new[] { IndexField.Ascending("Number") } }`
- **When** `ElasticSearchIndexManagerAdapter.CreateAsync(definition)` runs
- **Then** the index is created with no descriptor derived from `Fields` — no explicit field mapping is
  applied and ES dynamic mapping governs the fields

#### Scenario: Non-int shard property is ignored

- **Given** `Properties["NumberOfShards"] = "3"` (a string)
- **When** `CreateAsync` runs
- **Then** the `s is int` test fails, no settings descriptor is built, and the index is created with ES
  defaults

#### Scenario: Uniqueness cannot be expressed

- **Given** a definition with `Unique == true`
- **When** `CreateAsync` runs against ES
- **Then** the flag is silently dropped — unlike `SqlIndexManager`, which branches on it

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

### Requirement: ElasticSearch adapter listing swallows failures

The system SHALL list ES indexes through the `_cat/indices` API, skipping any index whose name starts with
`"."`, mapping store size through a suffix parser and index status into `State`, exposing docs count, health,
primary shards and replicas as string entries in `Properties`; and SHALL return an **empty list** when the
cat response is not valid.

#### Scenario: A failed cat request is indistinguishable from an empty cluster

- **Given** a cluster that rejects the `_cat/indices` request
- **When** `ListAsync()` runs
- **Then** `Array.Empty<IndexInfo>()` is returned with no exception and no diagnostic — the caller cannot
  tell failure from "no indexes"

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

### Requirement: ElasticSearch adapter info lookup collapses failures to null

The system SHALL return `null` from `GetInfoAsync` whenever the native `GetIndexInfoAsync` throws
`InvalidOperationException`, and SHALL otherwise project the ES info into the portable shape with document
count, shard/replica counts, health and aliases carried in `Properties`.

#### Scenario: A missing index reads as null

- **Given** an index name that does not exist, so stats validation throws `InvalidOperationException`
- **When** `GetInfoAsync("nope")` runs
- **Then** `null` is returned

#### Scenario: A cluster error also reads as null

- **Given** a stats or settings request that fails for an unrelated reason (still surfacing as
  `InvalidOperationException` from `ValidateResponse`)
- **When** `GetInfoAsync` runs
- **Then** `null` is returned, so "index absent" and "cluster failed" are indistinguishable to the caller

#### Scenario: Blank name still throws

- **Given** `GetInfoAsync("")`
- **When** it is called
- **Then** `ArgumentException("Index name is required.")` is thrown rather than `null` being returned

### Requirement: Server-side reindex never throws

The system SHALL perform reindexing through the ES server-side reindex API, validate that source and target
are non-empty and differ case-insensitively, refresh the target on success, and report *every* other outcome
— including thrown exceptions — as a `ReindexResult` with `Success == false`.

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

#### Scenario: Script variant requires a script

- **Given** `ReindexWithScriptAsync(source, target, "  ")`
- **When** it is called
- **Then** `ArgumentException("Script source cannot be null or empty.")` is thrown; a valid script is sent as
  the reindex `Script.Source` with `WaitForCompletion(true)` forced

### Requirement: Zero-downtime reindex via alias swap

The system SHALL implement `ReindexWithAlias` as: resolve the alias to exactly one index, create the new
index, reindex (optionally with a script), atomically swap the alias, and optionally delete the old index;
returning a successful result whose `SourceIndex` is the resolved old index.

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

#### Scenario: A failed swap leaves the new index behind

- **Given** a successful reindex followed by a `SwapAlias` failure
- **When** the exception is caught
- **Then** `ReindexResult.Failed(old, new, ex.Message)` is returned but the new index is **not** deleted, so
  a partially migrated index is left in the cluster

#### Scenario: The old index survives unless asked otherwise

- **Given** `deleteOldIndex` left at its `false` default
- **When** the swap succeeds
- **Then** the old index remains, allowing a manual rollback

#### Scenario: Typed overload auto-maps the new index

- **Given** `ReindexWithAlias<Invoice>("invoices", "invoices_v2", numberOfShards: 2)`
- **When** it runs
- **Then** the generic overload delegates to the descriptor-based one with a descriptor applying
  `NumberOfShards(2)` and `Map<Invoice>(m => m.AutoMap())`
