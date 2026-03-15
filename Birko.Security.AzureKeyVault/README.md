# Birko.Security.AzureKeyVault

Azure Key Vault integration for the Birko framework. Implements `ISecretProvider` using the Key Vault REST API directly with OAuth2 client credentials — no Azure.Security.KeyVault.Secrets dependency required.

## Features

- **Full CRUD** — Get, Set, Delete, List secrets
- **Metadata support** — version, timestamps, expiration, tags
- **OAuth2 client credentials** — automatic token acquisition and caching
- **Thread-safe token refresh** — SemaphoreSlim-based locking
- **No Azure SDK dependency** — uses `System.Net.Http` and `System.Text.Json` only

## Usage

```csharp
using Birko.Security.AzureKeyVault;

var settings = new AzureKeyVaultSettings
{
    VaultUri = "https://myvault.vault.azure.net/",
    TenantId = "your-tenant-id",
    ClientId = "your-client-id",
    ClientSecret = "your-client-secret"
};

using var akv = new AzureKeyVaultSecretProvider(settings);

// Set a secret
await akv.SetSecretAsync("db-password", "s3cret!");

// Get a secret
var password = await akv.GetSecretAsync("db-password");

// Get with metadata
var result = await akv.GetSecretWithMetadataAsync("db-password");
Console.WriteLine($"Version: {result?.Version}, Expires: {result?.ExpiresAt}");

// List secrets
var keys = await akv.ListSecretsAsync();

// Delete
await akv.DeleteSecretAsync("db-password");
```

## Dependencies

- Birko.Security (ISecretProvider, SecretResult)
- No external NuGet packages

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
