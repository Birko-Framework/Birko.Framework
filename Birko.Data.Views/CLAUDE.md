# Birko.Data.Views

Unified fluent view builder for defining cross-platform views/projections/aggregations. Replaces attribute-based SQL view definitions with a fluent API that works across SQL, MongoDB, ElasticSearch, RavenDB, and Cosmos DB.

## Components

### Enums
- **ViewQueryMode** — OnTheFly (computed at query time), Persistent (materialized), Auto (try persistent, fall back)
- **AggregateFunction** — Count, Sum, Avg, Min, Max
- **JoinType** — Inner, LeftOuter, Cross

### Builder
- **ViewDefinitionBuilder\<TView\>** — Fluent API: From\<T\>(), Select(), Join(), LeftJoin(), GroupBy(), Count(), Sum(), Avg(), Min(), Max(), Hint(), Build()
- **ViewDefinition** — Immutable result produced by builder

### DTOs
- **FieldSelector** — Source type + property + alias
- **JoinClause** — Left/right types + keys + join type
- **AggregateClause** — Function + source property + output alias
- **GroupByClause** — Source type + property

### Configuration (follows IModelMapping pattern)
- **IViewMapping\<TView\>** — Configure(ViewDefinitionBuilder\<TView\>)
- **ViewMapRegistry** — Register\<T\>(), RegisterFromAssembly(), GetDefinition\<T\>()

### Interfaces
- **IViewStore\<TView\>** — QueryAsync, QueryFirstAsync, CountAsync (read-only)
- **IViewManager** — EnsureAsync, DropAsync, ExistsAsync, RefreshAsync (persistent view lifecycle)
- **ViewResult\<TView\>** — Items + TotalCount

## Dependencies
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy\<T\>)

## Namespace
`Birko.Data.Views`
