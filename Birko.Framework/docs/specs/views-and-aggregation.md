---
area: views-and-aggregation
generated-at: de598e6
generated-on: 2026-08-16
sources:
  - ../Birko.Data.CosmosDB.Views/CosmosViewManager.cs
  - ../Birko.Data.CosmosDB.Views/CosmosViewStore.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewIndexResolver.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewManager.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs
  - ../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs
  - ../Birko.Data.MongoDB.Views/MongoViewManager.cs
  - ../Birko.Data.MongoDB.Views/MongoViewSerialization.cs
  - ../Birko.Data.MongoDB.Views/MongoViewStore.cs
  - ../Birko.Data.MongoDB.Views/MongoViewTranslator.cs
  - ../Birko.Data.RavenDB.Views/RavenViewManager.cs
  - ../Birko.Data.RavenDB.Views/RavenViewStore.cs
  - ../Birko.Data.RavenDB.Views/RavenViewTranslator.cs
  - ../Birko.Data.SQL.Views/SqlViewManager.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractAsyncConnector_SelectView.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnector_SelectView.cs
  - ../Birko.Data.SQL.View/SQL/DataBase_ViewOrderBy.cs
  - ../Birko.Data.SQL.Views/SqlViewStore.cs
  - ../Birko.Data.SQL.Views/SqlViewTranslator.cs
  - ../Birko.Data.Stores/AggregateHelper.cs
  - ../Birko.Data.Stores/AggregateMath.cs
  - ../Birko.Data.Stores/AggregateQuery.cs
  - ../Birko.Data.Stores/AggregateResult.cs
  - ../Birko.Data.Stores/IAggregatableStore.cs
  - ../Birko.Data.Stores/IAsyncAggregatableStore.cs
  - ../Birko.Data.Stores/OrderBy.cs
  - ../Birko.Data.Stores/OrderByHelper.cs
  - ../Birko.Data.Stores/TimeIntervalParser.cs
  - ../Birko.Data.Views/AggregateClause.cs
  - ../Birko.Data.Views/FieldSelector.cs
  - ../Birko.Data.Views/GroupByClause.cs
  - ../Birko.Data.Views/IViewManager.cs
  - ../Birko.Data.Views/IViewMapping.cs
  - ../Birko.Data.Views/IViewStore.cs
  - ../Birko.Data.Views/JoinClause.cs
  - ../Birko.Data.Views/JoinType.cs
  - ../Birko.Data.Views/ViewDefinition.cs
  - ../Birko.Data.Views/ViewDefinitionBuilder.cs
  - ../Birko.Data.Views/ViewMapRegistry.cs
  - ../Birko.Data.Views/ViewQueryMode.cs
  - ../Birko.Data.Views/ViewResult.cs
source-commits:   # sibling HEADs when this spec was last written (2026-08-16 16:17:32,
                  # commit c78cfca). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.CosmosDB.Views: 1b91192
  ../Birko.Data.ElasticSearch: 9b523e2
  ../Birko.Data.ElasticSearch.Views: 3881649
  ../Birko.Data.MongoDB.Views: 1a69f29
  ../Birko.Data.RavenDB.Views: 3b3f9dd
  ../Birko.Data.SQL.View: 6fa64c5
  ../Birko.Data.SQL.Views: 6095b16
  ../Birko.Data.Stores: c828ef1
  ../Birko.Data.Views: cd516af
shaped-by: []
# false, not an empty answer: every source glob in this area points into a sibling repo, so no
# task's pr: sha resolves under `git show` in this aggregator and the evidence pass cannot run
# here at all. Treat shaped-by as unknown rather than as "no feature shaped this area".
shaped-by-derived: false
---

# Fluent view definitions and aggregation queries

## Purpose

This capability lets an application declare a read-only *view* — a projection over one or
more entity types, optionally joined, grouped and aggregated — once, in a fluent,
platform-agnostic C# API, and then execute or materialize it on any of five persistence
backends: SQL, MongoDB, ElasticSearch, RavenDB and Azure Cosmos DB. A
`ViewDefinitionBuilder<TView>` produces an immutable `ViewDefinition`; a per-backend
*translator* converts that definition into the native query shape (SQL `SELECT`/`VIEW`, a
MongoDB aggregation pipeline, a RavenDB map/reduce index, NEST aggregations, Cosmos SQL);
a per-backend `IViewStore<TView>` executes it and a per-backend `IViewManager` manages the
persistent artifact's lifecycle.

Alongside the view layer sits a lower-level, store-facing aggregation contract:
`AggregateQuery<T>` / `AggregateResult` with the optional `IAggregatableStore<T>` and
`IAsyncAggregatableStore<T>` interfaces for backends with native aggregation, plus
`AggregateHelper` — a LINQ fallback that any store without native aggregation can use —
and the shared `OrderBy<T>`, `OrderByHelper` and `TimeIntervalParser` utilities that both
layers depend on.

The backends do **not** behave uniformly. Persistent-view support, join support,
group-by-only handling, and the meaning of `CountAsync` on an aggregate view all differ per
backend; those divergences are recorded explicitly below because they are the operational
reality of this capability.

## Regen provenance

Full regen at `a62ec94`. Supersedes a partial regen that carried its scope as prose in the `generated-at`
stamp itself, making it unparseable as a sha so the staleness check reported this area stale on format alone
(TASK-128 → `Birko.Data.SQL.View@576707c`).

Four of this area's 40 sources changed between the originating harvest and this regen —
`Birko.Data.SQL.View`'s `SQL/DataBase_ViewOrderBy.cs` and its two view-select connectors, plus a
documentation-only change to `Birko.Data.Stores/OrderBy.cs` — and all four were re-read here. The other 36,
spread across `Birko.Data.Views`, `Birko.Data.SQL.Views`, the Mongo / ElasticSearch / RavenDB / Cosmos view
projects and `Birko.Data.ElasticSearch`, have no commits since that harvest, so the requirements drawn from
them cannot have drifted.

Sibling-repo shas are recorded here rather than in the stamp because this repo cannot resolve them — every
source in this area lives in a sibling repo, so `generated-at` can only ever name this aggregator's HEAD.

## Requirements

### Requirement: Fluent view definition construction

The system SHALL let a caller build an immutable `ViewDefinition` through chained calls on
`ViewDefinitionBuilder<TView>`, where every builder method returns the same builder
instance, `HasQueryMode` defaults to `ViewQueryMode.OnTheFly`, `LeftJoin` is a shorthand for
`Join(..., JoinType.LeftOuter)`, `Sum`/`Avg`/`Min`/`Max` are shorthands for
`Aggregate(AggregateFunction.X, ...)`, and `Hint` accumulates into a dictionary whose later
value for the same key replaces the earlier one.

#### Scenario: Minimal on-the-fly definition

- **Given** a fresh `ViewDefinitionBuilder<OrderSummary>`
- **When** `From<Order>().Select<Order,string>(o => o.Number, v => v.Number).Build()` is called
- **Then** the returned `ViewDefinition` has `QueryMode == ViewQueryMode.OnTheFly`, `Name == null`, `PrimarySource == typeof(Order)`, `ViewType == typeof(OrderSummary)`, one `FieldSelector`, and `HasJoins`, `HasAggregates`, `HasGroupBy` all false

#### Scenario: LeftJoin records a LeftOuter join clause

- **Given** a builder with `From<Order>()`
- **When** `LeftJoin<Order, Customer, Guid>(o => o.CustomerGuid, c => c.Guid)` is called
- **Then** the built definition contains one `JoinClause` with `LeftType == typeof(Order)`, `RightType == typeof(Customer)`, `LeftProperty == "CustomerGuid"`, `RightProperty == "Guid"` and `JoinType == JoinType.LeftOuter`, and `HasJoins` is true

#### Scenario: Hint keys are last-write-wins

- **Given** a builder with `From<Order>()`
- **When** `Hint("MaterializedViewType", "A")` then `Hint("MaterializedViewType", "B")` are called and the definition is built
- **Then** `Hints["MaterializedViewType"]` is `"B"` and `Hints` has one entry

#### Scenario: Hints are copied at build time

- **Given** a builder that has recorded one hint and been built
- **When** a further `Hint("extra", 1)` call is made on the builder
- **Then** the already-built `ViewDefinition.Hints` still has one entry (Build copies the hint dictionary)

### Requirement: Member-name extraction from selector expressions

The system SHALL extract the property name from a selector lambda by accepting either a
direct `MemberExpression` body or a `UnaryExpression` (conversion) whose operand is a
`MemberExpression`, and SHALL throw `ArgumentException` with the message "Expression must be
a member access expression." for any other expression shape. `OrderBy<T>` applies the same
rule with the message "Expression must be a property access expression.".

#### Scenario: Boxed / converted member access is accepted

- **Given** a selector `Expression<Func<Order, object>> e = o => o.Total` (which the compiler wraps in a `Convert`)
- **When** it is passed to `Select`, `GroupBy`, `Aggregate` or `OrderBy<T>.By`
- **Then** the recorded property name is `"Total"`

