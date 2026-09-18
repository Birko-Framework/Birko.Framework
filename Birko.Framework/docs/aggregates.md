# Aggregates Guide

## Overview

`Birko.Data.Aggregates` maps between **normalized relational schemas** (SQL with FKs and junction tables) and **denormalized nested documents** (NoSQL). It solves two shape-mapping problems:

- **Flatten** — given a relational root entity, gather all related children via foreign keys / junction tables into a single composite structure ready for a NoSQL write
- **Expand** — given a nested document, compute the minimal set of insert/update/delete operations needed to reconstitute the relational form (with diffing, so unchanged rows are untouched)

Typical use: a cross-store sync pipeline that replicates SQL → MongoDB (flatten), or MongoDB → SQL (expand).

## Problem solved

When you sync a `Product` from SQL to MongoDB, you want to push the product **and** its categories (via a `ProductCategory` junction), tags (direct FK), and default image (one-to-one) as a single nested document. When syncing back, you don't want to delete and re-insert everything — you want diffing so only added/removed children generate operations.

Birko.Aggregates gives you a definition language for this mapping, a mapper that executes it, and operation types you can feed into any store.

## Public API

### `AggregateDefinition<T>`

Fluent builder for describing the aggregate shape. Subclass it and configure in the constructor:

```csharp
public class ProductAggregate : AggregateDefinition<Product>
{
    public ProductAggregate()
    {
        HasMany(p => p.Categories)
            .Through<ProductCategory>(j => j.ProductGuid, j => j.CategoryGuid);

        HasMany(p => p.Tags)
            .Via(t => t.ProductGuid);

        HasOne(p => p.DefaultImage)
            .Via(i => i.ProductGuid);
    }
}
```

**Relationship types:**

| Type | Syntax | When to use |
|---|---|---|
| **OneToOne** | `HasOne(p => p.Child).Via(c => c.ParentGuid)` | Child has direct FK to parent; single related entity |
| **OneToMany** | `HasMany(p => p.Children).Via(c => c.ParentGuid)` | Child has direct FK to parent; collection |
| **ManyToMany** | `HasMany(p => p.Children).Through<TJunction>(j => j.ParentGuid, j => j.ChildGuid)` | Related via junction table |

### `IAggregateMapper<T>` / `AggregateMapper<T>`

Default implementation. Takes an `IAggregateDefinition` in the constructor.

```csharp
public interface IAggregateMapper<T>
{
    FlattenResult<T> Flatten(T root, IRelatedDataProvider dataProvider);
    Task<FlattenResult<T>> FlattenAsync(T root, IAsyncRelatedDataProvider dataProvider, CancellationToken ct);
    IEnumerable<FlattenResult<T>> FlattenMany(IEnumerable<T> roots, IRelatedDataProvider dataProvider);
    IEnumerable<SyncOperation> Expand(FlattenResult<T> aggregate, IRelatedDataProvider currentStateProvider);
    Task<IEnumerable<SyncOperation>> ExpandAsync(FlattenResult<T> aggregate, IAsyncRelatedDataProvider currentStateProvider, CancellationToken ct);
}
```

### `FlattenResult<T>`

Container for a flattened aggregate:

```csharp
public class FlattenResult<T>
{
    public T Root { get; }
    public IReadOnlyDictionary<string, IEnumerable<AbstractModel>> NestedCollections { get; }
    public IReadOnlyDictionary<string, AbstractModel?> NestedSingles { get; }

    public IEnumerable<TChild> GetCollection<TChild>(string propertyName);
    public TChild? GetSingle<TChild>(string propertyName);
}
```

### `SyncOperation`

```csharp
public class SyncOperation
{
    public SyncOperationType Type { get; set; }      // Insert | Update | Delete
    public Type EntityType { get; set; }
    public AbstractModel Entity { get; set; }
    public string NavigationProperty { get; set; }   // "Categories", "Tags", ...
}
```

## Related data providers

