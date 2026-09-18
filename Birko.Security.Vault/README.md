# Birko.Security.Vault

HashiCorp Vault integration for the Birko framework. Implements `ISecretProvider` using the Vault HTTP API directly — no VaultSharp dependency required.

## Features

- **KV v1 and v2** secrets engines
- **CRUD operations** — Get, Set, Delete, List secrets
- **Metadata support** — version, timestamps, custom metadata (KV v2)
- **Vault Enterprise** — optional namespace support
- **Health check** — `IsHealthyAsync()` endpoint
- **No external dependencies** — uses `System.Net.Http` and `System.Text.Json` only

## Usage

```csharp
using Birko.Security.Vault;

var settings = new VaultSettings
{
    Address = "http://127.0.0.1:8200",
    Token = "hvs.your-vault-token",
    MountPath = "secret",
    KvVersion = 2
};

using var vault = new VaultSecretProvider(settings);

// Set a secret
await vault.SetSecretAsync("myapp/db-password", "s3cret!");

// Get a secret
var password = await vault.GetSecretAsync("myapp/db-password");

// Get with metadata (KV v2)
var result = await vault.GetSecretWithMetadataAsync("myapp/db-password");
Console.WriteLine($"Version: {result?.Version}, Created: {result?.CreatedAt}");

// List secrets
var keys = await vault.ListSecretsAsync("myapp/");

// Delete
await vault.DeleteSecretAsync("myapp/db-password");

// Health check
var healthy = await vault.IsHealthyAsync();
```

## Dependencies

- Birko.Security (ISecretProvider, SecretResult)
- No external NuGet packages

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
