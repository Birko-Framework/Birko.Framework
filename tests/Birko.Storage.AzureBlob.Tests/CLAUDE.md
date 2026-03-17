# Birko.Storage.AzureBlob.Tests

## Overview
Unit tests for the Birko.Storage.AzureBlob project. Tests settings mapping, path validation, presigned URL generation, constructor validation, and content-type/size enforcement — all without requiring a live Azure account.

## Project Location
`C:\Source\Birko.Storage.AzureBlob.Tests\` — .csproj test project (net10.0, xUnit, FluentAssertions)

## Test Files

- **AzureBlob/AzureBlobSettingsTests.cs** — Settings defaults, property-to-RemoteSettings mapping, container name, default options
- **AzureBlob/AzureBlobStorageTests.cs** — Constructor validation, null argument checks, content-type/size enforcement, dispose behavior
- **AzureBlob/AzureBlobPresignedUrlProviderTests.cs** — SAS URI generation: correct path, query params, permissions, expiry, content headers, signature uniqueness
- **AzureBlob/PathResolutionTests.cs** — Path traversal rejection, null bytes, empty/whitespace paths, prefix handling, backslash normalization, leading slash stripping

## Dependencies

- Birko.Storage.AzureBlob (projitems)
- Birko.Storage (projitems)
- Birko.Data.Core, Birko.Data.Stores, Birko.Helpers (projitems)
- xunit 2.9.3, FluentAssertions 7.0.0, Microsoft.NET.Test.Sdk 18.0.1

## Running Tests
```bash
dotnet test Birko.Storage.AzureBlob.Tests/
```

## Maintenance
When adding new functionality to Birko.Storage.AzureBlob, add corresponding tests here.