The mapper is **store-agnostic**. You provide an `IRelatedDataProvider` (or `IAsyncRelatedDataProvider`) that knows how to fetch child entities from your stores:

```csharp
public interface IRelatedDataProvider
{
    IEnumerable<AbstractModel> GetRelated(Guid parentGuid, RelationshipDescriptor relationship);
    IEnumerable<AbstractModel> GetRelatedViaJunction(Guid parentGuid, RelationshipDescriptor relationship);
}
```

Your implementation can fetch categories from SQL, tags from Elasticsearch, and images from blob metadata — the mapper doesn't care.

## Flatten (SQL → NoSQL)

```csharp
var definition = new ProductAggregate();
var mapper = new AggregateMapper<Product>(definition);
var dataProvider = new SqlRelatedDataProvider(categoryStore, tagStore, imageStore);

// Single product
FlattenResult<Product> aggregate = mapper.Flatten(product, dataProvider);

var categories   = aggregate.GetCollection<Category>("Categories");
var tags         = aggregate.GetCollection<Tag>("Tags");
var defaultImage = aggregate.GetSingle<Image>("DefaultImage");

// Now ready to push to MongoDB/Cosmos as a single document

// Bulk
IEnumerable<FlattenResult<Product>> many = mapper.FlattenMany(products, dataProvider);
```

## Expand (NoSQL → SQL)

```csharp
// Flatten the incoming NoSQL document (deserialize → FlattenResult<T>)
var aggregate = DeserializeFromMongo(mongoDoc);

// Compute the minimal ops needed vs. current relational state
IEnumerable<SyncOperation> ops = mapper.Expand(aggregate, currentStateProvider);

foreach (var op in ops)
{
    switch (op.Type)
    {
        case SyncOperationType.Insert: await store.CreateAsync(op.Entity); break;
        case SyncOperationType.Update: await store.UpdateAsync(op.Entity); break;
        case SyncOperationType.Delete: await store.DeleteAsync(op.Entity); break;
    }
}
```

**Diffing strategy:** `Expand` uses `Birko.Helpers.EnumerableHelper.DiffByKey(current, desired, e => e.Guid)` to classify each child as Insert / Update / Delete. Unchanged children produce no operation.

## Typical use cases

1. **SQL ↔ NoSQL replication** — Push Products with nested Categories/Tags/Images to MongoDB; pull them back and diff.
2. **Event sourcing snapshots** — Flatten an aggregate into a single event payload; expand on replay.
3. **GraphQL shape mapping** — Flatten relational data into the nested shape a GraphQL resolver expects.
4. **Import pipelines** — Accept a denormalized document from an external system; expand into the right inserts/updates.
5. **Polymorphic relationships** — A Product with categories via junction AND tags via direct FK is handled by one definition.

## Dependencies

- **Birko.Data.Core** — `AbstractModel` (all entities must extend this; `.Guid` is the diffing key)
- **Birko.Data.Stores** — Store interfaces (for settings only, not direct store access)
- **Birko.Helpers** — `EnumerableHelper.DiffByKey` for collection diffing

## Design notes

- **Store-agnostic** — The mapper never talks to a store directly; all reads go through your `IRelatedDataProvider`. You control caching, query batching, and cross-store fetching.
- **No query translation** — Aggregates does shape mapping, not SQL generation. Your provider implementation is where SQL/Mongo/ES queries live.
- **Guid-based diffing** — Uses `AbstractModel.Guid` as the diff key. Entities without a Guid cannot be diffed.
- **Lazy relationship loading** — `Flatten()` iterates relationships one at a time and calls the provider for each. For bulk use cases, implement batch-fetching logic in your provider.
- **One-way per call** — Flatten and Expand are independent; compose them in your sync pipeline rather than inside the mapper.

## See also

- [Data Synchronization Guide](sync.md) — the broader sync framework that uses aggregates to shape cross-store replication
- [Event Sourcing Guide](event-sourcing.md) — aggregate-per-event snapshotting
- [Store Composition Guide](composition.md) — runtime decorator composition for the stores you feed into your data providers
