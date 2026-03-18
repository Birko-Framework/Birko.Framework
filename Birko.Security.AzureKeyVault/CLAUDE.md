# Birko.Security.AzureKeyVault

## Overview
Azure Key Vault secret provider — uses Key Vault REST API with OAuth2 client credentials, no Azure SDK dependency.

## Project Location
`C:\Source\Birko.Security.AzureKeyVault\` — Shared project (.shproj + .projitems)

## Components
- **AzureKeyVaultSettings.cs** — VaultUri, TenantId, ClientId, ClientSecret, TimeoutSeconds
- **AzureKeyVaultSecretProvider.cs** — Implements ISecretProvider. OAuth2 token acquisition/caching. Get/Set/Delete/List secrets via Key Vault REST API v7.4.

## Dependencies
- Birko.Security (ISecretProvider, SecretResult)
- Birko.Serialization — ISerializer for API payload serialization (optional, defaults to SystemJsonSerializer)
- System.Net.Http, System.Text.Json (BCL built-in)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
