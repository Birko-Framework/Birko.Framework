# Birko.Security.Vault.Configuration

## Overview
Microsoft.Extensions.Configuration integration for secret providers. Contains two layers: a provider-agnostic layer (`Birko.Security.Configuration`) that works with any `ISecretProvider`, and a Vault-specific layer (`Birko.Security.Vault.Configuration`) with LocalVault hierarchical-path conventions. Recursively loads KV secrets into IConfiguration, rewriting `--` separators to `:` for clean POCO binding.

## Project Location
`C:\Source\Birko.Security.Vault.Configuration\` — Shared project (.shproj + .projitems)

## Components

### Birko.Security.Configuration (provider-agnostic)
- **SecretConfigurationProvider.cs** — ConfigurationProvider that reads secrets from any ISecretProvider. Supports recursive path traversal and `--` to `:` key rewriting.
- **SecretConfigurationSource.cs** — IConfigurationSource that builds a SecretConfigurationProvider for a given ISecretProvider and path.
- **SecretConfigurationExtensions.cs** — `AddSecretConfiguration()` extension methods on IConfigurationBuilder for single or multiple secret paths.

### Birko.Security.Vault.Configuration (Vault-specific)
- **LocalVaultOptions.cs** — Configuration POCO for LocalVault: Token, Url, User, Domain, Environment, MountPath, KvVersion, TimeoutSeconds. All optional with sensible defaults.
- **LocalVaultConfigurationProvider.cs** — ConfigurationProvider backed by VaultSecretProvider. Recursively loads KV paths into config keys.
- **LocalVaultConfigurationSource.cs** — IConfigurationSource that builds a LocalVaultConfigurationProvider for a given VaultSecretProvider and path.
- **LocalVaultConfigurationExtensions.cs** — `AddLocalVaultConfiguration()` (hierarchical defaults/env/project/user path scheme), `AddVaultPath()` overloads for VaultSecretProvider or any ISecretProvider. Environment variable fallback (LOCAL_VAULT_TOKEN, LOCAL_VAULT_ADDR, etc.).

## Dependencies
- Birko.Security (ISecretProvider)
- Birko.Security.Vault (VaultSecretProvider, VaultSettings — Vault-specific layer only)
- Microsoft.Extensions.Configuration.Abstractions (IConfigurationBuilder, IConfigurationSource, ConfigurationProvider)
- Microsoft.Extensions.Configuration (ConfigurationPath, ConfigurationBuilder)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
