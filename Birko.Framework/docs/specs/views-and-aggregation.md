---
area: views-and-aggregation
generated-at: 50828c68d6f35ca2096600381c2d0c4481132330
generated-on: 2026-10-21
sources:
  - ../Birko.Data.CosmosDB.Views/CosmosViewManager.cs
  - ../Birko.Data.CosmosDB.Views/CosmosViewStore.cs
  - ../Birko.Data.CosmosDB/Aggregation/CosmosAggregationHelper.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewIndexResolver.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewManager.cs
  - ../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs
  - ../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs
  - ../Birko.Data.MongoDB.Views/MongoViewManager.cs
  - ../Birko.Data.MongoDB.Views/MongoViewSerialization.cs
  - ../Birko.Data.MongoDB.Views/MongoViewStore.cs
  - ../Birko.Data.MongoDB.Views/MongoViewTranslator.cs
  - ../Birko.Data.MongoDB/Aggregation/AggregationPipelineBuilder.cs
  - ../Birko.Data.MongoDB/Aggregation/StoreAggregationHelper.cs
  - ../Birko.Data.RavenDB.Views/RavenViewManager.cs
  - ../Birko.Data.RavenDB.Views/RavenViewStore.cs
  - ../Birko.Data.RavenDB.Views/RavenViewTranslator.cs
  - ../Birko.Data.RavenDB/Aggregation/FacetAggregationHelper.cs
  - ../Birko.Data.SQL.MSSql.View/Database/Connector/MSSqlConnector_View.cs
  - ../Birko.Data.SQL.MySQL.View/Database/Connectors/MySQLConnector_View.cs
  - ../Birko.Data.SQL.PostgreSQL.View/Database/Connectors/PostgreSQLConnector_View.cs
  - ../Birko.Data.SQL.SqLite.View/Database/Connectors/SqLiteConnector_View.cs
  - ../Birko.Data.SQL.View/Attributes/AggregateField.cs
  - ../Birko.Data.SQL.View/Attributes/AvgField.cs
  - ../Birko.Data.SQL.View/Attributes/CountField.cs
  - ../Birko.Data.SQL.View/Attributes/MaxField.cs
  - ../Birko.Data.SQL.View/Attributes/MinField.cs
  - ../Birko.Data.SQL.View/Attributes/SumField.cs
  - ../Birko.Data.SQL.View/Attributes/View.cs
  - ../Birko.Data.SQL.View/Attributes/ViewField.cs
  - ../Birko.Data.SQL.View/MaterializedViewType.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractAsyncConnector_CreateView.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractAsyncConnector_SelectView.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractAsyncConnector_SelectViewCount.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnectorBase_View.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnector_CreateSelectCommand.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnector_CreateView.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnector_SelectView.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/AbstractConnector_SelectViewCount.cs
  - ../Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs
  - ../Birko.Data.SQL.View/SQL/DataBase_View.cs
  - ../Birko.Data.SQL.View/SQL/DataBase_ViewConditions.cs
  - ../Birko.Data.SQL.View/SQL/DataBase_ViewOrderBy.cs
  - ../Birko.Data.SQL.View/SQL/Fields/BooleanFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/CharFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/DateTimeFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/DecimalFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/FunctionField.cs
  - ../Birko.Data.SQL.View/SQL/Fields/GuidFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/IntegerFunction.cs
  - ../Birko.Data.SQL.View/SQL/Fields/StringFunction.cs
  - ../Birko.Data.SQL.View/SQL/Tables/View.cs
  - ../Birko.Data.SQL.View/ViewQueryMode.cs
  - ../Birko.Data.SQL.Views/SqlViewManager.cs
  - ../Birko.Data.SQL.Views/SqlViewStore.cs
  - ../Birko.Data.SQL.Views/SqlViewTranslator.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Aggregate.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Aggregate.cs
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
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-21 (TASK-696) over 376 feature-linked tasks: 117 resolved, 259 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 1 commit leads with the id of a task the state gate refuses. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 259
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

Beneath the portable SQL backend sits `Birko.Data.SQL.View`, the SQL view engine both it and an
older, attribute-declared API (`[View]`, `[ViewField]`, `[SumField]` …) run on: the `Tables.View`
metadata, the view `SELECT` / `CREATE VIEW` builders, the connectors' view read, count and DDL
operations, the per-provider overrides (SQL Server, MySQL, PostgreSQL, SQLite) including SQL
Server indexed views and PostgreSQL materialized views, and the SQL connectors' native
`SelectAggregate` for `AggregateQuery<T>`.

The backends do **not** behave uniformly. Persistent-view support, on-the-fly support and
join support all differ per backend; where a backend cannot honour part of a definition it
refuses it with an exception rather than answering a differently-shaped result. Those
divergences are recorded explicitly below because they are the operational reality of this
capability.

## Regen provenance

Regen at `50828c68` (TASK-696): the area now covers the whole of `Birko.Data.SQL.View`, so most of this run is new
requirements for files the map newly reaches. Two existing scenarios were amended rather than kept verbatim: the persistent
view's sort key is the view property for every field, unquoted (TASK-209, `728243ea`), which the old text contradicted.

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

Regen of 2026-10-21 widened the area to the whole of `Birko.Data.SQL.View`, the four provider
`*.View` connector partials and the SQL connectors' aggregate entry points (79 sources). Requirements
already present were kept verbatim where the code still satisfies them; the persistent sort-key form was
corrected to the TASK-209 code (view property for every column, no quoted alias), and requirements were
added for the attribute-declared view build, the `Tables.View` metadata, the view `SELECT` / count / DDL
builders, the connector view operations, the provider overrides and `SelectAggregate`.

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
`{SourceType.FullName}.{SourceProperty}` is absent from the set of grouped keys;
(5) some `GroupBy` clause has no field selector with the same `SourceType` and
`SourceProperty` — with or without aggregates — so every row carries the key it was grouped
by.

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

#### Scenario: A GroupBy field that is not Selected is rejected

