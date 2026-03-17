# Birko.Health.Azure.Tests

## Overview
Unit tests for Birko.Health.Azure health checks. Tests constructor validation, error handling, and cancellation behavior without requiring live Azure services.

## Project Location
`C:\Source\Birko.Health.Azure.Tests\` — .csproj test project (net10.0, xUnit, FluentAssertions)

## Test Files

- **Azure/AzureBlobHealthCheckTests.cs** — Constructor null checks, factory/instance constructors, factory exception → Unhealthy, cancellation → Unhealthy
- **Azure/AzureKeyVaultHealthCheckTests.cs** — Constructor null checks, factory/instance constructors, factory exception → Unhealthy, cancellation → Unhealthy

## Dependencies

- Birko.Health.Azure, Birko.Health, Birko.Storage.AzureBlob, Birko.Security.AzureKeyVault (projitems)
- xunit 2.9.3, FluentAssertions 7.0.0

## Running Tests
```bash
dotnet test Birko.Health.Azure.Tests/
```
