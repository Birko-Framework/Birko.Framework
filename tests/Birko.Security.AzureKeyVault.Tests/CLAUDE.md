# Birko.Security.AzureKeyVault.Tests

## Overview
Unit tests for Birko.Security.AzureKeyVault — settings, OAuth2 token flow, API response parsing.

## Project Location
`C:\Source\Birko.Security.AzureKeyVault.Tests\`

## Components
- **AzureKeyVaultSettingsTests.cs** — Settings defaults, property aliases to RemoteSettings
- **AzureKeyVaultSecretProviderTests.cs** — Null checks, auth flow, secret parsing, list (uses SequentialHttpHandler)

## Dependencies
- Birko.Data.Core, Birko.Data.Stores, Birko.Security, Birko.Security.AzureKeyVault (.projitems)
