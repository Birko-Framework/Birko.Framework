# Birko.Data.Aggregates

SQL-to-NoSQL aggregate mapper for the Birko Framework. Transforms normalized relational data (with junction tables and foreign keys) into nested NoSQL documents (flatten), and reverse-decomposes nested documents back into relational store operations (expand).

## Features

- **Fluent aggregate definition** — Define relationships in a constructor using `HasMany`/`HasOne` with `Via` (direct FK) or `Through` (junction table)
- **Flatten** — Compose related entities from SQL stores into nested aggregate documents for NoSQL stores
- **Expand** — Decompose aggregate documents back into insert/delete operations for relational stores
- **Collection diffing** — Detect added/removed entities by Guid when expanding aggregates
- **Store-agnostic** — Uses `IRelatedDataProvider` interface; works with any Birko.Data store implementation
- **Sync pipeline integration** — Extension methods for use with Birko.Data.Sync providers

## Usage

### Define an aggregate shape

```csharp
using Birko.Data.Aggregates.Core;

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

### Flatten (SQL to NoSQL)

```csharp
using Birko.Data.Aggregates.Mapping;

var definition = new ProductAggregate();
var mapper = new AggregateMapper<Product>(definition);

// IRelatedDataProvider implementation queries your SQL stores
var dataProvider = new SqlRelatedDataProvider(categoryStore, tagStore, imageStore);

// Flatten a single product with all related data
FlattenResult<Product> aggregate = mapper.Flatten(product, dataProvider);

// Access nested data
var categories = aggregate.GetCollection<Category>("Categories");
var defaultImage = aggregate.GetSingle<Image>("DefaultImage");

// Flatten many for batch sync
var aggregates = mapper.FlattenMany(products, dataProvider);
```

### Expand (NoSQL to SQL)

```csharp
// Expand aggregate back into relational operations
// Diffs desired state (from aggregate) against current state (from provider)
IEnumerable<SyncOperation> operations = mapper.Expand(aggregate, currentStateProvider);

foreach (var op in operations)
{
    // op.Type: Insert, Update, or Delete
    // op.EntityType: which table/entity
    // op.Entity: the entity to operate on
    // op.NavigationProperty: which relationship
}
```

### Sync pipeline integration

```csharp
using Birko.Data.Aggregates.Extensions;

// Create mapper from definition
var mapper = definition.CreateMapper();

// Flatten for sync upload
var aggregates = mapper.FlattenForSync(products, sqlDataProvider);

// Expand from sync download
var operations = mapper.ExpandFromSync(aggregate, sqlCurrentStateProvider);
```

## Architecture

```
SQL (normalized)                    NoSQL (denormalized)
┌──────────┐                       ┌─────────────────────────┐
│ Products │──── Flatten ────────► │ {                       │
├──────────┤                       │   product: {...},       │
│ ProductCategories (junction) │   │   categories: [...],    │
├──────────┤                       │   tags: [...],          │
│ Categories │                     │   defaultImage: {...}   │
├──────────┤                       │ }                       │
│ Tags     │◄── Expand ──────────  └─────────────────────────┘
├──────────┤    (diff-based)
│ Images   │
└──────────┘
```

## Dependencies

- **Birko.Data.Core** — AbstractModel base class
- **Birko.Data.Stores** — Store interfaces (for settings)

## Important

This project handles **data shape mapping for sync only**. It does NOT attempt query translation (SQL joins to ES nested queries). Query logic remains in store-specific repository implementations.

## License

MIT License - see [License.md](License.md)
