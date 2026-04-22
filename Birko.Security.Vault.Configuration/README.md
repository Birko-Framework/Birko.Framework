# Birko.Security.Vault.Configuration

Microsoft.Extensions.Configuration integration for HashiCorp Vault and any `ISecretProvider`. Provides configuration sources and extension methods that pull secrets into the .NET configuration system at startup.

## Features

- **Provider-agnostic** — `SecretConfigurationProvider` works with any `ISecretProvider` (Vault, Azure Key Vault, etc.)
- **Vault-specific** — `LocalVaultConfigurationProvider` adds recursive KV path loading with `--` separator rewriting
- **Hierarchical path convention** — `AddLocalVaultConfiguration` builds an ordered path chain: defaults, env-specific, project, user overrides
- **Environment variable support** — reads `LOCAL_VAULT_TOKEN`, `LOCAL_VAULT_ADDR`, `LOCAL_VAULT_USER`, `LOCAL_VAULT_DOMAIN`, `LOCAL_VAULT_ENVIRONMENT`
- **Opt-in activation** — entire feature is a no-op unless `LOCAL_VAULT_ENABLED=true`

## Project Location
`C:\Source\Birko.Security.Vault.Configuration\` — shared project (.shproj)

## Components

- **LocalVaultOptions.cs** — Options model for Vault connection and hierarchical path resolution (token, URL, user, domain, environment, mount path, KV version, timeout).
- **LocalVaultConfigurationProvider.cs** — `ConfigurationProvider` that recursively loads KV secrets from Vault and rewrites `--` separators to `ConfigurationPath.KeyDelimiter` for nested config binding.
- **LocalVaultConfigurationSource.cs** — `IConfigurationSource` that builds a `LocalVaultConfigurationProvider` for a given Vault KV path.
- **LocalVaultConfigurationExtensions.cs** — Extension methods: `AddLocalVaultConfiguration` (hierarchical path convention) and `AddVaultPath` (single-path overload for Vault or any `ISecretProvider`).
- **SecretConfigurationProvider.cs** — Generic `ConfigurationProvider` backed by any `ISecretProvider`, with optional recursive sub-path traversal.
- **SecretConfigurationSource.cs** — `IConfigurationSource` that builds a `SecretConfigurationProvider`.
- **SecretConfigurationExtensions.cs** — Extension methods: `AddSecretConfiguration` for single or multiple paths using any `ISecretProvider`.

## Dependencies

- Birko.Security.Vault (VaultSecretProvider, VaultSettings)
- Birko.Security (ISecretProvider)
- Microsoft.Extensions.Configuration (IConfigurationBuilder, ConfigurationProvider, IConfigurationSource)
- No external NuGet packages

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
