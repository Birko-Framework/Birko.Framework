# Birko.Health.Azure

## Overview
Azure-specific health checks for the Birko.Health framework. Covers Azure Blob Storage and Azure Key Vault connectivity verification.

## Project Location
`C:\Source\Birko.Health.Azure\` — Shared project (.shproj + .projitems)

## Components

- **AzureBlobHealthCheck.cs** — `IHealthCheck` for Azure Blob Storage. Lists blobs (maxResults=1) as connectivity probe. Reports latency, degrades above 2000ms.
- **AzureKeyVaultHealthCheck.cs** — `IHealthCheck` for Azure Key Vault. Lists secrets as connectivity probe. Reports latency, degrades above 2000ms.

## Pattern

Both checks follow the standard Birko.Health pattern:
- Dual constructors: factory function `Func<T>` or singleton instance
- Try/catch wrapping all logic
- Three-level status: Healthy (OK), Degraded (slow > 2s), Unhealthy (exception)
- Latency reported in `data["latencyMs"]`

## Dependencies

- **Birko.Health** — IHealthCheck, HealthCheckResult, HealthStatus
- **Birko.Storage.AzureBlob** — AzureBlobStorage (for blob health check)
- **Birko.Security.AzureKeyVault** — AzureKeyVaultSecretProvider (for key vault health check)

## Maintenance

When adding new Azure health checks (e.g., Azure Service Bus), add them here.
Update this CLAUDE.md, README.md, and root framework CLAUDE.md.
