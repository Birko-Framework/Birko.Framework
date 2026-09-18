# Birko.Data.XML

XML file-based storage implementation for the Birko Framework. Good for human-readable data files and configuration storage.

## Features

- File-based data persistence (no database required)
- Sync and async stores with bulk operations
- Thread-safe file access with locking
- Human-readable XML data files
- Auto-save on every operation
- Support for single file, separate files, and batched files

## Installation

```bash
dotnet add package Birko.Data.XML
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Helpers (PathValidator)
- System.Xml.Serialization

## Usage

### Single File Store (All entities in one XML file)

```csharp
using Birko.Data.XML.Stores;

var store = new XmlStore<Customer>();
store.SetSettings(new Settings { Location = "data", Name = "customers.xml" });

// Create
var customer = new Customer { Name = "John Doe" };
var id = store.Create(customer);

// Read
var found = store.Read(id);

// Update
found.Name = "Jane Doe";
store.Update(found);

// Read all
var customers = store.Read();

// Delete
store.Delete(found);
```

### Separate Files Store (Each entity in its own file)

```csharp
var store = new XmlSeparateStore<Customer>();
store.SetSettings(new Settings { Location = "data/customers", Name = "customer" });

// Each customer is stored in a separate file: data/customers/customer-{Guid}.xml
```

### Batched Files Store (Entities grouped into batch files)

```csharp
var store = new XmlBatchStore<Customer>();
store.SetSettings(new BatchSettings
{
    Location = "data/customers",
    Name = "batch",
    BatchSize = 1000 // Each batch file contains up to 1000 entities
});
```

### Async Store

```csharp
var store = new AsyncXmlStore<Customer>();
store.SetSettings(new Settings { Location = "data", Name = "customers.xml" });

// Async operations
await store.CreateAsync(customer);
var found = await store.ReadAsync(id);
await store.UpdateAsync(found);
var customers = await store.ReadAsync();
await store.DeleteAsync(found);
```

### Repository Pattern

```csharp
using Birko.Data.XML.Repositories;

// Direct model repository
var store = new XmlStore<Customer>();
store.SetSettings(new Settings { Location = "data", Name = "customers.xml" });

var repository = new XmlModelRepository<Customer>(store);

// ViewModel repository
var viewModelRepository = new XmlRepository<CustomerViewModel, Customer>(store);
```

## XML Serialization

The XML store uses .NET's built-in `System.Xml.Serialization.XmlSerializer`. This means:

- Your model classes need a parameterless constructor
- Properties you want serialized must be public
- You can use XML serialization attributes to control the format:
  - `[XmlElement]` - Control element names
  - `[XmlAttribute]` - Serialize as attribute instead of element
  - `[XmlIgnore]` - Skip serialization
  - `[XmlArray]` / `[XmlArrayItem]` - Control collection serialization

```csharp
public class Customer : Models.AbstractModel
{
    [XmlElement("CustomerName")]
    public string Name { get; set; }

    [XmlAttribute("IsActive")]
    public bool IsActive { get; set; }

    [XmlIgnore]
    public string TemporaryData { get; set; }
}
```

## API Reference

### Stores

- **XmlStore\<T\>** - Sync XML file store (all entities in one file)
- **XmlSeparateStore\<T\>** - Each entity in a separate file
- **XmlBatchStore\<T\>** - Entities grouped into batch files
- **XmlSeparateBulkStore\<T\>** - Separate files with bulk operations
- **XmlBatchBulkStore\<T\>** - Batched files with bulk operations
- **AsyncXmlStore\<T\>** - Async version of XmlStore
- **AsyncXmlSeparateStore\<T\>** - Async separate files
- **AsyncXmlBatchStore\<T\>** - Async batched files
- **AsyncXmlSeparateBulkStore\<T\>** - Async separate files with bulk
- **AsyncXmlBatchBulkStore\<T\>** - Async batched files with bulk

### Repositories

- **XmlModelRepository\<T\>** - Direct model repository
- **XmlRepository\<TViewModel, TModel\>** - ViewModel repository
- **AsyncXmlModelRepository\<T\>** - Async model repository
- **AsyncXmlRepository\<TViewModel, TModel\>** - Async ViewModel repository

### Settings

- **Settings** - Base settings (Location, Name)
- **BatchSettings** - Batch settings with BatchSize property

## Comparison with JSON Store

The XML store is similar to the JSON store but with these differences:

| Feature | JSON Store | XML Store |
|---------|------------|-----------|
| Serialization | System.Text.Json | System.Xml.Serialization |
| File Format | JSON (.json) | XML (.xml) |
| Human-Readable | Yes | Yes |
| Attributes Support | No | Yes (XmlAttribute) |
| Schema Validation | No | Yes (XSD) |
| Performance | Generally faster | Slightly slower |
| Type Safety | Good | Very Good |

## When to Use XML Store

- **Configuration files** - XML is great for hierarchical configuration
- **Data interchange** - When exchanging data with systems that require XML
- **Legacy integration** - Working with existing XML-based systems
- **Validation needs** - When you need XML schema (XSD) validation
- **Human editing** - When non-technical users need to edit data files

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.JSON](../Birko.Data.JSON/) - JSON file store
- [Birko.Helpers](../Birko.Helpers/) - PathValidator and utilities

## Filter-Based Bulk Operations

Supports filter-based update and delete via default read-modify-save pattern inherited from AbstractBulkStore (file-based storage has no native filter-based bulk operations).

## License

Part of the Birko Framework.
