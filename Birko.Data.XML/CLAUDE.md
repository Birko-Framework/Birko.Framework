# Birko.Data.XML

XML file-based storage implementation for the Birko Framework.

## Overview

Birko.Data.XML provides XML file-based data persistence using .NET's built-in `System.Xml.Serialization.XmlSerializer`. It's ideal for configuration files, data interchange with systems requiring XML, and scenarios where human-readable XML files are preferred.

## Architecture

### Store Hierarchy
```
AbstractXmlStore (sync)
  -> XmlStore (single file)
  -> XmlSeparateStore (separate files per entity)
    -> XmlSeparateBulkStore (bulk operations)
    -> XmlBatchStore (batched files)
      -> XmlBatchBulkStore (bulk operations with batching)

AbstractAsyncXmlStore (async)
  -> AsyncXmlStore (single file)
  -> AsyncXmlSeparateStore (separate files per entity)
    -> AsyncXmlSeparateBulkStore (bulk operations)
    -> AsyncXmlBatchStore (batched files)
      -> AsyncXmlBatchBulkStore (bulk operations with batching)
```

### Key Differences from JSON Store

| Feature | JSON Store | XML Store |
|---------|------------|-----------|
| Serialization | System.Text.Json | System.Xml.Serialization |
| File Extension | .json | .xml |
| Attributes | Not supported | XmlAttribute supported |
| Schema Validation | No | Yes (XSD) |
| Requirements | Parameterless ctor optional | Parameterless ctor required |
| Property Requirements | Public getter/setter | Public getter/setter |

## Implementation Details

### XML Serialization Requirements

Models must:
1. Have a parameterless constructor
2. Have public properties with both get and set
3. Handle XML serialization attributes appropriately

```csharp
public class Customer : Models.AbstractModel
{
    public Customer() { } // Required for XML serialization

    [XmlElement("CustomerName")]
    public string Name { get; set; } // Must have public setter

    [XmlAttribute("IsActive")]
    public bool IsActive { get; set; }

    [XmlIgnore]
    public string TemporaryData { get; set; }
}
```

### File Storage Patterns

1. **Single File** (XmlStore): All entities in one XML file
2. **Separate Files** (XmlSeparateStore): Each entity in its own XML file (`{Name}-{Guid}.xml`)
3. **Batched Files** (XmlBatchStore): Entities grouped into batch files based on `BatchSize`

### Path Validation

All stores use `Birko.Helpers.PathValidator` for:
- Directory traversal prevention
- Path validation and normalization
- Safe path combination

## Usage Examples

### Basic Store Usage

```csharp
var store = new XmlStore<Customer>();
store.SetSettings(new Settings
{
    Location = "data",
    Name = "customers.xml"
});

// CRUD operations
var id = store.Create(new Customer { Name = "John" });
var customer = store.Read(id);
customer.Name = "Jane";
store.Update(customer);
store.Delete(customer);
```

### Async Operations

```csharp
var store = new AsyncXmlStore<Customer>();
store.SetSettings(new Settings
{
    Location = "data",
    Name = "customers.xml"
});

await store.CreateAsync(customer);
var found = await store.ReadAsync(id);
```

### Repository Pattern

```csharp
var store = new XmlStore<Customer>();
store.SetSettings(new Settings { Location = "data", Name = "customers.xml" });

// Direct model repository
var repository = new XmlModelRepository<Customer>(store);

// ViewModel repository
var vmRepository = new XmlRepository<CustomerViewModel, Customer>(store);
```

## Testing

Tests should be in `Birko.Data.XML.Tests` project following the pattern:
- Unit tests for serialization/deserialization
- Integration tests for file operations
- Tests for all store variants (single, separate, batch)
- Tests for sync and async variants

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Helpers (PathValidator)
- System.Xml.Serialization (XML serialization)

## Related Projects

- Birko.Data.JSON - JSON file store (similar pattern, different serialization)
- Birko.Data.Core - Core models and types
- Birko.Data.Stores - Store interfaces and base classes
- Birko.Helpers - Utility classes including PathValidator
