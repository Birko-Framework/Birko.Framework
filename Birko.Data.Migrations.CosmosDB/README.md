# Birko.Data.Migrations.CosmosDB

Cosmos DB migration provider for the Birko Framework.

## Features

- Container management migrations (create/delete)
- Indexing policy migrations (included/excluded/composite paths)
- Document operations (load, store, delete, bulk insert)
- Migration state tracking in Cosmos DB

## Usage

```csharp
// Define a migration
[Migration(1, "Create Products container")]
public class CreateProductsContainer : CosmosMigration
{
    protected override void Up(Database database)
    {
        CreateContainer(database, "Products", "/categoryId");
        AddCompositeIndex(database, "Products",
            ("/name", CompositePathSortOrder.Ascending),
            ("/createdAt", CompositePathSortOrder.Descending));
    }

    protected override void Down(Database database)
    {
        DeleteContainer(database, "Products");
    }
}

// Run migrations
var client = new CosmosClient("AccountEndpoint=...");
var database = client.GetDatabase("MyDatabase");
var runner = new CosmosMigrationRunner(database);
runner.Register<CreateProductsContainer>();
runner.MigrateUp();
```

## Dependencies

- Birko.Data.Migrations
- Microsoft.Azure.Cosmos

## Related Projects

- [Birko.Data.Migrations](../Birko.Data.Migrations/) - Core migration framework

## License

Part of the Birko Framework.
