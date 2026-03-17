# Birko.Storage.AzureBlob.Tests

Unit tests for the Birko.Storage.AzureBlob Azure Blob Storage provider.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **Target:** .NET 10.0

## Test Categories

| Category | Description |
|----------|-------------|
| Settings | Property mapping, defaults, RemoteSettings aliasing |
| Storage | Constructor validation, argument checks, content-type/size enforcement |
| Presigned URLs | SAS URI generation, query parameters, signatures |
| Path Resolution | Traversal rejection, prefix handling, normalization |

## Running

```bash
dotnet test
```

## License

[MIT](../Birko.Storage.AzureBlob/License.md)
