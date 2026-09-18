# Birko.Data.XML.ViewModel

XML ViewModel repositories for Birko Framework.

## Overview

Provides ViewModel pattern support for XML data stores, enabling separation of data models from presentation models.

## Features

- **ViewModel Pattern**: Separates data models from presentation models
- **XML Integration**: Works seamlessly with XML file-based stores
- **Bulk Operations**: Supports bulk CRUD operations
- **Async/Sync**: Both synchronous and asynchronous repository implementations
- **Validation**: Integrates with Birko.Validation framework
- **Wrappers**: Compatible with store wrappers (tenant, caching, etc.)

## Usage

```csharp
using Birko.Data.XML.Repositories;
using Birko.Data.XML.Stores;

// Define models
public class ProductViewModel : ILoadable<Product>
{
    public string Name { get; set; }
    public decimal Price { get; set; }

    public void LoadFromModel(Product model)
    {
        Name = model.Name;
        Price = model.Price;
    }
}

// Create repository
public class ProductRepository : XmlRepository<ProductViewModel, Product>
{
    public ProductRepository(XmlStore<Product> store) : base(store) { }
}

// Use repository
var store = new XmlStore<Product>(new XmlSettings { Location = "./data/products" });
var repository = new ProductRepository(store);

var products = await repository.ReadAllAsync();
```

## Dependencies

- Birko.Data.XML
- Birko.Data.ViewModel
- Birko.Data.Repositories
