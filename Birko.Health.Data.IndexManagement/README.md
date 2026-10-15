# Birko.Health.Data.IndexManagement

Index declaration drift health check for stores reached through `IIndexManager` (MongoDB, RavenDB, Cosmos DB).

## Features

- Reports a declared index that is missing, over other fields, or of the other uniqueness (TASK-635)
- Degraded, never Unhealthy; an index it could not compare is never reported Healthy

## Usage

```csharp
var declared = new[]
{
    new IndexDefinition { Name = "UX_Number", Fields = new[] { IndexField.Ascending("Number") }, Unique = true },
};
var manager = new MongoDBIndexManager(database);

registry.Add("indexes", new IndexDeclarationDriftHealthCheck(new Func<CancellationToken, Task<IndexDeclarationReport>>[]
{
    ct => IndexDeclarationDriftDetector.DetectAsync(manager, declared, "Invoices", ct),
}));
```

## Running tests

```sh
dotnet test tests/Birko.Health.Data.IndexManagement.Tests --nologo
```

## License

See the repository root `LICENSE`.