- **Given** a builder with `From<Order>()`, `GroupBy<Order>(o => o.Status)`, no `Select` calls, and a `Sum`
- **When** `Build()` runs
- **Then** an `InvalidOperationException` is thrown: "GroupBy field 'Status' on 'Order' is not selected. Select() it into a view property, so each row carries the key it was grouped by." — the builder checks grouped-field-implies-selected as well as selected-field-implies-grouped, so one portable meaning replaces the per-backend answers this shape used to get (the SQL translator's own check remains for a definition constructed directly)

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
`OnTheFly`, and `ElasticSearchViewManager.EnsureAsync` SHALL also return without work for
`Auto` — only a `Persistent` view has an index of its own there. `CosmosViewManager.EnsureAsync`
SHALL never create anything: it SHALL throw `ArgumentNullException` for a null definition,
`NotSupportedException` for a `Persistent` definition or one with joins, and otherwise return
`Task.CompletedTask`. RavenDB SHALL have no on-the-fly form (see *RavenDB view store
execution*).

#### Scenario: OnTheFly Ensure is a no-op on SQL, MongoDB, ElasticSearch and RavenDB

- **Given** a definition with `QueryMode == ViewQueryMode.OnTheFly`
- **When** `EnsureAsync` is called on `SqlViewManager`, `MongoViewManager`, `ElasticSearchViewManager` or `RavenViewManager`
- **Then** the method returns without issuing any DDL, index-put or index-create call

#### Scenario: Cosmos Ensure never creates anything

- **Given** a definition with `QueryMode == ViewQueryMode.OnTheFly` or `Auto` and no joins
- **When** `CosmosViewManager.EnsureAsync` is called
- **Then** it returns `Task.CompletedTask` without contacting the database — Cosmos DB has no native view support and its views are always computed on-the-fly

#### Scenario: Cosmos Ensure refuses a Persistent definition

- **Given** a definition with `QueryMode == ViewQueryMode.Persistent` and a name
- **When** `CosmosViewManager.EnsureAsync` is called
- **Then** a `NotSupportedException` is thrown reading that Cosmos DB has no stored views and the view cannot be Persistent — ensuring it used to report success and create nothing

#### Scenario: MongoDB Auto mode falls back when the view is absent

- **Given** a `MongoViewStore` whose definition is `Auto` with name `"order_summary"` and no such view exists
- **When** `QueryAsync` runs
- **Then** the store checks the collection-name listing for a collection named `"order_summary"` of `type: "view"`, finds nothing, and executes the base pipeline plus the query stages against the primary source collection instead of returning an empty result

#### Scenario: MongoDB Persistent mode uses the view, and a missing view is an error

- **Given** a `MongoViewStore` whose definition is `Persistent` with a name
- **When** `QueryAsync` runs
- **Then** the same view-only existence check runs; when the view exists the query stages are aggregated directly against it **without** prepending the base pipeline, and when it does not an `InvalidOperationException` reads "Persistent view '<name>' does not exist. Create it with IViewManager.EnsureAsync, or use Auto mode." — aggregating over a missing view used to answer an empty cursor, indistinguishable from an empty view

### Requirement: Persistent-view Ensure creates or replaces on SQL, MongoDB and RavenDB

The system SHALL, on `SqlViewManager`, drop an existing view of that name and then create it
from the current definition (`DropView`/`DropViewAsync` followed by `CreateView`/`CreateViewAsync`),
and create it outright when absent; on `MongoViewManager` it SHALL run a `collMod` command
carrying the definition's source collection (`viewOn`) and translated `pipeline` when a view of
that name exists, and the `create` command otherwise; on `RavenViewManager` it SHALL always send
the translated index definition, which replaces any existing index of that name. A changed
`ViewDefinition` therefore reaches the persistent artifact on every Ensure.

#### Scenario: SQL Ensure replaces an existing view

- **Given** a persistent SQL view named `"order_summary"` that already exists but was created from an older definition
- **When** `SqlViewManager.EnsureAsync` is called with the new definition
- **Then** `ViewExists`/`ViewExistsAsync` returns true, the view is dropped and `CREATE VIEW` is issued from the new definition — queries read the new columns rather than the stale view

#### Scenario: MongoDB Ensure modifies an existing view

- **Given** a MongoDB view named `"order_summary"` already listed among the database's collections with `type: "view"`
- **When** `MongoViewManager.EnsureAsync` is called
- **Then** a `collMod` command with `viewOn` set to the primary source collection and the newly translated `pipeline` is run, and the `create` command is not

#### Scenario: RavenDB Ensure replaces the index

- **Given** a RavenDB static index named `"order_summary"` built from an older definition
- **When** `RavenViewManager.EnsureAsync` is called with a new definition
- **Then** a `PutIndexesOperation` carrying the newly translated `Maps` (and `Reduce`, when the definition has aggregates) is sent, overwriting the existing index

#### Scenario: A RavenDB map-only index stores its view fields

- **Given** a definition without aggregates
- **When** `RavenViewManager.EnsureAsync` builds the index definition
- **Then** no `Reduce` is set and every field selector's `ViewProperty` gets `IndexFieldOptions { Storage = FieldStorage.Yes }`, so the store can project the view from the index — a map-only index otherwise answers with the source documents, and a renamed or joined field never reached the view

#### Scenario: RavenDB collections are named by the store's convention

- **Given** a `RavenViewManager` over a document store
- **When** the index is translated
- **Then** `RavenViewTranslator.TranslateToMapReduce(definition, documentStore.Conventions.GetCollectionName)` is used, so the map reads `docs.Orders` for `Order` under Raven's default convention rather than `docs.Order`, a collection no Birko-stored document is in

### Requirement: View manager name validation and idempotent teardown

The system SHALL, in every backend's `DropAsync` and `ExistsAsync`, throw `ArgumentException`
for a null, empty or whitespace view name; of the five `RefreshAsync` implementations only
`ElasticSearchViewManager`'s validates the name, the SQL, MongoDB, RavenDB and Cosmos DB
no-ops returning `Task.CompletedTask` without inspecting it. The system
SHALL throw `InvalidOperationException` with the message "View name is required for
persistent views." from `EnsureAsync` when a non-`OnTheFly` definition resolves to no name
(SQL, MongoDB, ElasticSearch, RavenDB). `DropAsync` SHALL treat a missing artifact as
success on ElasticSearch (HTTP 404) and MongoDB (no collection of that name is listed). On
MongoDB, `DropAsync` and `ExistsAsync` SHALL mean **views only**: `ExistsAsync` filters the
listing by `type: "view"`, and `DropAsync` of an ordinary collection SHALL throw
`InvalidOperationException` rather than drop it. On Cosmos DB, where no container is ever a
view, `DropAsync` SHALL do nothing and `ExistsAsync` SHALL always return `false`, neither
contacting the database once the name is validated.

#### Scenario: Blank view name is rejected

- **Given** any of the five `IViewManager` implementations
- **When** `DropAsync("   ")` or `ExistsAsync("")` is called
- **Then** an `ArgumentException` is thrown with parameter name `viewName`

#### Scenario: Dropping a non-existent ElasticSearch view index succeeds

- **Given** no index named `"order_summary"` exists
- **When** `ElasticSearchViewManager.DropAsync("order_summary")` is called and the delete response carries `ServerError.Status == 404`
- **Then** the method returns normally instead of throwing

#### Scenario: Dropping a Cosmos view touches nothing

- **Given** any valid name, whether or not a container of that name exists
- **When** `CosmosViewManager.DropAsync("order_summary")` is called
- **Then** the method returns normally without calling `DeleteContainerAsync`, and `ExistsAsync("order_summary")` returns `false`

#### Scenario: Drop never deletes a same-named real collection or container

- **Given** a MongoDB collection (not a view) named `"Order"`, or a Cosmos container named `"Order"`
- **When** `MongoViewManager.DropAsync("Order")` or `CosmosViewManager.DropAsync("Order")` is called
- **Then** MongoDB throws `InvalidOperationException` ("'Order' is a collection, not a view; DropAsync drops only views.") and Cosmos DB does nothing — the data collection / container and its documents survive. The name derived for an unnamed view is the source collection's, so both used to delete data

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
- **Then** no predicate is applied: SQL passes `conditions == null`, MongoDB adds no `$match` stage, ElasticSearch uses `MatchAllQuery`, RavenDB omits the `Where` call and Cosmos DB appends no `WHERE` clause

#### Scenario: QueryFirstAsync on an empty result returns null

- **Given** a view whose filter matches nothing
- **When** `QueryFirstAsync(filter)` is called
- **Then** `null` is returned (SQL `FirstOrDefault`, MongoDB `results.FirstOrDefault()` guard, ElasticSearch `results.FirstOrDefault()`, Raven `FirstOrDefaultAsync`, Cosmos `FirstOrDefault()` over the collected rows)

### Requirement: SQL view translation

The system SHALL translate a `ViewDefinition` into SQL `View` metadata by resolving each
referenced source type through `DataBase.TryLoadTable`, and SHALL fail fast with an exception
rather than silently dropping a column or a join, specifically: `ArgumentNullException` for a
null definition; `NotSupportedException` when a `GroupBy` field is not also `Select`ed, or
when a join's type is not `Inner`, `LeftOuter` or `Cross` (an unknown type used to map to
`Cross`, whose condition is not rendered);
`InvalidOperationException` for an unmapped source type, an unmapped source property, a
missing public instance view property, or a `Count(*)` whose base table has no fields.

#### Scenario: GroupBy without a matching Select is rejected

- **Given** a definition, constructed directly rather than through the builder (which now refuses this shape first), grouping by `Order.Status` where `Status` is not in `Fields`
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

### Requirement: An attribute-declared SQL view is built from its attributes alone

