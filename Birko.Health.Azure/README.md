# Birko.Health.Azure

Azure-specific health checks for the Birko.Health framework.

## Health Checks

| Check | Service | Probe Method |
|-------|---------|-------------|
| `AzureBlobHealthCheck` | Azure Blob Storage | List blobs (maxResults=1) |
| `AzureKeyVaultHealthCheck` | Azure Key Vault | List secrets |

## Status Levels

- **Healthy** — Service responds within 2 seconds
- **Degraded** — Service responds but slower than 2 seconds
- **Unhealthy** — Service unreachable or throws an exception

## Usage

### Azure Blob Storage

```csharp
var settings = new AzureBlobSettings("https://myaccount.blob.core.windows.net",
    "my-container", "tenant-id", "client-id", "client-secret");
var storage = new AzureBlobStorage(settings);

var runner = new HealthCheckRunner();
runner.Register("azure-blob", new AzureBlobHealthCheck(storage), "azure", "storage");

var report = await runner.RunAsync();
```

### Azure Key Vault

```csharp
var settings = new AzureKeyVaultSettings("https://myvault.vault.azure.net",
    "tenant-id", "client-id", "client-secret");
var provider = new AzureKeyVaultSecretProvider(settings);

runner.Register("azure-keyvault", new AzureKeyVaultHealthCheck(provider), "azure", "secrets");
```

### Factory Pattern (DI-friendly)

```csharp
runner.Register("azure-blob", new AzureBlobHealthCheck(() => serviceProvider.GetRequiredService<AzureBlobStorage>()));
```

## Dependencies

- Birko.Health (IHealthCheck, HealthCheckResult)
- Birko.Storage.AzureBlob (AzureBlobStorage)
- Birko.Security.AzureKeyVault (AzureKeyVaultSecretProvider)

## License

[MIT](License.md)