#### Scenario: A non-member expression is rejected

- **Given** a selector `o => o.Total * 2`
- **When** it is passed to `ViewDefinitionBuilder.Select`
- **Then** an `ArgumentException` is thrown with parameter name `expression`

### Requirement: Build-time validation of the view definition

The system SHALL reject an invalid definition at `Build()` time with an
`InvalidOperationException`, in this order: (1) `From<TSource>()` was never called;
(2) `QueryMode` is `Persistent` or `Auto` while `Name` is null, empty or whitespace;
(3) aggregates are present, `GroupBy` is empty and at least one field is selected;
(4) aggregates are present, `GroupBy` is non-empty and some selected field's
`{SourceType.FullName}.{SourceProperty}` is absent from the set of grouped keys.

#### Scenario: Missing primary source

- **Given** a builder on which only `Select` was called
- **When** `Build()` runs
- **Then** an `InvalidOperationException` is thrown reading "From<TSource>() must be called to set the primary source entity."

#### Scenario: Persistent mode without a name

- **Given** a builder with `From<Order>()` and `HasQueryMode(ViewQueryMode.Persistent)` and no `HasName`
- **When** `Build()` runs
- **Then** an `InvalidOperationException` is thrown reading "HasName() is required for Persistent or Auto query modes."

#### Scenario: Aggregate with a selected field and no GroupBy

- **Given** a builder with `From<Order>()`, `Select<Order,string>(o => o.Status, v => v.Status)` and `Sum<Order,decimal>(o => o.Total, v => v.Total)`
- **When** `Build()` runs
- **Then** an `InvalidOperationException` is thrown telling the caller to `GroupBy()` each selected field or remove the `Select()` calls

#### Scenario: Aggregate with a selected field missing from GroupBy

- **Given** a builder with `From<Order>()`, `Select` of `Status` and `Currency`, `GroupBy<Order>(o => o.Status)` and a `Sum`
- **When** `Build()` runs
- **Then** an `InvalidOperationException` names the ungrouped field: "Field 'Currency' on 'Order' is selected but not in GroupBy."

#### Scenario: Aggregate-only view with no fields and no GroupBy is legal

- **Given** a builder with `From<Order>()` and only `Sum<Order,decimal>(o => o.Total, v => v.Total)`
- **When** `Build()` runs
- **Then** the definition builds successfully with `HasAggregates == true`, `HasGroupBy == false` and zero fields (a global aggregate)

#### Scenario: A GroupBy field that is not Selected passes builder validation

- **Given** a builder with `From<Order>()`, `GroupBy<Order>(o => o.Status)`, no `Select` calls, and a `Sum`
- **When** `Build()` runs
- **Then** the definition builds successfully — the builder only checks selected-field-implies-grouped, never grouped-field-implies-selected (the SQL backend rejects this shape later, see the SQL translation requirement)

### Requirement: Aggregate target-property validation

The system SHALL, for every aggregate clause including `Count`, require that the named view
property exists as a public property on `TView` and throw `InvalidOperationException`
otherwise, and SHALL additionally require that the view property's declared type is one of
the recognised numeric or nullable-numeric types (`byte`, `sbyte`, `short`, `ushort`, `int`,
`uint`, `long`, `ulong`, `float`, `double`, `decimal` and their `Nullable<>` forms) when the
function is `Sum` or `Avg`. `Count`, `Min` and `Max` may target any property type.

#### Scenario: Aggregate targets a missing view property

- **Given** `TView` has no property called `Total`
- **When** `Sum<Order,decimal>(o => o.Total, v => v.Total)` is built
- **Then** an `InvalidOperationException` is thrown: "View property 'Total' not found on '<TView>'."

#### Scenario: Sum onto a non-numeric view property

- **Given** `TView.Label` is a `string`
- **When** an aggregate `Sum` targeting `Label` is built
- **Then** an `InvalidOperationException` is thrown: "Aggregate Sum on 'Label' requires a numeric property type, but found 'String'."

#### Scenario: Min onto a DateTime view property is allowed

- **Given** `TView.Earliest` is a `DateTime`
- **When** `Min<Order,DateTime>(o => o.Created, v => v.Earliest)` is built
- **Then** the definition builds successfully — the numeric constraint applies only to `Sum` and `Avg`

#### Scenario: Count records a null source property

- **Given** a builder with `From<Order>()` and `Count<Order>(v => v.Rows)` where `Rows` is `int`
- **When** the definition is built
- **Then** the single `AggregateClause` has `Function == AggregateFunction.Count`, `SourceProperty == null` and `ViewProperty == "Rows"`

### Requirement: View mapping registry

The system SHALL let `ViewMapRegistry` hold at most one `ViewDefinition` per view type,
keyed by `Type`, populated either by `Register<TView>(IViewMapping<TView>)` — which
constructs a builder, calls `Configure`, and stores `Build()`'s result — or by
`RegisterFromAssembly(Assembly)`, which reflects over the assembly's concrete, non-abstract,
non-interface types implementing `IViewMapping<>` and registers each. Lookups
(`GetDefinition`, `HasDefinition`) SHALL return `null` / `false` for an unregistered type
rather than throwing.

#### Scenario: Registering the same view type twice keeps the last definition

- **Given** two `IViewMapping<OrderSummary>` implementations producing different definitions
- **When** both are passed to `Register`
- **Then** `GetDefinition<OrderSummary>()` returns the definition built from the second mapping

#### Scenario: Unregistered lookup returns null

- **Given** an empty registry
- **When** `GetDefinition<OrderSummary>()` and `HasDefinition(typeof(OrderSummary))` are called
- **Then** they return `null` and `false` respectively

#### Scenario: Assembly scan survives partially unloadable assemblies

- **Given** a type-enumeration delegate that throws `ReflectionTypeLoadException` carrying a `Types` array with some non-null entries
- **When** `ViewMapRegistry.GetLoadableTypes` invokes it
- **Then** the non-null entries are returned and the exception is not propagated

#### Scenario: A mapping whose Build throws propagates out of the scan

- **Given** an assembly containing an `IViewMapping<T>` whose `Configure` omits `From<TSource>()`
- **When** `RegisterFromAssembly` runs
- **Then** the `InvalidOperationException` raised by `Build()` surfaces (wrapped by reflection invocation) — the scan does not skip invalid mappings

### Requirement: Query-mode semantics

The system SHALL interpret `ViewQueryMode` as: `OnTheFly` — generate the query at runtime
against the source data; `Persistent` — query a pre-created persistent artifact; `Auto` — try
the persistent artifact and fall back to on-the-fly. Every `IViewManager.EnsureAsync`
implementation except Cosmos DB's SHALL return without doing work when the mode is
`OnTheFly`; `CosmosViewManager.EnsureAsync` SHALL be a no-op for every mode.

#### Scenario: OnTheFly Ensure is a no-op on SQL, MongoDB, ElasticSearch and RavenDB

- **Given** a definition with `QueryMode == ViewQueryMode.OnTheFly`
- **When** `EnsureAsync` is called on `SqlViewManager`, `MongoViewManager`, `ElasticSearchViewManager` or `RavenViewManager`
- **Then** the method returns without issuing any DDL, index-put or index-create call

#### Scenario: Cosmos Ensure never creates anything

- **Given** a definition with `QueryMode == ViewQueryMode.Persistent` and a name
- **When** `CosmosViewManager.EnsureAsync` is called
- **Then** it returns `Task.CompletedTask` without contacting the database — Cosmos DB has no native view support and its views are always computed on-the-fly

#### Scenario: MongoDB Auto mode falls back when the view is absent

- **Given** a `MongoViewStore` whose definition is `Auto` with name `"order_summary"` and no such view exists
- **When** `QueryAsync` runs
- **Then** the store checks the collection-name listing, finds nothing, and executes the base pipeline plus the query stages against the primary source collection instead of returning an empty result

#### Scenario: MongoDB Persistent mode uses the view unconditionally

- **Given** a `MongoViewStore` whose definition is `Persistent` with a name
- **When** `QueryAsync` runs
- **Then** the query stages are aggregated directly against the named view collection with **no** existence check and **without** prepending the base pipeline

### Requirement: Persistent-view creation is not an update on SQL and MongoDB

The system SHALL, on `SqlViewManager` and `MongoViewManager`, create the persistent artifact
only when it does not already exist and leave an existing artifact untouched even if the
`ViewDefinition` has since changed; on `RavenViewManager` it SHALL always send the translated
index definition, which replaces any existing index of that name.

#### Scenario: SQL Ensure skips an existing view

- **Given** a persistent SQL view named `"order_summary"` that already exists but was created from an older definition
- **When** `SqlViewManager.EnsureAsync` is called with the new definition
- **Then** `ViewExists`/`ViewExistsAsync` returns true and no `CREATE VIEW` is issued — the stale view remains

