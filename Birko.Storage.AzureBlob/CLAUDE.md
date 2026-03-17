# Birko.Storage.AzureBlob

## Overview
Azure Blob Storage provider for the Birko.Storage abstraction. Implements `IFileStorage` and `IPresignedUrlStorage` using the Azure Blob Storage REST API directly with OAuth2 client credentials — no Azure.Storage.Blobs SDK dependency required.

## Project Location
`C:\Source\Birko.Storage.AzureBlob\` — Shared project (.shproj + .projitems)

## Components

- **AzureBlobSettings.cs** — Configuration extending `RemoteSettings`. Maps: `StorageAccountUri` → Location, `TenantId` → Name, `ClientId` → UserName, `ClientSecret` → Password. Adds `ContainerName`, `PathPrefix`, `TimeoutSeconds`, `DefaultOptions`.
- **AzureBlobStorage.cs** — Main implementation of `IFileStorage` + `IPresignedUrlStorage`. OAuth2 token acquisition with SemaphoreSlim double-check pattern. All operations use Azure Blob REST API (api-version 2023-11-03). Supports custom metadata via `x-ms-meta-*` headers. Path prefix for tenant isolation.
- **AzureBlobPresignedUrlProvider.cs** — Static helper for generating Service SAS tokens using HMAC-SHA256. Requires `AccountName` and `AccountKey` (set on AzureBlobStorage instance).

## Key Design Decisions

- **No SDK dependency** — Uses raw HTTP REST API like Birko.Security.AzureKeyVault, keeping the shared project dependency-free.
- **OAuth2 for CRUD, SAS for presigned URLs** — Normal operations use Bearer token auth. Presigned URLs require account key for HMAC signing (SAS tokens).
- **BlockBlob only** — All uploads use `x-ms-blob-type: BlockBlob` (sufficient for most scenarios).
- **XML response parsing** — List Blobs API returns XML; parsed with `System.Xml.Linq`.

## Dependencies

- **Birko.Storage** — IFileStorage, IPresignedUrlStorage, FileReference, StorageResult, StorageOptions, PresignedUrlOptions, StorageException hierarchy
- **Birko.Data.Stores** — RemoteSettings base class
- **System.Net.Http, System.Text.Json, System.Xml.Linq, System.Security.Cryptography** — BCL built-in

## Azure REST API Reference

- **API Version:** 2023-11-03
- **Auth scope:** `https://storage.azure.com/.default`
- **Put Blob:** `PUT /{container}/{blob}` with `x-ms-blob-type: BlockBlob`
- **Get Blob:** `GET /{container}/{blob}`
- **Delete Blob:** `DELETE /{container}/{blob}`
- **Get Properties:** `HEAD /{container}/{blob}`
- **List Blobs:** `GET /{container}?restype=container&comp=list&prefix={prefix}`
- **Copy Blob:** `PUT /{dest}` with `x-ms-copy-source` header

## Maintenance

When modifying this project, update:
- This CLAUDE.md
- README.md
- Root framework CLAUDE.md (Birko.Framework)
- Birko.Storage.AzureBlob.Tests (when tests are added)
