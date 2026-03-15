# Birko.Security.Vault

## Overview
HashiCorp Vault secret provider — uses Vault HTTP API directly, no VaultSharp dependency.

## Project Location
`C:\Source\Birko.Security.Vault\` — Shared project (.shproj + .projitems)

## Components
- **VaultSettings.cs** — Address, Token, MountPath, KvVersion (1 or 2), Namespace, TimeoutSeconds
- **VaultSecretProvider.cs** — Implements ISecretProvider. Get/Set/Delete/List secrets via Vault HTTP API. Supports KV v1 and v2. Includes IsHealthyAsync().

## Dependencies
- Birko.Security (ISecretProvider, SecretResult)
- System.Net.Http, System.Text.Json (BCL built-in)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