#### Scenario: MongoDB Ensure skips an existing view

- **Given** a MongoDB view named `"order_summary"` already listed among the database's collections
- **When** `MongoViewManager.EnsureAsync` is called
- **Then** it returns after the existence check without running the `create`/`viewOn`/`pipeline` command

#### Scenario: RavenDB Ensure replaces the index

- **Given** a RavenDB static index named `"order_summary"` built from an older definition
- **When** `RavenViewManager.EnsureAsync` is called with a new definition
- **Then** a `PutIndexesOperation` carrying the newly translated `Maps` (and `Reduce`, when the definition has aggregates) is sent, overwriting the existing index

### Requirement: View manager name validation and idempotent teardown

The system SHALL, in every backend's `DropAsync` and `ExistsAsync`, throw `ArgumentException`
for a null, empty or whitespace view name; of the five `RefreshAsync` implementations only
`ElasticSearchViewManager`'s validates the name, the SQL, MongoDB, RavenDB and Cosmos DB
no-ops returning `Task.CompletedTask` without inspecting it. The system
SHALL throw `InvalidOperationException` with the message "View name is required for
persistent views." from `EnsureAsync` when a non-`OnTheFly` definition resolves to no name
(SQL, MongoDB, ElasticSearch, RavenDB). `DropAsync` SHALL treat a missing artifact as
success on ElasticSearch (HTTP 404) and Cosmos DB (`CosmosException` with
`HttpStatusCode.NotFound`).

#### Scenario: Blank view name is rejected

- **Given** any of the five `IViewManager` implementations
- **When** `DropAsync("   ")` or `ExistsAsync("")` is called
- **Then** an `ArgumentException` is thrown with parameter name `viewName`

#### Scenario: Dropping a non-existent ElasticSearch view index succeeds

- **Given** no index named `"order_summary"` exists
- **When** `ElasticSearchViewManager.DropAsync("order_summary")` is called and the delete response carries `ServerError.Status == 404`
- **Then** the method returns normally instead of throwing

#### Scenario: Dropping a non-existent Cosmos container succeeds

- **Given** no container named `"order_summary"` exists
- **When** `CosmosViewManager.DropAsync("order_summary")` is called and `DeleteContainerAsync` throws `CosmosException` with `HttpStatusCode.NotFound`
- **Then** the exception is swallowed and the method returns normally

#### Scenario: Drop deletes a same-named real collection or container

- **Given** a MongoDB collection (not a view) named `"Order"`, or a Cosmos container named `"Order"`
- **When** `MongoViewManager.DropAsync("Order")` or `CosmosViewManager.DropAsync("Order")` is called
- **Then** the real collection / container and its documents are deleted — neither implementation verifies that the target is a view

### Requirement: Refresh is a no-op on every backend except ElasticSearch

The system SHALL implement `RefreshAsync` as an immediately-completed no-op on
`SqlViewManager`, `MongoViewManager`, `RavenViewManager` and `CosmosViewManager`, and on
`ElasticSearchViewManager` SHALL issue an index refresh against `viewName.ToLowerInvariant()`
and throw `InvalidOperationException` when the response is invalid or carries an original
exception.

#### Scenario: SQL refresh does nothing even for a materialized view

- **Given** a PostgreSQL materialized view backing a persistent definition
- **When** `SqlViewManager.RefreshAsync("order_summary")` is called
- **Then** `Task.CompletedTask` is returned and no `REFRESH MATERIALIZED VIEW` statement is executed; a derived class must override to do so

#### Scenario: ElasticSearch refresh lowercases the name and surfaces failure

- **Given** an ElasticSearch cluster that rejects a refresh of index `"order_summary"`
- **When** `ElasticSearchViewManager.RefreshAsync("Order_Summary")` is called
- **Then** the refresh targets `"order_summary"` and an `InvalidOperationException` is thrown whose message includes the NEST `DebugInformation`

### Requirement: Read-only view query contract

The system SHALL expose views through `IViewStore<TView>` only, offering
`QueryAsync(filter, orderBy, limit, offset, ct)`, `QueryFirstAsync(filter, ct)` returning
`null` when nothing matches, and `CountAsync(filter, ct)` returning a `long`; there SHALL be
no create, update or delete operation on a view store. A `null` filter SHALL mean
"no restriction" in every backend.

#### Scenario: No filter means all rows

- **Given** any `IViewStore<TView>` implementation
- **When** `QueryAsync()` is called with all arguments defaulted
- **Then** no predicate is applied: SQL passes `conditions == null`, MongoDB adds no `$match` stage, ElasticSearch uses `MatchAllQuery`, RavenDB and Cosmos DB omit the `Where` call

#### Scenario: QueryFirstAsync on an empty result returns null

- **Given** a view whose filter matches nothing
- **When** `QueryFirstAsync(filter)` is called
- **Then** `null` is returned (SQL `FirstOrDefault`, MongoDB `results.FirstOrDefault()` guard, ElasticSearch `results.FirstOrDefault()`, Raven `FirstOrDefaultAsync`, Cosmos `response.FirstOrDefault()`)

### Requirement: SQL view translation

The system SHALL translate a `ViewDefinition` into SQL `View` metadata by resolving each
referenced source type through `DataBase.LoadTable`, and SHALL fail fast with an exception
rather than silently dropping a column or a join, specifically: `ArgumentNullException` for a
null definition; `NotSupportedException` when a `GroupBy` field is not also `Select`ed;
`InvalidOperationException` for an unmapped source type, an unmapped source property, a
missing public instance view property, or a `Count(*)` whose base table has no fields.

#### Scenario: GroupBy without a matching Select is rejected

- **Given** a definition grouping by `Order.Status` where `Status` is not in `Fields`
- **When** `SqlViewTranslator.Translate` runs
- **Then** a `NotSupportedException` explains that the SQL view backend derives `GROUP BY` from the projected non-aggregate fields, so the group-by field must also be selected

#### Scenario: Unmapped source property is rejected

- **Given** a field selector referencing `Order.Nonexistent`
- **When** `SqlViewTranslator.Translate` runs
- **Then** an `InvalidOperationException` reads "View field '<view prop>' references unmapped source property 'Order.Nonexistent'."

#### Scenario: Count(*) uses the first table field as its base column

- **Given** an aggregate clause with `Function == Count` and `SourceProperty == null` over table `Order`
- **When** the translation runs
- **Then** the first entry of `table.Fields.Values` is used as the base column for the `COUNT` function field, and an `InvalidOperationException` is thrown if the table exposes no fields

#### Scenario: Two aggregates of the same function both survive translation

- **Given** a definition with `Sum(Order.Total) → TView.TotalSpent` and `Sum(Order.Tax) → TView.TotalTax` — the same SQL function over one table
- **When** `SqlViewTranslator.Translate` runs
- **Then** both aggregates are added to `View.Tables[…].Fields` under their **view property** keys (`TotalSpent`, `TotalTax`), so `GetPersistentViewSelectFields()` exposes both. Keying by the SQL function name would collide on `"SUM"`, and the second aggregate would be dropped with no column, no exception and no log entry, its property reading back as `default(T)`

#### Scenario: Two view properties projecting one source column both survive translation

- **Given** a definition with `Select(Person.Name → TView.DisplayName)` and `Select(Person.Name → TView.SortName)` — two view properties over one source column, which `ViewDefinitionBuilder.Build` does not reject
- **When** `SqlViewTranslator.Translate` runs
- **Then** both fields are present, keyed by their **view property** (`DisplayName`, `SortName`). Keying a non-aggregate by its source column name would put both under `"Name"` and the second would never populate

#### Scenario: An aggregate whose view property matches a neighbouring column's source name survives

- **Given** a definition selecting `Order.Total → TView.OrderTotal` alongside `Sum(Order.Amount) → TView.Total` — the aggregate's view property coincides with the non-aggregate's source column name
- **When** `SqlViewTranslator.Translate` runs
- **Then** both fields are present in the view metadata and read back their own values on the persistent path (the `OnTheFly` path has its own PostgreSQL defect, TASK-211). A per-table dictionary keyed by view property for aggregates and by source column for non-aggregates puts two namespaces in one key space, and whichever field is added second is lost

#### Scenario: A persistent view's columns are its view properties, spelled the way the read spells them

- **Given** any persistent (or `Auto`) view, aggregate or not
- **When** its DDL is generated and later read back
- **Then** all four producers of a column identifier agree on one rule — **quote table identifiers, never quote column identifiers**: the DDL projection qualifies with a quoted table and a bare column (`"AvPersons".Name`), aliases **every** column bare by its view property (`AS OrderCount`, `AS PersonName`), the join `ON` clause quotes only its table half, the persistent read selects those bare names, and the persistent `ORDER BY` interpolates the same bare view-property key. `GetPersistentViewSelectFields()` returns the **view property** for every column, aggregate or not

#### Scenario: A persistent view round-trips on PostgreSQL

