# Birko.Storage.Tests

Unit tests for the Birko.Storage file/blob storage abstraction.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **Target:** .NET 10.0

## Running Tests

```bash
dotnet test
```

## Test Coverage

- **Core types** — StorageResult, FileReference, StorageOptions
- **LocalFileStorage** — Upload, download, delete, exists, list, copy, move, path validation, metadata, tenant prefix
- **Extensions** — UploadBytes, DownloadBytes, DownloadToFile

## License

MIT License - see [License.md](License.md)
