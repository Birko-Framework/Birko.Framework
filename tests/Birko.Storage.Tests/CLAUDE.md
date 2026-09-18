# Birko.Storage.Tests

## Overview
Unit tests for Birko.Storage — file/blob storage abstraction.

## Location
`C:\Source\Birko.Storage.Tests\`

## Structure
```
Birko.Storage.Tests/
├── Core/
│   ├── StorageResultTests.cs          — StorageResult<T> Success/NotFound
│   ├── FileReferenceTests.cs          — Default values, property assignment
│   └── StorageOptionsTests.cs         — Default options, property assignment
├── Local/
│   └── LocalFileStorageTests.cs       — Upload, download, delete, exists, list, copy, move, path validation, metadata, prefix
└── Extensions/
    └── FileStorageExtensionsTests.cs  — UploadBytes, DownloadBytes, DownloadToFile
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores (shared projects)
- Birko.Storage (shared project)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Running Tests
```bash
dotnet test Birko.Storage.Tests/Birko.Storage.Tests.csproj
```

## Maintenance
- Add tests for every new public method or feature
- LocalFileStorageTests use temp directories, cleaned up in Dispose