- **Given** a persistent view over PascalCase models on PostgreSQL — the only supported provider that case-folds an unquoted identifier
- **When** the view is created and queried
- **Then** it is created and returns its rows. Previously it could not be created at all: base-table DDL emits column definitions bare so every base column is folded, and the view DDL emitted an unquoted table qualifier (`missing FROM-clause entry for table "avpersons"`), a quoted join column (`column AvOrders.PersonId does not exist`) and a quoted read (`column "Name" does not exist`) — three failures in front of one another, so no SQLite-passing test could observe any of them

#### Scenario: Two view properties over one source column get distinct output columns

- **Given** `Select(Person.Name → DisplayName)` and `Select(Person.Name → SortName)` on a persistent view
- **When** its DDL is generated
- **Then** each column is aliased by its own view property, so the view has two distinct output names. Aliasing only aggregates left both under the source column name — one duplicated identifier, which the persistent read (selecting by name) bound twice to the first column, and which MSSql and PostgreSQL reject outright at `CREATE VIEW`

### Requirement: SQL view store execution

The system SHALL execute SQL view queries through the connector, preferring genuine async
I/O — `SelectAsync` / `SelectCountAsync` on an `AbstractAsyncConnector`, threading the
`CancellationToken` — and falling back to synchronous `Select` / `SelectCount` otherwise;
SHALL parse the filter with `DataBase.ParseConditionExpression`; SHALL translate
`OrderBy<TView>` to a `Dictionary<string,bool>` (view property name → descending), returning
`null` when there are no sort fields — the connector resolves those keys against the view's
fields, so the store passes them through unvalidated on purpose (see *View sort keys are
resolved against the view's field metadata, per path*); SHALL check the token with `ThrowIfCancellationRequested`
before each operation; and SHALL materialize each row by reading reader columns
**positionally** against `View.GetTableFields()`.

#### Scenario: Async connector threads the cancellation token

- **Given** a `SqlViewStore` over an `AbstractAsyncConnector`
- **When** `QueryAsync(filter, orderBy, 10, 20, ct)` is called
- **Then** `SelectAsync(view, transform, conditions, orderFields, 10, 20, ct)` is enumerated and its items are collected

#### Scenario: QueryFirstAsync requests exactly one row and no ordering

- **Given** any `SqlViewStore`
- **When** `QueryFirstAsync(filter)` is called
- **Then** the select is issued with `orderBy == null`, `limit == 1`, `offset == null`

#### Scenario: Row materialization is positional

- **Given** a view whose `GetTableFields()` order differs from the reader's column order
- **When** the transform function runs
- **Then** field *i* reads reader column *i* for `i < min(tableFields.Length, reader.FieldCount)` — the mapping is by ordinal, not by column name, and surplus reader columns are ignored

#### Scenario: Non-TView rows are dropped on the async path but throw on the sync path

- **Given** a select that yields an object that is not a `TView`
- **When** the async path runs
- **Then** the item is skipped by the `item is TView` test; on the synchronous path the same object causes `.Cast<TView>()` to throw `InvalidCastException`

#### Scenario: A non-null filter is resolved against TView's own SQL mapping, not the view's fields

- **Given** a `SqlViewStore<OrderSummary>` built from a portable definition and the filter `v => v.Status == 3`
- **When** `DataBase.ParseConditionExpression` resolves `Status` against the lambda's parameter type `OrderSummary`
- **Then** resolution goes through `LoadTable(typeof(OrderSummary))` and then the `ResolveFieldSelectName` view hook, both of which require SQL mapping attributes on `TView`; the definition's `FieldSelector` list is never consulted, so for a plain portable view POCO the condition is left with no name and the query fails with `InvalidOperationException("Condition name cannot be null or empty")`

#### Scenario: Order-by keys are resolved against the view's fields, per path

- **Given** an aggregate view whose `Sum` of `Order.Total` targets `TView.Total`, queried with `OrderBy<TView>.By(v => v.Total)`
- **When** the select command is built
- **Then** the key is resolved by `DataBase.ResolveViewOrderFields` before it reaches the clause, and the resolved form follows the path: on the on-the-fly path `ORDER BY SUM(Order.Total) ASC` (what that SELECT list projects, aliased `as Total` — the aggregate's view property, read off `AbstractField.Property`), and on the persistent path `ORDER BY Total ASC` — emitted bare, while the column the view DDL created is `AS "Total"`, which is the pre-existing three-way quoting disagreement this area's sort requirement notes and TASK-209 owns. Before resolution existed the raw key was interpolated, so neither an aggregate nor a renamed view property matched an emitted column

### Requirement: View sort keys are resolved against the view's field metadata, per path

`OrderBy<TView>` carries **view property names** — `SqlViewStore.TranslateOrderBy` copies
`OrderByField.PropertyName` across, and `ByName` accepts an arbitrary caller string — while both view
`ORDER BY` emit sites interpolate their keys into `CommandText` unparameterised
(`CreatePersistentViewSelectCommand`, and the entity builder reached via
`CreateSelectCommand(DbCommand, Tables.View, …)`).

The system SHALL therefore resolve every view sort key through the view's field metadata before it reaches
either clause, in `Select(Tables.View, …)` / `SelectAsync(Tables.View, …)` — the one method every view read
passes through, and the only one that knows which path will run. It SHALL match on view property name first
— compared `Ordinal`, so case-sensitively — and then on source column name compared `OrdinalIgnoreCase`, and
SHALL throw `ArgumentException` naming the key and the view when neither matches. Because the emitted
identifier is always a name read out of that metadata, caller-supplied text cannot reach the clause. Unlike
the entity funnel's `ResolveOrderFields`, there is no third resolver hook: these two lookups are the whole
whitelist.

The system SHALL additionally throw `ArgumentException` naming every unresolved key when sort keys are
supplied but the view exposes no fields, rather than emitting them unresolved. A null or whitespace-only key
SHALL match nothing and raise the same unresolved-key error. Where two keys resolve to the same identifier —
a view property and its own source column are both accepted — the system SHALL collapse them into one clause
entry (last direction wins) rather than throwing.

The resolved form SHALL follow the path, because the two expose their columns under different names:
**on-the-fly** → `field.GetSelectName(true)`, the `Table.Column` form `View.GetSelectFields()` always
projects; **persistent** → `field.IsAggregate && field.Property != null ? field.Property.Name : field.Name`,
matching `GetPersistentViewSelectFields()` and the `AS "<ViewProperty>"` alias the view DDL emits for
aggregates. That DDL alias is the one column identifier the system quotes, because it *creates* the column and
the persistent SELECT list quotes it when reading it back; a bare alias would fold to lower case on PostgreSQL
and be unfindable. The `Property != null` arm is load-bearing rather than defensive: `AbstractField.Property` is
declared non-nullable but is assigned by the view builders, and a key matched on `Name` alone can reach this
point, so an aggregate field with no `Property` SHALL fall back to its source name instead of raising
`NullReferenceException` from a sort. The resolved identifier SHALL NOT be quoted, matching the on-the-fly
SELECT list (note the persistent SELECT list *does* quote its columns, so on that path alone the two
disagree).

#### Scenario: Sorting by a renamed view property works

- **Given** a view projecting `Person.Name` as the view property `PersonName`, queried with `OrderBy<TView>.By(v => v.PersonName)`
- **When** the read is enumerated
- **Then** the on-the-fly clause is `ORDER BY Persons.Name ASC` and the persistent clause is `ORDER BY Name ASC`, and the rows come back sorted. Before resolution both raised `no such column: PersonName` — so sorting a view by one of its own properties did not work at all, and only `ByName("<source column>")` did

#### Scenario: An injection payload is rejected on both paths

- **Given** `OrderBy<TView>.ByName("Name; CREATE TABLE Pwned (x INTEGER); --")`
- **When** the read is enumerated, on either the on-the-fly or the persistent path
- **Then** `ArgumentException` is thrown, no command runs and no such table is created. Before resolution the text reached `CommandText` verbatim on **both** paths and **the table was created**; the trailing ` ASC` the builders append is neutralised by the comment

#### Scenario: The expression-keyed connector overloads pass the view property name

- **Given** `SelectView<TView,P>(…)` or `Select<TView,P>(Tables.View, …)` with an expression-keyed order dictionary
- **When** the keys are prepared
- **Then** `DataBase.GetViewOrderKey` yields the member's own name — the view property — and resolution happens once in the funnel. Pre-resolving at these overloads (as they did, via `GetViewField(...).GetSelectName(true)`) could only ever suit one of the two paths, and it emitted the qualified source column, which a persistent view does not have

#### Scenario: A source column the view does not project is not a legal sort key

- **Given** a view that joins `Order` but selects only `Order.Guid` and `Person.Name`, queried with `OrderBy<TView>.ByName("Amount")`
- **When** the read is enumerated
- **Then** `ArgumentException` is thrown — resolution is scoped to what the view exposes, not to every column its source tables have

#### Scenario: The two lookups disagree on case, deliberately

- **Given** a view whose property is `PersonName` over source column `Name`, queried with
  `OrderBy<TView>.ByName("personname")`
- **When** the key is resolved
- **Then** the property lookup compares `Ordinal` and misses, then the source-column lookup compares
  `OrdinalIgnoreCase` and also misses (`personname` ≠ `Name`), so `ArgumentException` is thrown — whereas
  `ByName("NAME")` resolves via the case-insensitive column lookup. A view property must be named exactly;
  a source column need not be

#### Scenario: A view exposing no fields rejects its sort keys

- **Given** a non-empty sort dictionary and a view whose `GetTableFields()` yields nothing (or is null)
- **When** `ResolveViewOrderFields` runs
- **Then** `ArgumentException` is thrown naming every supplied key and reporting that the view exposes no
  fields — the keys are never emitted unresolved, which is what stops this path becoming a hole around the
  whitelist

#### Scenario: A blank sort key is an unresolved key

- **Given** `OrderBy<TView>.ByName("")` or `ByName("   ")`
- **When** the key is resolved
- **Then** `MatchViewOrderField` returns null on the `IsNullOrWhiteSpace` check before examining any field,
  so the caller raises the same unresolved-key `ArgumentException`

#### Scenario: A view property and its source column collapse to one clause entry

- **Given** a sort naming both the view property `PersonName` and its source column `Name`, which resolve to
  the same identifier on the queried path
- **When** the keys are resolved
- **Then** the resolved dictionary is written with the indexer rather than `Add`, so one entry survives
  carrying the later direction; no duplicate-key `ArgumentException` is raised

#### Scenario: An aggregate field with no view property sorts by its source name

- **Given** a persistent-view query whose sort key matched an aggregate field on `Name` alone, that field's
  `Property` being unset
- **When** the resolved form is computed
- **Then** `field.Name` is emitted rather than dereferencing the null `Property` — the persistent branch tests
  `Property != null` before preferring the alias, so a sort cannot throw `NullReferenceException`

#### Scenario: A non-property sort expression is rejected at the key-extraction step

- **Given** an expression-keyed order dictionary whose selector is not a property access (for example
  `v => v.Compute()`), or a null expression
- **When** `DataBase.GetViewOrderKey` runs
- **Then** a null expression raises `ArgumentNullException` and a non-property selector raises
  `ArgumentException` naming the expression — the failure is at key extraction, before any resolution or
  command building

### Requirement: MongoDB pipeline translation

The system SHALL translate a `ViewDefinition` into an ordered list of BSON aggregation
stages: one `$lookup` per join into the field `_joined_{RightTypeName}` followed by an
`$unwind` that sets `preserveNullAndEmptyArrays: true` only for `JoinType.LeftOuter`; a
`$group` stage when aggregates are present; and a final `$project` that always sets
`_id: 0`, maps each field selector to its view property, and includes each aggregate view
property as `1`. The `$project` stage SHALL be omitted entirely when it would contain nothing
but `_id: 0`. A property named `Guid` SHALL be translated to the Mongo field `_id`, and a
collection name SHALL be the CLR type's `Name`. `$match` SHALL never be part of the
translated pipeline — it is added at query time. A null definition SHALL raise
`ArgumentNullException`.

#### Scenario: Inner join does not preserve empty arrays

- **Given** a join with `JoinType.Inner` from `Order` to `Customer`
- **When** the pipeline is translated
- **Then** stages are `$lookup` (`from: "Customer"`, `as: "_joined_Customer"`) followed by `$unwind { path: "$_joined_Customer" }` with no `preserveNullAndEmptyArrays` key

#### Scenario: Cross join is translated identically to Inner

- **Given** a join with `JoinType.Cross`
- **When** the pipeline is translated
- **Then** the `$unwind` omits `preserveNullAndEmptyArrays` — the Cross join type is indistinguishable from Inner in the MongoDB translation

#### Scenario: Joined-source field paths are prefixed

- **Given** a field selector reading `Customer.Name` where `Customer` is not the primary source
- **When** the non-aggregate `$project` is built
- **Then** the projected value is `"$_joined_Customer.Name"`

#### Scenario: Guid maps to _id

- **Given** a field selector or join key named `Guid`
- **When** the field path is resolved
- **Then** the Mongo field name used is `_id` — which agrees with storage, because `MongoSerialization` maps the canonical `AbstractModel.Guid` to be the document's `_id`

### Requirement: A view type is class-mapped to mirror its projection

The system SHALL, on construction of a `MongoViewStore<TView>`, register a driver class map for
`TView` via `MongoViewSerialization.EnsureRegistered<TView>` that matches the projection
`MongoViewTranslator` emits for the same definition: a view property carrying an entity's canonical
`Guid` SHALL be string-represented, `TView` SHALL have no id member, and every element name SHALL
equal its property name. Registration SHALL use `TryRegisterClassMap`, so a consumer that mapped the
view type first keeps its own map, and SHALL raise `ArgumentNullException` for a null definition.

This exists because `MongoViewStore` renders its query-time `$match` through this same class map. A
map that disagrees with the projection therefore produces a filter that **matches nothing** rather
than an error — a silently wrong answer, not a failure.

#### Scenario: A filter on the canonical id matches the entity

- **Given** an entity stored through a Birko store and a view selecting its `Guid` into a `Guid?` view property
- **When** the view is queried with a filter comparing that property to the entity's `Guid`
- **Then** the entity is returned, because the rendered `$match` carries the id as a BSON string — the representation the projected `_id` actually holds

#### Scenario: A view property named Id is not treated as the document id

- **Given** a view type whose canonical-id property is named `Id`
- **When** a view row is deserialized
- **Then** it binds from the element `Id`, not `_id`, because the registered map clears the id member and pins element names to property names — the projection emits `_id: 0` and never produces an `_id` to bind

#### Scenario: Aggregate views project grouped fields from the root

- **Given** a definition with aggregates and a field selector for `Order.Status`
- **When** the `$project` is built
- **Then** the projection is `Status → "$Status"` (the *source* property at root level after `$group`), not a joined path

### Requirement: MongoDB view store execution

The system SHALL build the base pipeline once at construction and apply query-time stages —
`$match` from the rendered `Builders<TView>.Filter.Where(filter)`, `$sort` from
`OrderBy<TView>`, `$skip` and `$limit` — over the *view's* field shape, prepending the base
pipeline only on the on-the-fly path. `$skip` SHALL be emitted only for an offset strictly
greater than zero and `$limit` only for a limit strictly greater than zero. `CountAsync`
SHALL append `$count: "count"` and return `0` when the pipeline yields no document.

#### Scenario: Limit of zero is ignored

- **Given** `QueryAsync(filter, null, limit: 0, offset: 0)`
- **When** the stages are built
- **Then** neither `$skip` nor `$limit` is added, so the query returns every matching document rather than none

#### Scenario: Negative offset is ignored

- **Given** `QueryAsync(filter, null, null, offset: -5)`
- **When** the stages are built
- **Then** no `$skip` stage is emitted

#### Scenario: Count over an empty result

- **Given** a filter matching no documents
- **When** `CountAsync(filter)` runs
- **Then** the `$count` stage yields no document and the method returns `0`

#### Scenario: Base pipeline is not re-applied to a persistent view

- **Given** a `Persistent` definition with joins and aggregates
- **When** `QueryAsync` runs
- **Then** only `$match`/`$sort`/`$skip`/`$limit` are aggregated against the named view — re-running `$lookup`/`$group`/`$project` over already-projected documents would produce wrong or empty results

### Requirement: RavenDB map/reduce translation

The system SHALL translate a `ViewDefinition` into a `(map, reduce)` string pair where `map`
is always produced and `reduce` is `null` unless the definition has aggregates. The map SHALL
read `from entity in docs.{PrimarySource.Name}`, emit one
`let joined[N] = LoadDocument<{RightType.Name}>(entity.{LeftProperty})` per join (aliases
`joined`, `joined2`, `joined3`, …), and select each field as
`{ViewProperty} = {alias}.{SourceProperty}`. For aggregates the map SHALL emit `1` for
`Count`, the raw source value for `Sum`/`Min`/`Max`, and for `Avg` both the raw value and a
`{ViewProperty}_Sum` plus a `{ViewProperty}_Count = 1` helper. A null definition SHALL raise
`ArgumentNullException`.

#### Scenario: Join right-hand key and join type are ignored

- **Given** a `LeftJoin<Order, Customer, Guid>(o => o.CustomerGuid, c => c.Guid)`
- **When** the map is built
- **Then** it emits `let joined = LoadDocument<Customer>(entity.CustomerGuid)` — `RightProperty` (`Guid`) and `JoinType` (`LeftOuter`) never appear; the translation assumes the left property holds a document id

#### Scenario: A source type not found among join right-hand types falls back to entity

- **Given** a field selector whose `SourceType` is neither the primary source nor any join's `RightType`
- **When** `GetSourceAlias` resolves it
- **Then** `"entity"` is returned, so the map reads the property off the primary-source document

#### Scenario: Average is re-reduce safe

- **Given** an `Avg` aggregate on `Order.Total` targeting `TView.AvgTotal`
- **When** map and reduce are built
- **Then** the map emits `AvgTotal`, `AvgTotal_Sum` and `AvgTotal_Count = 1`, and the reduce emits `AvgTotal_Sum = g.Sum(x => x.AvgTotal_Sum)`, `AvgTotal_Count = g.Sum(x => x.AvgTotal_Count)` and `AvgTotal = g.Sum(x => x.AvgTotal_Sum) / g.Sum(x => x.AvgTotal_Count)` so incremental re-reduce cannot double-average

#### Scenario: Global aggregate groups by a constant

- **Given** an aggregate-only definition with no `GroupBy` clauses and no field selectors
- **When** the reduce is built
- **Then** it emits `group result by 1 into g` rather than the invalid `group result by new {  }` an empty composite key would produce

#### Scenario: No GroupBy but fields present groups by every projected field

- **Given** an aggregate definition with field selectors and no `GroupBy` clauses (only reachable when constructed outside the builder's validation)
- **When** the reduce is built
- **Then** every `field.ViewProperty` becomes part of the group key

#### Scenario: A GroupBy field with no matching selector falls back to its source name

- **Given** `GroupBy<Order>(o => o.Status)` where no field selector maps `Order.Status`
- **When** `FindViewPropertyForGroupBy` resolves it
- **Then** the raw property name `"Status"` is used as the reduce key, which must coincidentally match a name the map emitted

### Requirement: RavenDB view store execution

The system SHALL choose the Raven query source from the mode: `OnTheFly` — a dynamic
`session.Query<TView>()`; `Auto` — the static index when a name is present, else a dynamic
query; `Persistent` — the static index by name, throwing `InvalidOperationException` when no
name is set. It SHALL apply the filter with `Where`, ordering with `OrderByHelper.ApplyTo`,
then `Skip`/`Take`, and SHALL throw `InvalidOperationException` if the resulting queryable is
not an `IRavenQueryable<TView>`. Each call SHALL open and dispose its own async session.

#### Scenario: OnTheFly ignores the view definition entirely

- **Given** an `OnTheFly` definition with joins, aggregates and renamed field selectors
- **When** `QueryAsync` runs
- **Then** `session.Query<TView>()` issues a plain dynamic query over the collection Raven infers from `TView`; `Fields`, `Joins`, `Aggregates` and `GroupBy` are never consulted and `RavenViewTranslator` is never invoked

#### Scenario: Persistent without a name is rejected

- **Given** a `Persistent` definition whose `Name` is null (constructed directly, bypassing the builder)
- **When** `QueryAsync` runs
- **Then** `InvalidOperationException` is thrown: "View name (index name) is required for Persistent query mode."

#### Scenario: Count with a filter uses the server-side overload

- **Given** a filter expression
- **When** `CountAsync(filter)` is called
- **Then** `query.CountAsync(filter, ct)` is used rather than materializing results

### Requirement: ElasticSearch view index resolution

The system SHALL resolve the ElasticSearch index for a view to
`definition.Name.ToLowerInvariant()` only when the mode is `Persistent` **and** the name is
non-empty, and otherwise to `definition.PrimarySource.Name.ToLowerInvariant()`, and the store
and the manager SHALL both use this one resolver so they cannot target different indices.

#### Scenario: Persistent named view resolves to its own index

- **Given** a `Persistent` definition named `"Order_Summary"`
- **When** `ElasticSearchViewIndexResolver.Resolve` runs
- **Then** `"order_summary"` is returned

#### Scenario: Auto mode resolves to the primary source index

- **Given** an `Auto` definition named `"order_summary"` over primary source `Order`
- **When** the resolver runs
- **Then** `"order"` is returned, and `ElasticSearchViewManager.EnsureAsync` for that definition therefore creates the `"order"` index (not `"order_summary"`) if it is missing

### Requirement: ElasticSearch view store execution

The system SHALL route a view query with aggregates through an aggregation search
(`Size = 0`) and a view query without aggregates through a document search with `_source`
include-filtering, and SHALL throw `InvalidOperationException` including the NEST
`DebugInformation` whenever a response is invalid or carries an `OriginalException`.
Join clauses SHALL be ignored — only primary-source fields are queried. The effective page
size SHALL be clamped so `From + Size` never exceeds 10000.

#### Scenario: Aggregate query ignores ordering and offset

- **Given** an aggregate view and `QueryAsync(filter, OrderBy<TView>.ByDescending(v => v.Total), limit: 50, offset: 100)`
- **When** the query executes
- **Then** the search request carries `Size = 0`, the filter query and the aggregations only; `orderBy` and `offset` are never passed to `ExecuteAggregateQueryAsync` and have no effect, while `limit` becomes the group-by bucket `size`

#### Scenario: Window clamping

- **Given** `ClampWindowSize` is called
- **When** the arguments are `(from: 0, limit: null)`, `(from: 9500, limit: 1000)` and `(from: 10000, limit: 100)`
- **Then** the results are `10000`, `500` and `0` respectively; a negative `from` is treated as `0` for the clamp calculation

#### Scenario: A negative offset still reaches ElasticSearch

- **Given** `QueryAsync(offset: -5)` on a non-aggregate view
- **When** the search request is built
- **Then** `From = -5` (only the local copy inside `ClampWindowSize` is normalised), so ElasticSearch receives an invalid `from`

#### Scenario: An untranslatable filter is not widened to match-all

- **Given** a filter expression `ElasticSearch.ParseFilterQuery` cannot translate
- **When** `BuildFilterQuery` runs
- **Then** the helper throws rather than returning null, so the query never silently becomes a full-result set; a null filter, by contrast, deliberately yields `MatchAllQuery`

#### Scenario: Count ignores grouping and aggregation

- **Given** an aggregate view grouping 1 000 000 orders into 12 status buckets
- **When** `CountAsync()` is called
- **Then** a `CountRequest` against the resolved index returns `1000000` — the raw document count, not the number of groups

#### Scenario: Grouped aggregate parsing prefers composite over terms

- **Given** an aggregation response containing a `group_by` aggregation
- **When** `ParseGroupedAggregateResponse` runs
- **Then** the composite form is consumed if present; otherwise the terms form is used and only `GroupBy[0]` is populated from `bucket.Key`; if neither is present an empty sequence is returned

#### Scenario: Enum and Guid view properties round-trip

- **Given** a view property of an enum type receiving the string `"Active"`, and a `Guid` property receiving a string GUID
- **When** `ConvertValue` runs
- **Then** `Enum.Parse(..., ignoreCase: true)` and `Guid.Parse` are used; a value whose shape does not match the target type causes `ConvertValue` to return `false` and the property is left at its default with no error

#### Scenario: Non-writable or null values are skipped

- **Given** a get-only view property, or an aggregation that returned no value
- **When** `SetPropertyValue` runs
- **Then** it returns without assigning

### Requirement: ElasticSearch aggregation building and parsing

The system SHALL build one NEST metric aggregation per `AggregateField` keyed by
`ResolvedAlias`, mapping `Sum`→`SumAggregation`, `Avg`→`AverageAggregation`,
`Min`→`MinAggregation`, `Max`→`MaxAggregation` and `Count`→`ValueCountAggregation` over the
source field or `"_id"` when no field is given, and SHALL throw `NotSupportedException` for
any other function value. Grouping SHALL use a `TermsAggregation` named `"group_by"` for a
single field and a `CompositeAggregation` named `"group_by"` for two or more, with a default
bucket `size` of 10000. Extracted metric values SHALL always be surfaced as `double?`.

#### Scenario: Count without a source field counts _id

- **Given** an `AggregateField(Count, sourcePropertyName: null-or-empty)` passed as `fieldName == null`
- **When** `BuildSingleMetricAggregation` runs
- **Then** a `ValueCountAggregation` over `"_id"` is produced

#### Scenario: Two group-by fields produce a composite aggregation

- **Given** `groupByFields == ["Status", "Currency"]`
- **When** `BuildGroupByAggregation` runs
- **Then** a `CompositeAggregation("group_by")` with one `TermsCompositeAggregationSource` per field and `Size = 10000` is produced

#### Scenario: Time-bucketed response parsing

- **Given** a query with a time-bucket interval and a response containing a `time_bucket` date histogram
- **When** `ParseAggregateResponse` runs with `hasTimeBucket == true`
- **Then** one `AggregateResult` per histogram bucket is produced with `bucket_time` set to the bucket's `Date`; when the query also groups, each nested group bucket yields its own result carrying the same `bucket_time`

#### Scenario: A missing time_bucket aggregation yields no results

- **Given** `hasTimeBucket == true` but the response has no `time_bucket` date histogram
- **When** `ParseAggregateResponse` runs
- **Then** an empty list is returned with no error

#### Scenario: Unparseable interval silently becomes one hour

- **Given** the interval string `"fortnightly"`
- **When** `StoreAggregationHelper.ParseToTime` runs
- **Then** `TimeIntervalParser.Parse` yields `TimeSpan.Zero` and a `Nest.Time("1h")` is returned

#### Scenario: Multi-field terms grouping loses its keys

- **Given** a response parsed with `hasGroupBy == true`, more than one `GroupByFields` entry, and a `group_by` **terms** aggregation
- **When** `ParseAggregateResponse` runs
- **Then** the `if (query.GroupByFields.Count == 1)` guard skips key assignment and each row carries only metric values

#### Scenario: Min/Max on a non-numeric field is surfaced as a double and then dropped

- **Given** the aggregate `Min<Order,DateTime>(o => o.Created, v => v.Earliest)` — a shape `ViewDefinitionBuilder` permits — and a `min` aggregation whose value NEST reports as epoch milliseconds
- **When** `ExtractMetricValues` returns it as a `double?` and `ElasticSearchViewStore.SetPropertyValue` assigns it
- **Then** `Convert.ChangeType(double, typeof(DateTime))` throws inside `ConvertValue`, which returns `false`, and `TView.Earliest` is left at `default(DateTime)` with no error — the `double?` surface makes date and string `Min`/`Max` unreachable on this backend

### Requirement: Cosmos DB view store execution

The system SHALL take the hand-built Cosmos SQL path in `QueryAsync` whenever the definition
has aggregates **or** group-by clauses, and the LINQ path (`GetItemLinqQueryable`) otherwise;
`QueryFirstAsync` and `CountAsync` SHALL branch on `HasAggregates` alone. The SQL path SHALL
map view property names back to source field names in the `WHERE` clause, order aggregate
results by their SELECT alias and group keys by `c.{source field}`, and SHALL emit
`OFFSET {offset ?? 0} LIMIT {limit ?? int.MaxValue}` whenever either paging argument is
present. Joins are not supported.

#### Scenario: Group-by-only view is routed inconsistently

- **Given** a definition with `HasGroupBy == true` and `HasAggregates == false`
- **When** `QueryAsync` is called
- **Then** the grouping SQL path is used; but calling `QueryFirstAsync` or `CountAsync` on the same definition takes the LINQ path and returns raw ungrouped documents / the raw document count

#### Scenario: WHERE clause is translated to source field names

- **Given** a field selector mapping `Order.StatusCode` → `TView.Status` and the filter `v => v.Status == 3`
- **When** the aggregate SQL is built
- **Then** the emitted predicate is `c.StatusCode = 3`, because the query runs `FROM c` over raw documents

#### Scenario: Order key resolution distinguishes aggregates from group keys

- **Given** an aggregate view with `TView.Total` produced by a `Sum` and `TView.Status` mapped from `Order.StatusCode`
- **When** ordering by `Total` then `Status` descending
- **Then** the clause is `ORDER BY Total ASC, c.StatusCode DESC`

#### Scenario: Only offset supplied still emits a LIMIT

- **Given** `QueryAsync(filter, null, limit: null, offset: 20)` on an aggregate view
- **When** the SQL is built
- **Then** it ends with `OFFSET 20 LIMIT 2147483647`

### Requirement: Cosmos DB filter translation is best-effort and fails open

The system SHALL translate a filter expression into a Cosmos SQL `WHERE` fragment supporting
`AndAlso`, `OrElse`, `Not`, the six binary comparisons, and `Contains` on a member target,
and SHALL return the **empty string** — producing a query with no `WHERE` clause at all —
whenever any part of the expression cannot be translated. Literal values SHALL be inlined:
`null` unquoted, strings single-quoted with `'` escaped as `\'`, booleans as `true`/`false`,
enums as their numeric value, `DateTime`/`DateTimeOffset` as ISO-8601 (`"o"`) quoted strings,
`Guid` quoted, `decimal`/`double`/`float` invariant-formatted, and anything else via
`ToString()`.

#### Scenario: An untranslatable predicate silently drops the filter

- **Given** the filter `v => v.Tags.Any(t => t == "x")` on an aggregate Cosmos view
- **When** `CosmosFilterTranslator.Translate` runs
- **Then** the `NotSupportedException` is caught, `string.Empty` is returned, no `WHERE` is appended, and the query returns **every** document's aggregate rather than the filtered subset

#### Scenario: Constant on the left of a comparison is not translated

- **Given** the filter `v => 100 < v.Total`
- **When** the binary expression is translated
- **Then** `TranslateFieldAccess` throws on the constant left operand, the outer catch swallows it, and the whole filter is dropped

#### Scenario: Enum and DateTime literals are emitted as valid SQL

- **Given** the filters `v => v.Status == OrderStatus.Active` and `v => v.Created > new DateTime(2026, 1, 1)`
- **When** the values are translated
- **Then** the emitted literals are the enum's numeric value (unquoted) and the ISO-8601 round-trip string in single quotes

#### Scenario: Values are inlined, not parameterized

- **Given** a string filter value containing a single quote
- **When** it is translated
- **Then** the quote is escaped as `\'` and the literal is concatenated directly into the SQL text; no `QueryDefinition` parameter is created

### Requirement: Store-agnostic aggregation query specification

The system SHALL describe a store-level aggregation with `AggregateQuery<T>` (where
`T : AbstractModel`) carrying an optional pre-aggregation `Filter`, `GroupByFields` (property
names), `Aggregates`, an optional `TimeBucketInterval` plus `TimeColumn`, an optional
`OrderBy` map of field → descending, and `Limit`/`Offset`, with `GroupByFields` and
`Aggregates` defaulting to empty. Each `AggregateField` SHALL resolve its output key as its
explicit `Alias`, or `"{function-lowercase}_{SourcePropertyName}"` when no alias is given.
Server-side aggregation SHALL be offered as the *optional* interfaces
`IAggregatableStore<T>.Aggregate` and `IAsyncAggregatableStore<T>.AggregateAsync`.

#### Scenario: Default alias derivation

- **Given** `new AggregateField(AggregateFunction.Sum, "Total")`
- **When** `ResolvedAlias` is read
- **Then** it is `"sum_Total"`

#### Scenario: Explicit alias wins

- **Given** `new AggregateField(AggregateFunction.Count, "Guid", "row_count")`
- **When** `ResolvedAlias` is read
- **Then** it is `"row_count"`

#### Scenario: A default query aggregates nothing

- **Given** `new AggregateQuery<Order>()`
- **When** its collections are inspected
- **Then** `GroupByFields` and `Aggregates` are empty and every optional member is `null`

### Requirement: LINQ aggregation fallback

The system SHALL provide `AggregateHelper.LinqAggregateAsync` as the aggregation
implementation for stores without native support: it SHALL compile and apply `Filter`, and
when both `Aggregates` and `GroupByFields` are empty SHALL return one `AggregateResult` per
source item holding every public instance property of that item. Otherwise it SHALL group by
the composite key formed from the resolvable group-by properties (joined with `"|"` over
`ToString()`, `null` rendering as the empty string), compute each aggregate per group, and
apply ordering, offset and limit. Grouping SHALL NOT mutate the source entities.
`LinqAggregate` SHALL be a blocking wrapper (`GetAwaiter().GetResult()`) over the async form.

#### Scenario: No aggregates and no grouping returns raw rows

- **Given** three `Order` instances and `new AggregateQuery<Order>()`
- **When** `LinqAggregateAsync` runs
- **Then** three `AggregateResult`s are returned, each keyed by the entity's public property names

#### Scenario: Count uses the group size, other functions read the property

- **Given** orders grouped by `Status` with aggregates `Count` and `Sum` of `Total`
- **When** the helper runs
- **Then** each row carries the group key under its group-by field name, `count_<x>` set to `group.Count()` as an `int`, and `sum_Total` computed by `AggregateMath`

#### Scenario: An unresolvable aggregate source property is silently omitted

- **Given** an `AggregateField(Sum, "Nonexistent")`
- **When** the helper runs
- **Then** the property lookup returns null, the aggregate is skipped with `continue`, and the result row simply has no `sum_Nonexistent` key

#### Scenario: Time bucketing does not mutate the source entities

- **Given** an InMemory store's live `Order` instances, `TimeBucketInterval == "1 hour"` and `TimeColumn == "Created"`
- **When** the helper groups and builds rows
- **Then** each row gains a `bucket_time` computed by `AggregateMath.TruncateToBucket`, and every source entity's `Created` value is unchanged

#### Scenario: An unparseable or zero interval disables bucketing

- **Given** `TimeBucketInterval == "fortnightly"` (or a `TimeColumn` that does not resolve to a property)
- **When** the helper runs
- **Then** `bucketTicks` is `0` (or `timeProp` is null), the `bucketTicks > 0` guard skips bucketing entirely, and no `bucket_time` key appears in any row — with no error raised

#### Scenario: An unresolvable group-by field shifts the remaining key labels

- **Given** `GroupByFields == ["Bogus", "Status"]` where `Bogus` is not a property of `T`
- **When** the helper builds the result rows
- **Then** the null property is filtered out of `keyProperties`, leaving one entry, and the row-labelling loop writes the **`Status`** value under the key **`"Bogus"`** because it indexes `GroupByFields` by the position in the shortened `keyProperties` array

#### Scenario: Ordering and paging are applied after aggregation

- **Given** aggregate results and `OrderBy == { ["sum_Total"] = true }`, `Offset == 2`, `Limit == 3`
- **When** `ApplyOrderingAndPaging` runs
- **Then** the rows are sorted descending by the boxed value of `sum_Total`, the first two are skipped and at most three are returned; multiple order entries chain via `ThenBy`/`ThenByDescending` in the dictionary's enumeration order

### Requirement: Pure aggregate computation

The system SHALL compute aggregates over boxed values in `AggregateMath`:
`TruncateToBucket` floors a `DateTime` to a multiple of `bucketTicks` preserving its `Kind`;
`ComputeSum` and `ComputeAvg` return `null` for an empty value list and dispatch on the
property's underlying (non-nullable) type across `decimal`, `double`, `float`, `int` and
`long`, returning `null` for any other type; `ComputeAggregate` returns the supplied
`groupCount` for `Count` without inspecting the values, strips nulls before `Sum`/`Avg`, and
returns `Min`/`Max` via the default comparer or `null` when nothing non-null remains.

#### Scenario: Sum over an unsupported numeric type returns null

- **Given** a property of type `short` (or `byte`, `uint`, `ulong`) holding non-null values
- **When** `ComputeSum` runs
- **Then** `null` is returned — no type branch matches, and no exception is raised

#### Scenario: Count ignores the value list

- **Given** `ComputeAggregate(AggregateFunction.Count, values: [], propertyType: typeof(int), groupCount: 7)`
- **When** it runs
- **Then** `7` is returned

#### Scenario: All-null group yields null for every non-Count function

- **Given** a group whose aggregated property is null on every item
- **When** `ComputeAggregate` runs for `Sum`, `Avg`, `Min` and `Max`
- **Then** `null` is returned in each case

#### Scenario: Bucket truncation preserves DateTimeKind

- **Given** `dt = 2026-07-30T13:47:12Z` (`Kind == Utc`) and `bucketTicks == TimeSpan.FromHours(1).Ticks`
- **When** `TruncateToBucket` runs
- **Then** `2026-07-30T13:00:00` with `Kind == Utc` is returned

### Requirement: Aggregate result access

The system SHALL expose an aggregation row as a read-only dictionary plus a typed
`GetValue<TVal>(alias)` accessor that returns the value directly when it is already of type
`TVal`, otherwise attempts `Convert.ChangeType`, and returns `default` when the key is
missing, the value is `null`, or the conversion throws.

#### Scenario: Numeric widening via conversion

- **Given** a row whose `"sum_Total"` value is the boxed `int` 42
- **When** `GetValue<decimal>("sum_Total")` is called
- **Then** `42m` is returned

#### Scenario: Failed conversion is swallowed

- **Given** a row whose `"label"` value is the string `"abc"`
- **When** `GetValue<int>("label")` is called
- **Then** `0` is returned and no exception escapes

#### Scenario: Missing bucket_time yields default(DateTime), not null

- **Given** a row produced by an un-bucketed aggregation, so no `bucket_time` key exists
- **When** `GetBucketTime()` is called
- **Then** `DateTime.MinValue` is returned wrapped in the `DateTime?` return type — never a `null` — because the unconstrained `TVal?` of `GetValue<DateTime>` resolves to `DateTime`

### Requirement: Expression-based sort specification

The system SHALL build an `OrderBy<T>` only through the static factories
`By`, `ByDescending` and `ByName` — the constructor is private — each starting a fresh
single-field specification, with `ThenBy` / `ThenByDescending` appending further fields in
call order. `ToDictionary()` SHALL project the fields to property-name → descending, so
repeated sorts on the same property collapse to the last one.

#### Scenario: Multi-level sort preserves order

- **Given** `OrderBy<Order>.ByDescending(o => o.Created).ThenBy(o => o.Number)`
- **When** `Fields` is read
- **Then** it contains `OrderByField("Created", true)` followed by `OrderByField("Number", false)`

#### Scenario: ByName accepts an arbitrary string

- **Given** `OrderBy<Order>.ByName("bucket_time", descending: true)`
- **When** `Fields` is read
- **Then** it contains a single `OrderByField("bucket_time", true)` with no compile-time or runtime property check

#### Scenario: Duplicate property collapses in ToDictionary

- **Given** `OrderBy<Order>.By(o => o.Total).ThenByDescending(o => o.Total)`
- **When** `ToDictionary()` is called
- **Then** the result has one entry, `["Total"] = true`

### Requirement: Shared sort application

The system SHALL apply an `OrderBy<T>` to an `IQueryable<T>` by building
`OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` calls reflectively against
`Queryable` (preserving the provider, so RavenDB / Cosmos / EF translate the sort
server-side), and to an `IEnumerable<T>` by compiling each key selector to
`Func<T, object>` and chaining LINQ-to-Objects sorts. A null `orderBy`, a null `Fields` or an
empty `Fields` SHALL return the input sequence unchanged.

#### Scenario: Null or empty ordering is a pass-through

- **Given** a queryable or enumerable and `orderBy == null`
- **When** `OrderByHelper.ApplyTo` is called
- **Then** the same instance is returned without any sort call

#### Scenario: Queryable ordering keeps the provider

- **Given** a Raven `IRavenQueryable<TView>` and a two-field `OrderBy<TView>`
- **When** `ApplyTo` runs
- **Then** the returned queryable is still a Raven queryable (this is what lets `RavenViewStore` cast to `IRavenQueryable<TView>` afterwards)

#### Scenario: An unknown property name throws

- **Given** `OrderBy<Order>.ByName("Nonexistent")`
- **When** `OrderByHelper.ApplyTo` builds `Expression.Property`
- **Then** an `ArgumentException` is thrown by the expression factory

### Requirement: Time-interval parsing

The system SHALL parse an interval string by first attempting `TimeSpan.TryParse`, then
falling back to a two-token `"{number} {unit}"` form recognising second(s)/s,
minute(s)/m/min/mins, hour(s)/h/hr/hrs and day(s)/d case-insensitively, and SHALL return
`TimeSpan.Zero` — never throw — for a null, whitespace, unrecognised-unit or otherwise
unparseable input. `ToSqlInterval` SHALL render a parsed non-zero span as the largest whole
unit among days, hours, minutes, seconds, and SHALL pass the original string through
unchanged when parsing yields zero.

#### Scenario: Human-readable and TimeSpan forms both parse

- **Given** the inputs `"5 minutes"`, `"30 s"`, `"1 hr"` and `"00:15:00"`
- **When** `TimeIntervalParser.Parse` runs
- **Then** the results are 5 minutes, 30 seconds, 1 hour and 15 minutes

#### Scenario: A bare number is interpreted as days

- **Given** the input `"5"`
- **When** `Parse` runs
- **Then** `TimeSpan.TryParse` succeeds and 5 **days** is returned

#### Scenario: Unrecognised unit yields zero

- **Given** the inputs `"3 fortnights"`, `"abc"`, `""` and `null`
- **When** `Parse` runs
- **Then** `TimeSpan.Zero` is returned in each case with no exception

#### Scenario: SQL formatting truncates to the largest whole unit

- **Given** the inputs `"90 minutes"` and `"1.5 days"`
- **When** `ToSqlInterval` runs
- **Then** `"1 hours"` and `"1 days"` are returned — the remainder is discarded

#### Scenario: Unparseable input is passed through to SQL verbatim

- **Given** the input `"1 week"`
- **When** `ToSqlInterval` runs
- **Then** `Parse` yields zero and the original string `"1 week"` is returned unchanged for the caller to embed

### Requirement: Paged view result container

The system SHALL provide `ViewResult<TView>` as an immutable pair of `Items` and an optional
`TotalCount`, with a shared `Empty` instance holding no items and a total of `0`. No
`IViewStore<TView>` member returns it — the query contract returns `IEnumerable<TView>` plus a
separate `long` count — and no backend in this capability constructs one, so the type is
unreferenced outside its own declaration.

#### Scenario: Empty singleton

- **Given** `ViewResult<OrderSummary>.Empty`
- **When** it is inspected
- **Then** `Items` is empty and `TotalCount` is `0`, and the same instance is returned on every access

#### Scenario: Total count is optional

- **Given** `new ViewResult<OrderSummary>(items)` with no second argument
- **When** `TotalCount` is read
- **Then** it is `null`