The system SHALL build `DataBase.LoadView(viewType)` from the type's `[View]` and `[ViewField]` / aggregate attributes
alone, so concurrent first loads of one view type all build the same view. It SHALL NOT take a view field from the
field cache by source column name (TASK-666: a build that ran while a concurrent one had cached its fields took
`COUNT(Orders.Guid) AS OrderCount` as the first cached field named `Guid`, rendering `"Orders".Guid AS CustomerId`;
seen on CI after `ViewCacheConcurrencyTests`' 64 concurrent first loads).

#### Scenario: A rebuild while the fields are cached keeps the aggregate

- **Given** a view with `[ViewField(Customer, Guid)] CustomerId` and `[CountField(Order, Guid)] OrderCount`, loaded once (its fields are cached) and then dropped from the view cache
- **When** `LoadView` builds it again
- **Then** its select SQL contains `COUNT(` and `AS OrderCount`, never `"Orders".Guid AS CustomerId`

### Requirement: SQL view store execution

The system SHALL execute SQL view queries through the connector, preferring genuine async
I/O — `SelectAsync` / `SelectCountAsync` on an `AbstractAsyncConnector`, threading the
`CancellationToken` — and falling back to synchronous `Select` / `SelectCount` otherwise;
SHALL parse the filter with `DataBase.ParseConditionExpression` inside a per-call
`DataBase.ScopeFieldResolver` scope that resolves `TView`'s properties against this store's own
view fields, refusing a filter on an aggregate view property with `NotSupportedException`
(it would need `HAVING`); SHALL translate
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

#### Scenario: A non-null filter is resolved against the view's own fields

- **Given** a `SqlViewStore<OrderSummary>` built from a portable definition and the filter `v => v.Status == 3`
- **When** `DataBase.ParseConditionExpression` resolves `Status` against the lambda's parameter type `OrderSummary`
- **Then** the scoped resolver — consulted ahead of `TryLoadTable(typeof(OrderSummary))` and the process-wide `ResolveFieldSelectName` hook — finds the view field whose `Property.Name` is `Status` and answers `field.GetSelectName(withTableName)`, so a plain portable view POCO with no SQL mapping attributes is filterable. The scope is an `AsyncLocal` disposed when parsing ends (the conditions are materialised inside it), so nothing is registered process-wide. A property no view field carries falls through to the default resolution. Previously every filtered query on a portable view failed with `InvalidOperationException("Condition name cannot be null or empty")`

#### Scenario: A filter on a Persistent or Auto view names the view's own columns

- **Given** a view renaming `Person.Name` to `TView.PersonName`, read Persistent (or Auto once the view exists), single-source or joined and grouped, through `SqlViewStore` or the attribute-declared `SelectView(Type, …)` / `SelectCountView`
- **When** it is queried or counted with `v => v.PersonName == "p2"`
- **Then** the conditions, parsed for the on-the-fly SELECT (`Persons.Name`), are resolved by `DataBase.ResolveViewConditions` in the connector's view funnels, beside `ResolveViewOrderFields` once `usePersistent` is known: each source name a view field knows (qualified or not, and the other column of a field-to-field comparison, inside groups too) becomes that field's persistent column, the view property. A name that is already a persistent column, or that no field knows, passes through; an unqualified source name two fields share with different columns is refused with `ArgumentException`; the caller's conditions are not modified; the on-the-fly path is untouched. Measured live on SQLite, PostgreSQL, MySQL and SQL Server (TASK-639): before, every filtered read and count of such a view failed ("no such column: Persons.Name", 42P01, "could not be bound")

#### Scenario: A filter on an aggregate view property is refused

- **Given** a view whose `Sum` of `Order.Total` targets `TView.Total`, and the filter `v => v.Total > 1000m`
- **When** the filter is parsed
- **Then** a `NotSupportedException` reads "A filter on aggregate view property '<TView>.Total' would need HAVING, which view filters do not emit." — the condition is never rendered as a `WHERE` on the pre-aggregation rows

#### Scenario: Order-by keys are resolved against the view's fields, per path

- **Given** an aggregate view whose `Sum` of `Order.Total` targets `TView.Total`, queried with `OrderBy<TView>.By(v => v.Total)`
- **When** the select command is built
- **Then** the key is resolved by `DataBase.ResolveViewOrderFields` before it reaches the clause, and the resolved form follows the path: on the on-the-fly path `ORDER BY SUM(Order.Total) ASC` (what that SELECT list projects, aliased `as Total` — the aggregate's view property, read off `AbstractField.Property`), and on the persistent path `ORDER BY Total ASC` — emitted bare, matching the bare `AS Total` alias the view DDL creates and the bare persistent SELECT list (TASK-209 brought all three into line). Before resolution existed the raw key was interpolated, so neither an aggregate nor a renamed view property matched an emitted column

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
projects; **persistent** → `field.Property != null ? field.Property.Name : field.Name` for **every** field,
aggregate or not, matching `GetPersistentViewSelectFields()` and the bare `AS <ViewProperty>` alias the view
DDL emits for every column (TASK-209). The `Property != null` arm is load-bearing rather than defensive:
`AbstractField.Property` is declared non-nullable but is assigned by the view builders, and a key matched on
`Name` alone can reach this point, so a field with no `Property` SHALL fall back to its source name instead of
raising `NullReferenceException` from a sort. The resolved identifier SHALL NOT be quoted on either path,
matching both the on-the-fly and the persistent SELECT lists ("quote tables, never quote columns").

#### Scenario: Sorting by a renamed view property works

- **Given** a view projecting `Person.Name` as the view property `PersonName`, queried with `OrderBy<TView>.By(v => v.PersonName)`
- **When** the read is enumerated
- **Then** the on-the-fly clause is `ORDER BY Persons.Name ASC` and the persistent clause is `ORDER BY PersonName ASC` (the view's own column since TASK-209), and the rows come back sorted. Before resolution both raised `no such column: PersonName` — so sorting a view by one of its own properties did not work at all, and only `ByName("<source column>")` did

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
  `Property != null` before preferring the view property, so a sort cannot throw `NullReferenceException`

#### Scenario: A non-property sort expression is rejected at the key-extraction step

- **Given** an expression-keyed order dictionary whose selector is not a property access (for example
  `v => v.Compute()`), or a null expression
- **When** `DataBase.GetViewOrderKey` runs
- **Then** a null expression raises `ArgumentNullException` and a non-property selector raises
  `ArgumentException` naming the expression — the failure is at key extraction, before any resolution or
  command building

### Requirement: Attribute-declared SQL view metadata

The system SHALL build `DataBase.LoadView(type)` from the type's `[View]` attributes
(`AllowMultiple`) and its public instance properties' `[ViewField]` / `[SumField]` / `[AvgField]` /
`[MinField]` / `[MaxField]` / `[CountField]` attributes, and cache the result per type. For each
`[View]` whose two model types load as tables, it SHALL add a join from `ModelLeft`'s table to
`ModelRight`'s: a field-to-field condition when both `ModelProperyLeft` and `ModelProperyRight` are
strings naming a mapped property, a field-to-constant condition when only one does (the other
argument is the value), and no join when neither does. The join type SHALL be `LeftOuter` for
`ViewConnect.Check` and `Inner` for `ViewConnect.CheckExisting` and the default `ViewConnect.None`
(TASK-626). A non-aggregate view field SHALL be the source field(s) from `LoadField`, re-pointed at
the view property; an aggregate one SHALL be a `FunctionField` keyed by the view property. The view's
`QueryMode` SHALL come from the first `[View]` attribute (default `OnTheFly`), its `Name` from the
first attribute's non-empty `Name`, otherwise the distinct table names concatenated. A `[ViewField]`'s
own `name` argument SHALL have no effect on the built view. A type yielding no table — no `[View]`
attribute, or no field that resolves — SHALL make `LoadView` return `null` (uncached); the
`TableAttributeException("No view attributes in type")` branch is unreachable. `ReadView` SHALL throw
`TableAttributeException` when the data object's type has no view. The view field resolver SHALL be
registered as `DataBase.ResolveFieldSelectName` by a module initializer, idempotently, so the first
`ToConditions<TView>` in a process resolves view fields (TASK-111).

#### Scenario: Default ViewConnect is an inner join

- **Given** `[View(typeof(Customer), typeof(Order), "Guid", "CustomerGuid")]` with no `connect` argument
- **When** `LoadView` builds the view
- **Then** its single join is `JoinType.Inner` with condition `Customers.Guid = Orders.CustomerGuid` — it used to be `Cross`, whose condition was not rendered, answering the cartesian product

#### Scenario: Check is a left outer join

- **Given** the same attribute with `ViewConnect.Check`
- **When** `LoadView` builds the view
- **Then** the join is `JoinType.LeftOuter`

#### Scenario: Name defaults to the concatenated table names

- **Given** `[View]` attributes with no `Name` over tables `Customers` and `Orders`
- **When** `LoadView` builds the view
- **Then** `Name` is `"CustomersOrders"`; with `Name = "customer_orders"` on the first attribute it is `"customer_orders"`

#### Scenario: A type with no view attributes yields null

- **Given** a class with no `[View]` attribute
- **When** `LoadView` runs
- **Then** `null` is returned rather than an exception, so `SelectView` over it yields no rows and `SelectCountView` returns `0`

### Requirement: View field metadata is keyed by the view property

The system SHALL store each `Tables.View` field in its table's dictionary under the **view property**
it populates (`field.Property.Name`, falling back to `field.Name` when no property is attached), or
under an explicit `name` passed to `AddField`; a table is created on first use. Re-adding a field equal
by value (same type, `Name`, `IsAggregate`, property name and declaring type) SHALL be a no-op, and a
different field under a taken key SHALL throw `FieldAttributeException` naming both (TASK-207).
`AddJoin` SHALL merge the conditions of a join with the same `Left`, `Right` and `JoinType` into the
existing join, and append any other. The constructor SHALL derive `Name` from the table names only when
no name is supplied (CR-M147). `GetPersistentViewSelectFields()` SHALL return, by position, the view
property (else `Name`) of every field of every table; `HasAggregateFields()` SHALL be true when any
table has an aggregate field.

#### Scenario: Two view properties over one source column are two fields

- **Given** `[ViewField(typeof(Person), "Name")] DisplayName` and `[ViewField(typeof(Person), "Name")] SortName`
- **When** both are added
- **Then** the table holds keys `DisplayName` and `SortName`, both with source `Name`

#### Scenario: A key collision is refused

- **Given** a view whose table already holds key `Total` for one field
- **When** `AddField` presents a different field under `Total` (an explicit `name`, or `AddTable` of source fields)
- **Then** `FieldAttributeException` is thrown — the second field is not dropped silently

#### Scenario: A repeated [View] attribute re-adds fields idempotently

- **Given** a view type with three `[View]` attributes, so `LoadView`'s per-property loop runs three times and presents fresh `AbstractField` instances
- **When** the fields are added
- **Then** each later add is recognised as the same field by value and nothing is thrown or duplicated

### Requirement: Aggregate view columns are typed by function and source

The system SHALL create an aggregate view column as a `FunctionField` whose single parameter is the
source column name and whose `IsAggregate` is true: `COUNT` as an integer function and `AVG` as a
decimal function, each nullable exactly when the source column is; `SUM`, `MIN` and `MAX` typed by the
source field — integer, decimal, date-time, boolean and Guid sources giving the same-typed function
(nullable when the source is), a char source a `CharFunction` of its length, and anything else a
`StringFunction`, both of which are non-nullable regardless of the source. An aggregate attribute other
than the five SHALL throw `NotSupportedException`. Each typed function SHALL read its column with the
matching typed reader getter (`GetInt32`, `GetDecimal`, `GetDateTime`, `GetBoolean`, `GetGuid`,
`GetString`), the nullable forms first assigning `null` for `DBNull`.

#### Scenario: Average over an integer column is decimal

- **Given** `[AvgField(typeof(Order), "Quantity")]` over a non-null integer column
- **When** the view is built
- **Then** the field is a `DecimalFunction` named `AVG` and read with `GetDecimal`

#### Scenario: A nullable source gives a nullable aggregate column

- **Given** `[MaxField(typeof(Order), "ShippedAt")]` over a nullable date-time column
- **When** a row whose maximum is `NULL` is read
- **Then** the field is a `NullableDateTimeFunction` and the view property is set to `null`

#### Scenario: A string aggregate is always non-nullable

- **Given** `[MinField(typeof(Person), "Nickname")]` over a nullable string column
- **When** a group whose every `Nickname` is `NULL` is read
- **Then** the field is a `StringFunction`, and `GetString` on the `DBNull` column throws

### Requirement: The view SELECT body has one builder

The system SHALL build the `SELECT` that defines a view's body — used by `CREATE VIEW`, PostgreSQL
materialized views and SQL Server indexed views — in `ViewSelectSqlBuilder.BuildViewSelectSql(view,
quoteIdentifier, qualifyTableName)`, throwing `ArgumentNullException` for a null view or quoting
delegate, `InvalidOperationException("View has no source table to select from.")` when no table has a
name and `InvalidOperationException("View must have at least one field.")` when nothing is projected. A
view with no join SHALL select from its one table (TASK-151). Each column SHALL be the table-qualified
projection with the table quoted and the column bare, aliased `AS <ViewProperty>` bare when the field
has a property (TASK-209); aggregates SHALL be projected without their own alias so only one `AS`
appears (TASK-129). The `FROM` SHALL list the tables no join brings in, each followed by its joins
grouped by right table and join type, the keyword from `Join.Keyword` (which refuses a `Cross` join with
conditions and an `Inner`/`LeftOuter` join without), and `ON (…)` conditions joined by `AND`: a
field-to-field condition as `"Left".Column = "Right".Column`, a constant condition as `"Table".Column =
<literal>` — `TRUE`/`FALSE` for a bool, an invariant-culture number, or a single-quoted string with its
quotes doubled — and a constant condition with a null value omitted. An aggregate view SHALL end with
`GROUP BY` over its non-aggregate projections. `qualifyTableName` SHALL change only the `FROM`/`JOIN`
table token; the connector passes `ViewRelation`, which renders a schema-placed table as its relation
aliased by the quoted logical name (TASK-272).

#### Scenario: A grouped join view body

- **Given** a view joining `Customers` to `Orders` inner on `Guid = CustomerGuid`, selecting `Customers.Name → CustomerName` and `COUNT(Orders.Guid) → OrderCount`
- **When** the body is built with double-quote quoting
- **Then** it reads `SELECT "Customers".Name AS CustomerName, COUNT("Orders".Guid) AS OrderCount FROM "Customers" INNER JOIN "Orders" ON ("Customers".Guid = "Orders".CustomerGuid) GROUP BY "Customers".Name` — one alias per column, tables quoted, columns bare

#### Scenario: A constant join condition is rendered as a literal

- **Given** a `[View]` whose right property is the string constant `"O'Brien"` rather than a mapped property
- **When** the body is built
- **Then** the condition is `"Table".Column = 'O''Brien'`; a `true` constant renders `TRUE` and `1.5` renders `1.5` under any culture

#### Scenario: A view with no table is refused

- **Given** a `Tables.View` with no named table
- **When** `BuildViewSelectSql` runs
- **Then** `InvalidOperationException("View has no source table to select from.")` is thrown

### Requirement: On-the-fly view read and count commands

The system SHALL build the on-the-fly view read through the entity `CreateSelectCommand` with the view's
`FROM` tables (`ViewFromTables`: the tables no join's right side names, or every table when there is no
join), `GetSelectFields()`, the view's joins, the conditions, the non-aggregate projections as `GROUP BY`
when the view aggregates, and the resolved order, limit and offset; a null view SHALL throw
`ArgumentNullException`. The on-the-fly count SHALL be `SELECT COUNT(*) FROM (<inner>) birko_view_count`,
where the inner select projects the constant `1 AS birko_one` grouped as the view is — or `COUNT(*) AS
birko_one` with no grouping for an aggregate view with no non-aggregate field — so it counts the rows a
query returns (TASK-151).

#### Scenario: A grouped view counts its groups

- **Given** a view grouping 1 000 000 orders into 12 statuses
- **When** `SelectCount(view)` runs on-the-fly
- **Then** the inner select is grouped by the status column and the count is `12` — it used to count the joined source rows

#### Scenario: A whole-table aggregate counts one row

- **Given** a view whose only field is `SUM(Orders.Amount)`
- **When** it is counted on-the-fly
- **Then** the inner select is `SELECT COUNT(*) AS birko_one …` with no `GROUP BY`, and the count is `1`

### Requirement: Persistent-path selection and the view-existence cache

The system SHALL decide per read and per count whether to query the persistent view:
`OnTheFly` never, `Persistent` always, and `Auto` only when the view has a name and the existence check
answers true. In `Auto` it SHALL cache only a positive answer, per connector instance and view name, so a
`false` (absent, or a swallowed transient failure) is re-checked on the next query (CR-M149).
`CreateView`, `DropView`, `CreateIndexedView` and `DropIndexedView` SHALL invalidate the name's cache
entry; `ClearViewExistsCache` and `InvalidateViewExistsCache` SHALL be public. Both the sync and the
async read and count funnels SHALL use the **synchronous** `ViewExists` for this check. A null view SHALL
make the read yield nothing and the count return `0`.

#### Scenario: Auto falls back while the view is absent, then uses it

- **Given** an `Auto` view named `"order_summary"` that does not yet exist
- **When** it is read, the view is created through `CreateView`, and it is read again
- **Then** the first read runs on-the-fly, the existence check runs again on the second read (the `false` was not cached) and the second read queries the view

#### Scenario: Persistent never checks existence

- **Given** a `Persistent` view
- **When** it is read
- **Then** no existence check runs and the persistent command is issued — a missing view surfaces as the database's error

### Requirement: Persistent view read and count commands

The system SHALL read a persistent view as `SELECT <persistent columns, bare> FROM <QuoteIdentifier(name)>`,
with the resolved conditions, the resolved `ORDER BY` when sort keys are given, otherwise
`ORDER BY <PagingPlaceholderOrderBy>` when paging on a provider whose `RequiresOrderByForPaging` is set
(SQL Server, TASK-278), and the provider's limit/offset clause whenever either is set (TASK-151). It
SHALL throw `InvalidOperationException` for an empty view name or a view with no fields. Rows SHALL be
transformed with `GetPersistentViewSelectFields()` on this path and `GetSelectFields()` on the
on-the-fly path. A persistent count SHALL be `SELECT COUNT(*) FROM <quoted name>` with the resolved
conditions; when the persistent path is chosen but the view's name is empty, the count SHALL instead run
the on-the-fly count (the read throws).

#### Scenario: Offset alone pages on SQL Server

- **Given** a persistent view on SQL Server and a read with `offset: 20`, no limit and no order
- **When** the command is built
- **Then** it carries the placeholder `ORDER BY` followed by the provider's offset clause

#### Scenario: Persistent count

- **Given** a persistent view `"order_summary"` and a filter on `PersonName`
- **When** `SelectCount` runs
- **Then** the command is `SELECT COUNT(*) FROM "order_summary"` followed by a `WHERE` on `PersonName` (the condition already resolved to the view's column)

### Requirement: Connector view DDL operations

The system SHALL offer on both connector families: `CreateView(Async)` from a view type (throwing
`InvalidOperationException` when `LoadView` yields no tables) or from `Tables.View` (throwing
`ArgumentNullException` for null and `InvalidOperationException` when neither the `viewName` argument nor
`View.Name` is non-blank), issuing `BuildCreateViewSql(name, BuildViewSelectSql(view))` as DDL —
`CREATE OR REPLACE VIEW <quoted> AS …` by default; `CreateViews(Async)` over a list (a null list a
no-op); `DropView(Async)` by type or name as `DROP VIEW IF EXISTS <quoted>` (a blank name
`ArgumentException`, an unnamed type `InvalidOperationException`); `DropViews(Async)` skipping blank
names; `RecreateView(Async)` as drop then create; and `CreateViewIfNotExists(Async)`, creating only when
`ViewExists(Async)` answers false. The base `ViewExists(Async)` SHALL throw `ArgumentException` for a
blank name, run `SELECT 1 FROM <quoted> WHERE 1=0`, and answer `false` on **any** exception — so it
answers `true` for a table of that name as well as a view.

#### Scenario: Default CREATE VIEW replaces

- **Given** a provider with no `BuildCreateViewSql` override (MySQL, PostgreSQL)
- **When** `CreateView(typeof(CustomerOrders))` runs
- **Then** `CREATE OR REPLACE VIEW "<name>" AS <body>` is executed and the name's existence-cache entry is removed

#### Scenario: The base existence check swallows every failure

- **Given** a connector without a provider override and a lost connection
- **When** `ViewExists("order_summary")` runs
- **Then** `false` is returned and no exception escapes

### Requirement: Provider view DDL and existence overrides

The system SHALL override view DDL and existence per provider: **SQL Server** creates with
`CREATE OR ALTER VIEW` and checks `sys.views WHERE name = @viewName`; **SQLite** creates with
`CREATE VIEW IF NOT EXISTS` — so an existing view of that name is kept, not replaced — and checks
`sqlite_master WHERE type = 'view'`; **MySQL** keeps `CREATE OR REPLACE VIEW` and checks
`information_schema.VIEWS` in `DATABASE()`; **PostgreSQL** keeps `CREATE OR REPLACE VIEW` and checks
`information_schema.views` in `current_schema()` (CR-L191). Every override binds the name as the
parameter `@viewName`, throws `ArgumentException` for a blank name, and lets a database error propagate
rather than answering `false`. SQLite and MySQL override both `ViewExists` and `ViewExistsAsync`; SQL
Server and PostgreSQL override only the synchronous `ViewExists`, their `ViewExistsAsync` being the
base relation probe.

#### Scenario: SQLite does not replace an existing view

- **Given** a SQLite view `"order_summary"` created from an older definition
- **When** `CreateView` is called with a new definition
- **Then** `CREATE VIEW IF NOT EXISTS` keeps the old view; only `SqlViewManager.EnsureAsync`'s drop-then-create (or `RecreateView`) brings the new definition in

#### Scenario: A same-named table is not a view on SQLite, MySQL and PostgreSQL

- **Given** a table, not a view, named `"order_summary"`
- **When** the provider's `ViewExists("order_summary")` runs
- **Then** SQLite, MySQL, PostgreSQL and SQL Server answer `false` from their catalogue queries, whereas the base probe used by SQL Server's and PostgreSQL's `ViewExistsAsync` answers `true`

### Requirement: SQL Server indexed views

The system SHALL let `MSSqlConnector.CreateIndexedView(Async)(viewType, viewName)` create a
`WITH SCHEMABINDING` view with `CREATE OR ALTER VIEW`, its body built by the shared builder with two-part
table names (the table's own schema, else `GetSchemaName()`, default `dbo`; TASK-272), and then
`CREATE UNIQUE CLUSTERED INDEX "IX_<name>" ON <quoted name> (<key columns>)`, invalidating the name's
existence-cache entry (CR-H090). It SHALL refuse an aggregate view with `NotSupportedException`
(SCHEMABINDING aggregates need `COUNT_BIG(*)`, which the builder does not emit), and throw
`InvalidOperationException` for a type with no view tables, an empty name, or no field to index. The key
columns SHALL be the primary-key fields of the first table that has any, else the unique fields of the
first table that has any, else the first table's first field, each rendered as quoted
`"Table"."Column"`. `DropIndexedView(Async)` SHALL issue `DROP VIEW IF EXISTS` and invalidate the cache;
`IndexedViewExists(Async)` SHALL answer whether a non-shipped view of that name has a clustered index. The
async forms SHALL use the connector's async primitives with the token (CR-M139).

#### Scenario: An aggregate indexed view is refused

- **Given** a view type with a `[CountField]`
- **When** `CreateIndexedView` runs
- **Then** `NotSupportedException` is thrown before any DDL

#### Scenario: Index key columns come from the primary key

- **Given** a non-aggregate view over `Orders` whose primary key is `Guid`
- **When** `CreateIndexedView(typeof(OrderView), "order_view")` runs
- **Then** the index statement is `CREATE UNIQUE CLUSTERED INDEX [IX_order_view] ON [order_view] ([Orders].[Guid])` — a table-qualified source column, although the view's own column is aliased by its view property

### Requirement: PostgreSQL materialized views

The system SHALL let `PostgreSQLConnector` create a materialized view from a view type with
`CREATE MATERIALIZED VIEW IF NOT EXISTS <quoted> AS <body>` (an existing one is kept), refresh it with
`REFRESH MATERIALIZED VIEW [CONCURRENTLY] <quoted>`, drop it with `DROP MATERIALIZED VIEW IF EXISTS`, and
check it in `pg_matviews` for `current_schema()`, throwing `ArgumentException` for a blank name and
`InvalidOperationException` for a type with no view tables or no name. The async forms SHALL run the
synchronous operation on `Task.Run`, observing the token only before the work starts. Materialized views
SHALL NOT be reached by `ViewExists`, the existence cache, or `SqlViewManager.RefreshAsync`;
`MaterializedViewType` SHALL be an unreferenced enum.

#### Scenario: Concurrent refresh

- **Given** a materialized view `"order_summary"`
- **When** `RefreshMaterializedView("order_summary", concurrently: true)` runs
- **Then** `REFRESH MATERIALIZED VIEW CONCURRENTLY "order_summary"` is executed

#### Scenario: Auto mode does not see a materialized view

- **Given** an `Auto` view whose name exists only as a materialized view
- **When** it is read
- **Then** `ViewExists` (over `information_schema.views`) answers `false` and the read runs on-the-fly

### Requirement: MongoDB pipeline translation

The system SHALL translate a `ViewDefinition` into an ordered list of BSON aggregation
stages: one `$lookup` per join into the field `_joined_{RightTypeName}` followed by an
`$unwind` that sets `preserveNullAndEmptyArrays: true` only for `JoinType.LeftOuter`, a
`JoinType.Cross` join being refused with `NotSupportedException`; a
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

#### Scenario: Cross join is refused

- **Given** a join with `JoinType.Cross`
- **When** the pipeline is translated
- **Then** a `NotSupportedException` explains that a Cross join cannot be a MongoDB view because `$lookup` joins on keys, and to use Inner or LeftOuter — it used to be rendered as an equality join, a different row set from SQL's

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
is always produced and `reduce` is `null` unless the definition has aggregates. Collection names
SHALL come from a supplied `Func<Type, string>` (the document store's
`Conventions.GetCollectionName`), or from `DocumentConventions.DefaultGetCollectionName` when
none is given (the one-argument overload), and SHALL be refused with `NotSupportedException`
unless they are identifiers (a letter or `_` followed by letters, digits or `_`). The map SHALL
read `from entity in docs.{collection(PrimarySource)}`, emit one
`let joined[N] = LoadDocument("{right}/" + {leftAlias}.{LeftProperty}, "{right}")` per join
(aliases `joined`, `joined2`, `joined3`, …; `{right}` the right type's collection, `{leftAlias}`
the alias of the join's `LeftType`), followed by `where joined[N] != null` for an `Inner` join,
and select each field as `{ViewProperty} = {alias}.{SourceProperty}`. A join whose
`RightProperty` is not `Guid`, or whose type is neither `Inner` nor `LeftOuter`, SHALL be
refused with `NotSupportedException`. For aggregates the map SHALL emit `1` for
`Count`, the raw source value for `Sum`/`Min`/`Max`, and for `Avg` both the raw value and a
`{ViewProperty}_Sum` plus a `{ViewProperty}_Count = 1` helper. A null definition SHALL raise
`ArgumentNullException`.

#### Scenario: A join loads the related document by its id

- **Given** a `LeftJoin<Order, Customer, Guid>(o => o.CustomerGuid, c => c.Guid)` under Raven's default collection convention
- **When** the map is built
- **Then** it emits `from entity in docs.Orders` and `let joined = LoadDocument("Customers/" + entity.CustomerGuid, "Customers")` with no `where` line — a document's id is `{collection}/{guid}`. The old `LoadDocument<Customer>(entity.CustomerGuid)` form did not compile on the server, which knows no CLR model types

#### Scenario: An Inner join drops parents that loaded nothing

- **Given** an `Inner` join from `Order` to `Customer`
- **When** the map is built
- **Then** the `let joined = …` line is followed by `where joined != null`

#### Scenario: A join on anything but the right side's identity is refused

- **Given** a join whose `RightProperty` is `Code`, or whose `JoinType` is `Cross`
- **When** the map is built
- **Then** a `NotSupportedException` is thrown — an index loads a related document by id only, and Raven has no cross join

#### Scenario: A source type not found among join right-hand types falls back to entity

- **Given** a field selector whose `SourceType` is neither the primary source nor any join's `RightType`
- **When** `GetSourceAlias` resolves it
- **Then** `"entity"` is returned, so the map reads the property off the primary-source document

#### Scenario: Average is re-reduce safe

- **Given** an `Avg` aggregate on `Order.Total` targeting `TView.AvgTotal`
- **When** map and reduce are built
- **Then** the map emits `AvgTotal`, `AvgTotal_Sum` and `AvgTotal_Count = 1`, and the reduce emits `AvgTotal_Sum = g.Sum(x => x.AvgTotal_Sum)`, `AvgTotal_Count = g.Sum(x => x.AvgTotal_Count)` and `AvgTotal = (double)g.Sum(x => x.AvgTotal_Sum) / (double)g.Sum(x => x.AvgTotal_Count)` so incremental re-reduce cannot double-average, and an integer column does not average by integer division (10 and 15 to 12)

#### Scenario: Global aggregate groups by a constant

- **Given** an aggregate-only definition with no `GroupBy` clauses and no field selectors
- **When** the reduce is built
- **Then** it emits `group result by 1 into g` rather than the invalid `group result by new {  }` an empty composite key would produce

#### Scenario: No GroupBy but fields present groups by every projected field

- **Given** an aggregate definition with field selectors and no `GroupBy` clauses (only reachable when constructed outside the builder's validation)
- **When** the reduce is built
- **Then** every `field.ViewProperty` becomes part of the group key

#### Scenario: A GroupBy field with no matching selector falls back to its source name

- **Given** `GroupBy<Order>(o => o.Status)` where no field selector maps `Order.Status` (only reachable when constructed outside the builder, which now refuses an unselected GroupBy)
- **When** `FindViewPropertyForGroupBy` resolves it
- **Then** the raw property name `"Status"` is used as the reduce key, which must coincidentally match a name the map emitted

### Requirement: RavenDB view store execution

The system SHALL choose the Raven query source from the mode: `OnTheFly` — refused with
`NotSupportedException`, a RavenDB view being a static index; `Auto` — the static index by
name, throwing `InvalidOperationException` when no name is set or a `GetIndexOperation` for it
answers `null`; `Persistent` — the static index by name, throwing `InvalidOperationException`
when no name is set. It SHALL apply the filter with `Where`, ordering with
`OrderByHelper.ApplyTo`, then `Skip`/`Take`, and SHALL throw `InvalidOperationException` if the
resulting queryable is not an `IRavenQueryable<TView>`. For a definition without aggregates,
`QueryAsync` and `QueryFirstAsync` SHALL project the rows with `ProjectInto<TView>()` from the
index's stored fields; a map-reduce index already answers with its reduce results.
Each call SHALL open and dispose its own async session.

#### Scenario: OnTheFly is refused

- **Given** an `OnTheFly` definition
- **When** `QueryAsync`, `QueryFirstAsync` or `CountAsync` runs
- **Then** a `NotSupportedException` explains that a RavenDB view is a static index and OnTheFly cannot evaluate it, directing the caller to Persistent or Auto and `IViewManager.EnsureAsync`. It used to issue `session.Query<TView>()`, a dynamic query over the collection named after the view type, which ignored the definition and answered empty

#### Scenario: Auto reports a missing index

- **Given** an `Auto` definition whose named index does not exist
- **When** `QueryAsync` runs
- **Then** an `InvalidOperationException` reads "View index '<name>' does not exist, and RavenDB has no on-the-fly view to fall back to. Create it with IViewManager.EnsureAsync." — before any query is issued

#### Scenario: A map-only view is projected from its stored fields

- **Given** a `Persistent` definition with a renamed field selector and no aggregates
- **When** `QueryAsync` runs
- **Then** the query is `ProjectInto<TView>()`-projected, so the renamed view property carries the index's stored value rather than the source document's (absent) property

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
- **Then** `"order"` is returned, and `ElasticSearchViewManager.EnsureAsync` for that definition returns without creating anything — it used to create the `"order"` source index with dynamic mapping, while `DropAsync("order_summary")` targeted another index

### Requirement: ElasticSearch view store execution

The system SHALL route a view query with aggregates **or** group-by clauses through an
aggregation search (`Size = 0`) and any other view query through a document search with
`_source` include-filtering, and SHALL throw `InvalidOperationException` including the NEST
`DebugInformation` whenever a response is invalid or carries an `OriginalException`.
A definition with joins SHALL be refused at construction with `NotSupportedException`.
The documents read SHALL be of the primary source's type, or of `TView` for a `Persistent`
view with a name (view-shaped documents in the view's own index). Every name a caller uses is a
**view** property and SHALL be mapped to the stored field of the document property it selects —
through `ElasticSearch.StoredFieldName` for `_source` includes and `ElasticSearch.ExactFieldName`
for sorts, group keys and metric fields — and each returned document SHALL be projected into a
`TView` field by field. The effective page size SHALL be clamped so `From + Size` never exceeds
10000.

A grouping view SHALL be read as a `CompositeAggregation("group_by")` with one
`TermsCompositeAggregationSource` per group key named `k0`, `k1`, … (`MissingBucket = true`) and
metrics named `m0`, `m1`, … by position, paged with `After` in pages of `GroupPageSize` (default
1000) until a page is short or has no `AfterKey`; each key SHALL be written to the view property
that selects it. Its rows SHALL then be ordered with `OrderByHelper.ApplyTo`, skipped by a
positive `offset` and taken by `limit` in memory, and `CountAsync` SHALL count those rows. An
aggregate with no group-by SHALL yield exactly one row.

#### Scenario: Aggregate query reads every group, then orders and pages them

- **Given** an aggregate view and `QueryAsync(filter, OrderBy<TView>.ByDescending(v => v.Total), limit: 50, offset: 100)`
- **When** the query executes
- **Then** each search request carries `Size = 0`, the filter query and the composite aggregation only; every composite page is read, and the resulting rows are sorted by `Total` descending, the first 100 skipped and 50 returned. `orderBy` and `offset` used to have no effect, `limit` became the bucket `size`, and only the first page of groups was ever read

#### Scenario: Two aggregates of one function over one field both survive

- **Given** `Sum(Order.Total) → TView.A` and `Sum(Order.Total) → TView.B`
- **When** the metric aggregations are built
- **Then** they are named `m0` and `m1`, so neither replaces the other — the old `{fn}_{field}` name collided

#### Scenario: Window clamping

- **Given** `ClampWindowSize` is called
- **When** the arguments are `(from: 0, limit: null)`, `(from: 9500, limit: 1000)` and `(from: 10000, limit: 100)`
- **Then** the results are `10000`, `500` and `0` respectively; a negative `from` is treated as `0` for the clamp calculation

#### Scenario: A negative offset still reaches ElasticSearch

- **Given** `QueryAsync(offset: -5)` on a non-aggregate view
- **When** the search request is built
- **Then** `From = -5` (only the local copy inside `ClampWindowSize` is normalised), so ElasticSearch receives an invalid `from`

#### Scenario: An untranslatable filter is not widened to match-all

- **Given** a filter expression `ElasticSearch.ParseExpression` cannot translate
- **When** `BuildFilterQuery` runs
- **Then** a `NotSupportedException` is thrown — a parser failure of any other type is wrapped in one, and a null translation raises one — so the query never silently becomes a full-result set; a null filter, by contrast, deliberately yields `MatchAllQuery`

#### Scenario: A filter on a view property tests the stored field it selects

- **Given** a field selector mapping `Order.StatusCode` → `TView.Status` and the filter `v => v.Status == 3`
- **When** `BuildFilterQuery` runs
- **Then** the lambda is rewritten onto the document type (`o => o.StatusCode == 3`, with a `Convert` where the types differ) and translated by the entity store's parser, so it tests the stored field of `StatusCode`; a view property that selects no field is used as-is when the document type has such a property, and refused with `NotSupportedException` otherwise. A filter on an aggregate view property is refused with `NotSupportedException` (it would need `HAVING`), and a filter reading the view row itself rather than a property of it is refused

#### Scenario: A sort names the stored field

- **Given** a non-grouping view and `OrderBy<TView>.By(v => v.Status)` where `Status` selects `Order.StatusCode`
- **When** the search request is built
- **Then** its `FieldSort.Field` is the exact (sortable) stored field of `StatusCode` — it used to be the CLR view-property name, which ElasticSearch rejected

#### Scenario: Count of a grouping view counts its rows

- **Given** an aggregate view grouping 1 000 000 orders into 12 status buckets
- **When** `CountAsync()` is called
- **Then** every group is read and `12` is returned; an aggregate view with no group-by counts `1`, and only a non-grouping view sends a `CountRequest` for the raw document count

#### Scenario: Group keys land in the view property that selects them

- **Given** a view grouping by `Order.StatusCode` selected into `TView.Status`, and by `Order.Currency` selected into `TView.Currency`
- **When** a composite page is read
- **Then** each bucket's `k0` value is written to `Status` and `k1` to `Currency` — a grouped view used to look its keys up by source-property name and returned rows with no keys

#### Scenario: Enum and Guid view properties round-trip

- **Given** a view property of an enum type receiving the string `"Active"`, and a `Guid` property receiving a string GUID
- **When** `ConvertValue` runs
- **Then** `Enum.Parse(..., ignoreCase: true)` and `Guid.Parse` are used; a value whose shape does not match the target type causes `ConvertValue` to return `false` (catching only `FormatException`, `InvalidCastException`, `OverflowException` and `ArgumentException`), and `SetPropertyValue` then throws `InvalidOperationException` naming the value, the view property and its type — the property is no longer left at its default with no error

#### Scenario: Null values are skipped, a missing property is refused

- **Given** an aggregation or key that returned no value, or a view property that does not exist or is get-only
- **When** `SetPropertyValue` runs
- **Then** a null value returns without assigning, while a missing or non-writable property throws `NotSupportedException`; a value already of the property's type is assigned as-is

### Requirement: ElasticSearch aggregation building and parsing

The system SHALL build one NEST metric aggregation per `AggregateField` keyed by
`ResolvedAlias`, mapping `Sum`→`SumAggregation`, `Avg`→`AverageAggregation`,
`Min`→`MinAggregation`, `Max`→`MaxAggregation` and `Count`→`ValueCountAggregation` over the
source field or `"_id"` when no field is given, and SHALL throw `NotSupportedException` for
any other function value. Grouping SHALL use a `TermsAggregation` named `"group_by"` for a
single field and a `CompositeAggregation` named `"group_by"` for two or more, with a default
bucket `size` of 10000 (`DefaultGroupBySize`). Extracted metric values SHALL always be surfaced as `double?`.
When parsing a grouped response, the system SHALL throw `InvalidOperationException` for a
terms aggregation under a query with more than one group-by field, and for a response that is
not every group — a terms aggregation with `sum_other_doc_count > 0`, or a terms or composite
aggregation holding `DefaultGroupBySize` buckets or more.

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

#### Scenario: Multi-field terms grouping is refused

- **Given** a response parsed with `hasGroupBy == true`, more than one `GroupByFields` entry, and a `group_by` **terms** aggregation
- **When** `ParseAggregateResponse` runs
- **Then** an `InvalidOperationException` reports that a terms bucket carries one key while the query groups by several fields — each row used to carry only metric values, so the rows could not be told apart

#### Scenario: A truncated group-by response is refused

- **Given** a `group_by` terms aggregation whose `sum_other_doc_count` is greater than zero, or a terms or composite aggregation returning `DefaultGroupBySize` (10000) buckets or more
- **When** `ParseAggregateResponse` runs, at the top level or inside a time bucket
- **Then** an `InvalidOperationException` names how many groups were returned and how many documents were left out (or that a full page may have a successor), and advises narrowing the filter or grouping by fewer distinct values — a high-cardinality group-by used to return a complete-looking subset

#### Scenario: Min/Max on a date field is converted from epoch milliseconds

- **Given** the aggregate `Min<Order,DateTime>(o => o.Created, v => v.Earliest)` — a shape `ViewDefinitionBuilder` permits — and a `min` aggregation whose value NEST reports as epoch milliseconds
- **When** `ExtractMetricValues` returns it as a `double?` and `ElasticSearchViewStore.SetPropertyValue` assigns it
- **Then** `ConvertValue` reads the number as Unix milliseconds and `TView.Earliest` is the UTC instant (a `DateTimeOffset` target gets the offset form; a string is parsed invariantly assuming UTC; an unspecified-kind `DateTime` is taken as UTC). It used to be left at `default(DateTime)` with no error

### Requirement: Cosmos DB view store execution

The system SHALL take the hand-built grouping Cosmos SQL path in `QueryAsync`,
`QueryFirstAsync` and `CountAsync` alike whenever the definition has aggregates **or** group-by
clauses, and otherwise a hand-built projection —
`SELECT c["{SourceProperty}"] AS {ViewProperty}, … FROM c` (or `SELECT VALUE c` when there are
no field selectors) for queries, `SELECT VALUE COUNT(1) FROM c` (results summed) for a count.
Both paths SHALL map view property names back to source field names in the `WHERE` clause,
order aggregate results by their SELECT alias and other keys by `c.{source field}`, and SHALL
emit `OFFSET {offset ?? 0} LIMIT {limit ?? int.MaxValue}` whenever either paging argument is
present. A grouping view's `CountAsync` SHALL run the view's own grouping query and return the
number of groups it yields. A definition with joins SHALL be refused at construction with
`NotSupportedException`, and every call on a `Persistent` definition SHALL throw
`NotSupportedException`, Cosmos DB having no stored views; `OnTheFly` and `Auto` are computed
on the fly.

#### Scenario: Group-by-only view is routed consistently

- **Given** a definition with `HasGroupBy == true` and `HasAggregates == false`
- **When** `QueryAsync`, `QueryFirstAsync` or `CountAsync` is called
- **Then** each takes the grouping SQL path: `QueryFirstAsync` answers the first group and `CountAsync` the number of groups — they used to take the LINQ path and return a raw ungrouped document / the raw document count

#### Scenario: A plain view is projected under its view names

- **Given** a definition without aggregates or group-by, selecting `Order.StatusCode` into `TView.Status`
- **When** `QueryAsync(v => v.Status == 3)` runs
- **Then** the statement is `SELECT c["StatusCode"] AS Status FROM c WHERE c.StatusCode = @p0` with `3` bound — the old LINQ path deserialised raw documents into the view, so a renamed field stayed default and a filter or sort on a view name tested a field that does not exist

#### Scenario: A Persistent definition or a join is refused

- **Given** a `Persistent` definition, or one that joins `Customer`
- **When** a `CosmosViewStore` is constructed over the join, or any query or count is called on the Persistent one
- **Then** a `NotSupportedException` is thrown — a join explains that a Cosmos DB view reads one container and to denormalise the joined fields; joins used to be accepted and never read, every joined property staying default

#### Scenario: A filter on an aggregate view property is refused

- **Given** an aggregate view whose `Sum` targets `TView.Total`, and the filter `v => v.Total > 100`
- **When** the `WHERE` clause is built
- **Then** `MapViewPropertyToSource` throws `NotSupportedException` — a `WHERE` runs on the documents before grouping, and an aggregate would need `HAVING`

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

### Requirement: Cosmos DB filter translation refuses what it cannot express

The system SHALL translate a filter expression into a Cosmos SQL `WHERE` fragment supporting
`AndAlso`, `OrElse`, `Not`, the six binary comparisons, `Contains` on a member target, and a
boolean constant, and SHALL throw `NotSupportedException` whenever any part of the expression
cannot be translated.

An empty return SHALL mean exactly one thing — the predicate is the explicit constant `true`,
which constrains nothing. It SHALL NOT mean that translation failed: both callers append the
`WHERE` only when the clause is non-empty, so a swallowed failure ran the aggregate over every
document and a tenant-scoped query returned other tenants' rows (SH-H055). This is the same
invariant the ElasticSearch view store holds under CR-H047.

A boolean constant SHALL render as the SQL literal `true` or `false` wherever it appears inside
the expression, so that a constant nested in a conjunction produces valid SQL rather than an
empty fragment joined between its neighbours' separators.

Every value the filter compares against SHALL be **bound as a query parameter** named `@p0`, `@p1`,
… in the order encountered, and SHALL NOT be rendered into the statement text. TASK-447: values used
to be inlined as literals with only the quote escaped, and Cosmos NoSQL uses **backslash** as its
escape character — so a value containing a backslash consumed that escape, ended its own literal
early, and the remainder was parsed as SQL. On an aggregate view the predicate is the only thing
scoping the query, so a caller-supplied value could widen it to every document. Parameters are used
rather than stricter escaping because a bound value has no grammar to break out of.

A parameter's value SHALL be the operand's CLR value, with one exception: an operand that arrives as
a boxed `Enum` SHALL be converted to its underlying integral value, because Birko's own Cosmos store
writes enums in numeric form. An ordinary enum comparison never reaches that conversion — C# builds
`v.State == Status.Published` as `Convert(…, Int32) == Convert(…, Int32)`, so the operand is already
an `Int32`. `OFFSET` and `LIMIT` SHALL remain interpolated, being `int?` values from the store's own
API with no caller text in them.

#### Scenario: An untranslatable predicate is refused

- **Given** the filter `v => v.Tags.Any(t => t == "x")` on an aggregate Cosmos view
- **When** `CosmosFilterTranslator.Translate` runs
- **Then** the `NotSupportedException` propagates to the caller, and no query is built — rather than a query with no `WHERE` that aggregates every document

#### Scenario: The count path refuses it too

- **Given** the same untranslatable filter
- **When** `CountAsync` runs, on a grouping or a plain view
- **Then** it refuses identically; every statement — grouping, projection and count — appends its `WHERE` through the one `Where` helper, and a grouping view's count runs `BuildAggregateSql` itself

#### Scenario: An operand that cannot be evaluated is reported as a translation failure

- **Given** a column-vs-column filter such as `v => v.TenantGuid == v.OwnerGuid`
- **When** `EvaluateValue` compiles the right operand and `Compile()` throws because the lambda parameter is not in scope
- **Then** a `NotSupportedException` naming the operand's node type is thrown with the original exception as its inner — one exception type selects every "this filter cannot be translated" case, and the message carries the node's shape rather than the rendered expression

#### Scenario: Constant on the left of a comparison is refused

- **Given** the filter `v => 100 < v.Total`
- **When** the binary expression is translated
- **Then** `TranslateFieldAccess` throws on the constant left operand and the exception propagates; the filter is not silently dropped

#### Scenario: An explicit always-true predicate constrains nothing

- **Given** the filter `v => true`
- **When** `Translate` runs
- **Then** an empty clause is returned and no `WHERE` is appended — the caller asked for every document. The check is on the expression body, and accepts a single `ConstantExpression` node only, never a shape that merely reduces to true

#### Scenario: An explicit always-false predicate matches nothing

- **Given** the filter `v => false`
- **When** `Translate` runs
- **Then** `WHERE false` is emitted. Before SH-H055 this took the same swallow as an untranslatable node and therefore matched **every** document — the opposite of what it asks for

#### Scenario: A boolean constant nested in a conjunction renders as a literal

- **Given** the filter `v => v.Total > 0 && true`
- **When** the conjunction is translated
- **Then** the emitted clause is `(c.Amount > 0 AND true)` — a constant is rendered wherever it appears, because an empty fragment would be joined between the `AND` separators into invalid SQL

#### Scenario: Enum and DateTime values reach the parameter, not the statement

- **Given** the filters `v => v.Status == OrderStatus.Active` and `v => v.Created > new DateTime(2026, 1, 1)`
- **When** the values are translated
- **Then** each clause carries a `@pN` placeholder and the CLR value is bound; the SDK serializes it with the same serializer that wrote the documents, so no hand-written literal format is involved. CR-M086 fixed that format twice while it existed

#### Scenario: Values are parameterized, not inlined

- **Given** a string filter value containing a single quote, a backslash, or both
- **When** it is translated
- **Then** the statement carries `@p0` and the value is attached to the `QueryDefinition` unchanged; no caller-supplied text appears in the query text at all, and nothing is escaped because nothing needs to be

#### Scenario: A payload cannot widen the predicate

- **Given** a filter value crafted to close the literal and append `OR 1=1 --`
- **When** the aggregate and count statements are built
- **Then** both emit `WHERE c.Category = @p0`, neither contains `OR` or `--`, and the value reaches the parameter verbatim

#### Scenario: Several values bind in order

- **Given** the filter `v => v.CategoryName == "books" && v.Total > 10`
- **When** it is translated
- **Then** the clause references `@p0` and `@p1`, bound to `"books"` and `10` respectively

#### Scenario: A filter that restricts nothing binds nothing

- **Given** a `null` filter, or the explicit constant `v => true`
- **When** the statement is built
- **Then** no `WHERE` is appended and the `QueryDefinition` carries no parameters

### Requirement: Store-agnostic aggregation query specification

The system SHALL describe a store-level aggregation with `AggregateQuery<T>` (where
`T : AbstractModel`) carrying an optional pre-aggregation `Filter`, `GroupByFields` (property
names), `Aggregates`, an optional `TimeBucketInterval` plus `TimeColumn`, an optional
`OrderBy` map of field → descending, and `Limit`/`Offset`, with `GroupByFields` and
`Aggregates` defaulting to empty. Each `AggregateField` SHALL resolve its output key as its
explicit `Alias`, or `"{function-lowercase}_{SourcePropertyName}"` when no alias is given.
Server-side aggregation SHALL be offered as the *optional* interfaces
`IAggregatableStore<T>.Aggregate` and `IAsyncAggregatableStore<T>.AggregateAsync`.
`WithFilter(filter)` SHALL return a new query identical in every other member, with `filter`
in place of `Filter` (what a scoping decorator uses to narrow a caller's query).

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

#### Scenario: WithFilter replaces only the filter

- **Given** a query with a filter, group-by fields, aggregates, a time bucket, ordering and paging
- **When** `WithFilter(other)` is called
- **Then** a new `AggregateQuery<T>` is returned whose `Filter` is `other` and whose every other member is the original's; the original is unchanged

### Requirement: LINQ aggregation fallback

The system SHALL provide `AggregateHelper.LinqAggregateAsync` as the aggregation
implementation for stores without native support: it SHALL compile and apply `Filter`, and
SHALL resolve every group-by field, aggregate source property and time column
case-insensitively against `T`'s public instance properties, throwing `ArgumentException`
("Aggregate {role} '{name}' is not a property of {T}.") for a name that resolves to nothing.
When `Aggregates` and `GroupByFields` are both empty and no time bucketing is requested it
SHALL return one `AggregateResult` per source item holding every public instance property of
that item, ordered and paged like any other result. Otherwise it SHALL group by the key tuple of
the group-by values (plus the bucket time when bucketing), compared element by element with
`object.Equals`, compute each aggregate per group, and apply ordering, offset and limit.
Grouping SHALL NOT mutate the source entities.
`LinqAggregate` SHALL be a blocking wrapper (`GetAwaiter().GetResult()`) over the async form.

#### Scenario: No aggregates and no grouping returns raw rows

- **Given** three `Order` instances and `new AggregateQuery<Order>()`
- **When** `LinqAggregateAsync` runs
- **Then** three `AggregateResult`s are returned, each keyed by the entity's public property names

#### Scenario: Raw rows are ordered and paged

- **Given** five `Order` instances and a query with no aggregates or grouping but `OrderBy == { ["Number"] = false }`, `Offset == 1`, `Limit == 2`
- **When** `LinqAggregateAsync` runs
- **Then** two raw rows are returned — the second and third by `Number` — through the same `ApplyOrderingAndPaging` as aggregated rows; they used to be returned whole

#### Scenario: Group keys are compared as tuples, not joined strings

- **Given** orders grouped by two string fields with values `("a|b", "c")` and `("a", "b|c")`, or one field holding `null` on one order and `""` on another
- **When** the helper groups them
- **Then** each falls into its own group — the old `"|"`-joined key put both pairs (and `null` with `""`) in one group

#### Scenario: Count uses the group size, other functions read the property

- **Given** orders grouped by `Status` with aggregates `Count` and `Sum` of `Total`
- **When** the helper runs
- **Then** each row carries the group key under its group-by field name, `count_<x>` set to `group.Count()` as an `int`, and `sum_Total` computed by `AggregateMath`

#### Scenario: An unresolvable aggregate source property is refused

- **Given** an `AggregateField(Sum, "Nonexistent")`
- **When** the helper runs
- **Then** an `ArgumentException` reads "Aggregate aggregate source 'Nonexistent' is not a property of Order." — the aggregate used to be skipped and its column was simply absent

#### Scenario: Time bucketing does not mutate the source entities

- **Given** an InMemory store's live `Order` instances, `TimeBucketInterval == "1 hour"` and `TimeColumn == "Created"`
- **When** the helper groups and builds rows
- **Then** each row gains a `bucket_time` computed by `AggregateMath.TruncateToBucket`, and every source entity's `Created` value is unchanged

#### Scenario: An unparseable or zero interval is refused

- **Given** `TimeBucketInterval == "fortnightly"`, or a `TimeColumn` that does not resolve to a property, or only one of `TimeBucketInterval` / `TimeColumn` set
- **When** the helper runs
- **Then** an `ArgumentException` is thrown — "Time bucket interval 'fortnightly' is not a positive interval.", the unresolved-property message, or "Time bucketing needs both TimeBucketInterval and TimeColumn." respectively. Bucketing used to be skipped silently with no `bucket_time` key in any row

#### Scenario: An unresolvable group-by field is refused

- **Given** `GroupByFields == ["Bogus", "Status"]` where `Bogus` is not a property of `T`
- **When** the helper runs
- **Then** an `ArgumentException` reads "Aggregate group-by field 'Bogus' is not a property of <T>." — the field used to be filtered out of the key array, so the `Status` value was written under the key `"Bogus"`

#### Scenario: Ordering and paging are applied after aggregation

- **Given** aggregate results and `OrderBy == { ["sum_Total"] = true }`, `Offset == 2`, `Limit == 3`
- **When** `ApplyOrderingAndPaging` runs
- **Then** the rows are sorted descending by the boxed value of `sum_Total`, the first two are skipped and at most three are returned; multiple order entries chain via `ThenBy`/`ThenByDescending` in the dictionary's enumeration order

### Requirement: SQL native aggregation

The system SHALL let a SQL connector answer an `AggregateQuery<T>` with one `GROUP BY` statement
through `SelectAggregate<T>(type, query)` (sync) and `SelectAggregateAsync<T>(type, query, ct)`
(async, streaming), yielding nothing when `DataBase.LoadTable(type)` returns null. The statement
SHALL select from the type's one table with no joins; each group-by field SHALL be both selected
and grouped by; a time bucket, when both `TimeBucketInterval` and `TimeColumn` are set, SHALL be
selected as `time_bucket('{TimeIntervalParser.ToSqlInterval(interval)}', {column}) AS bucket_time`
and grouped by `bucket_time`; each aggregate SHALL be selected as `{FN}({column}) AS
{QuoteIdentifier(ResolvedAlias)}`, a `Count` always as `COUNT(*)` whatever its source property; the
filter SHALL be parsed with `DataBase.ParseConditionExpression`; and `query.OrderBy`, `Limit` and
`Offset` SHALL be passed to the entity `CreateSelectCommand`. Every property name — group-by field,
time column, aggregate source — SHALL be resolved to its SQL column by `ResolveSqlName`, which matches
the type's field metadata case-insensitively on property name and, when nothing matches, **uses the
supplied name as-is**. Each row SHALL become an `AggregateResult` keyed by the reader's column names,
`DBNull` read as `null`.

#### Scenario: A grouped sum is one GROUP BY statement

- **Given** `AggregateQuery<Order>` with `GroupByFields == ["Status"]` and `Sum` of `Total`
- **When** `SelectAggregate` builds the command
- **Then** it selects `Status` and `SUM(Total) AS <quoted "sum_Total">` from the order table, grouped by `Status`, and each row's result carries the reader's column names as keys

#### Scenario: Count ignores its source property

- **Given** an `AggregateField(Count, "Guid", "n")`
- **When** the select list is built
- **Then** the column is `COUNT(*) AS <quoted "n">` — the source property is not consulted

#### Scenario: An unknown property name reaches the statement verbatim

- **Given** a group-by field, time column or aggregate source naming no property of the type
- **When** the select list is built
- **Then** `ResolveSqlName` returns the supplied text unchanged and it is interpolated into the SELECT / GROUP BY — no refusal is raised before the database sees it

#### Scenario: Time bucketing emits time_bucket

- **Given** `TimeBucketInterval == "1 hour"` and `TimeColumn == "Created"`
- **When** the select list is built
- **Then** it contains `time_bucket('1 hours', Created) AS bucket_time` and `bucket_time` is a GROUP BY key — a TimescaleDB function, so on a provider without it the statement fails at the database; an interval `TimeIntervalParser` cannot parse is embedded between the quotes verbatim

### Requirement: Pure aggregate computation

The system SHALL compute aggregates over boxed values in `AggregateMath`:
`TruncateToBucket` floors a `DateTime` to a multiple of `bucketTicks` preserving its `Kind`;
`ComputeSum` and `ComputeAvg` throw `NotSupportedException` for a non-numeric underlying
(non-nullable) property type — checked before the values, so even for an empty list — return
`null` for an empty value list, and dispatch across `decimal`, `double`, `float`, `int` and
`long` natively, summing `short`, `byte`, `sbyte`, `ushort` and `uint` as `long` and `ulong` as
`decimal`, and averaging every one of those integral types as `double`; `ComputeAggregate` returns the supplied
`groupCount` for `Count` without inspecting the values, strips nulls before `Sum`/`Avg`, and
returns `Min`/`Max` via the default comparer or `null` when nothing non-null remains.

#### Scenario: Sum over a small integral type is a long

- **Given** a property of type `short` (or `byte`, `sbyte`, `ushort`, `uint`) holding non-null values, and one of type `ulong`
- **When** `ComputeSum` runs
- **Then** the `short`-like sums are returned as `long` and the `ulong` sum as `decimal` (which holds any `ulong` sum) — these used to answer `null` where SQL `SUM` and ES `sum` answer a number

#### Scenario: Sum over a non-numeric type is refused

- **Given** a property of type `string` (or `DateTime`)
- **When** `ComputeSum` or `ComputeAvg` runs
- **Then** a `NotSupportedException` reads "Sum is not defined for String." (or "Avg …"), never `null`

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
call order, and `ThenByName(propertyName, descending = false)` appending a field by name with
no property check, as `ByName` starts one. `ToDictionary()` SHALL project the fields to property-name → descending, so
repeated sorts on the same property collapse to the last one.

#### Scenario: Multi-level sort preserves order

- **Given** `OrderBy<Order>.ByDescending(o => o.Created).ThenBy(o => o.Number)`
- **When** `Fields` is read
- **Then** it contains `OrderByField("Created", true)` followed by `OrderByField("Number", false)`

#### Scenario: ByName accepts an arbitrary string

- **Given** `OrderBy<Order>.ByName("bucket_time", descending: true)`
- **When** `Fields` is read
- **Then** it contains a single `OrderByField("bucket_time", true)` with no compile-time or runtime property check

#### Scenario: ThenByName appends a named key

- **Given** `OrderBy<Order>.ByName("Created", descending: true).ThenByName("Guid")`
- **When** `Fields` is read
- **Then** it contains `OrderByField("Created", true)` followed by `OrderByField("Guid", false)`, and `ThenByName` returns the same instance

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

The system SHALL parse an interval string culture-invariantly, by first attempting
`TimeSpan.TryParse` with `CultureInfo.InvariantCulture`, then falling back to a two-token
`"{number} {unit}"` form (split on spaces with empty entries removed, the number parsed with
`NumberStyles.Float` invariantly) recognising second(s)/s,
minute(s)/m/min/mins, hour(s)/h/hr/hrs and day(s)/d case-insensitively, and SHALL return
`TimeSpan.Zero` — never throw — for a null, whitespace, unrecognised-unit or otherwise
unparseable input. `ToSqlInterval` SHALL render a parsed non-zero span as the largest whole
unit among days, hours, minutes, seconds, and SHALL pass the original string through
unchanged when parsing yields zero.

#### Scenario: Human-readable and TimeSpan forms both parse

- **Given** the inputs `"5 minutes"`, `"30 s"`, `"1 hr"` and `"00:15:00"`
- **When** `TimeIntervalParser.Parse` runs
- **Then** the results are 5 minutes, 30 seconds, 1 hour and 15 minutes

#### Scenario: Parsing does not depend on the process culture

- **Given** the current culture is `sk-SK` (comma decimal separator) and the inputs `"1.5 hours"` and `"2  minutes"` (two spaces)
- **When** `Parse` runs
- **Then** 90 minutes and 2 minutes are returned — the first used to come back as `TimeSpan.Zero` under a comma-decimal culture, so one query bucketed differently by process locale

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
