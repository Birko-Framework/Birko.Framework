# Birko.Security.Vault.Tests

## Overview
Unit tests for Birko.Security.Vault — settings, HTTP API mocking, response parsing.

## Project Location
`C:\Source\Birko.Security.Vault.Tests\`

## Components
- **VaultSettingsTests.cs** — Settings defaults, property aliases to PasswordSettings, GetId
- **VaultSecretProviderTests.cs** — Null checks, KV v1/v2 parsing, list, health check (uses FakeHttpHandler)

## Dependencies
- Birko.Data.Core, Birko.Data.Stores, Birko.Security, Birko.Security.Vault (.projitems)
