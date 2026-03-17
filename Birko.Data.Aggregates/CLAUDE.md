# Birko.Data.Aggregates

## Overview
SQL-to-NoSQL aggregate mapper. Defines aggregate shapes (which related entities to include), flattens normalized SQL data into nested documents for NoSQL stores, and expands nested documents back into relational insert/delete operations.

## Project Location
`C:\Source\Birko.Data.Aggregates\` (Shared Project - `.shproj`)

## Components

### Core/ — Aggregate definitions and relationship metadata
- **RelationshipType.cs** — Enum: OneToOne, OneToMany, ManyToMany
- **RelationshipDescriptor.cs** — Metadata for one relationship (parent/child types, FK names, junction table)
- **ExpressionHelper.cs** — Internal helper to extract property names from lambda expressions
- **IAggregateDefinition.cs** — Interface: RootType + Relationships list
- **RelationshipBuilder.cs** — Fluent builder: Via(foreignKey) for direct FK, Through<TJunction>(parentFk, childFk) for m:n
- **AggregateDefinition\<T\>.cs** — Abstract base class with HasMany/HasOne fluent methods (define in constructor)

### Mapping/ — Flatten and expand logic
- **FlattenResult\<T\>.cs** — Result container: Root entity + NestedCollections + NestedSingles dictionaries
- **IRelatedDataProvider.cs** — Interface for fetching related data (sync + async versions)
- **IAggregateMapper\<T\>.cs** — Interface: Flatten, FlattenMany, Expand, ExpandAsync
- **AggregateMapper\<T\>.cs** — Default implementation using IAggregateDefinition + IRelatedDataProvider
- **SyncOperationType.cs** — Enum: Insert, Update, Delete
- **SyncOperation.cs** — Single store operation (Type, EntityType, Entity, NavigationProperty)

### Extensions/ — Sync integration
- **SyncPipelineExtensions.cs** — Extension methods: CreateMapper, FlattenForSync, ExpandFromSync, ExpandManyFromSync

## Dependencies
- **Birko.Data.Core** — AbstractModel (all mapped entities extend this)
- **Birko.Data.Stores** — Store interfaces (settings base classes)
- **Birko.Helpers** — EnumerableHelper.DiffByKey for collection diffing

## Key Design Decisions
- **Store-agnostic via IRelatedDataProvider** — Mapper does not depend on any specific store. Callers implement the provider interface using their stores.
- **No query translation** — Only maps data shapes for sync. Query logic stays in store-specific repositories.
- **Collection diffing via Birko.Helpers** — Uses `EnumerableHelper.DiffByKey` with `e => e.Guid` as key selector (generic, reusable utility)

## Maintenance
- When adding new relationship types, update RelationshipType enum and add corresponding logic in AggregateMapper
- When modifying AbstractModel, ensure FlattenResult and the DiffByKey key selector (`e => e.Guid`) still work correctly
