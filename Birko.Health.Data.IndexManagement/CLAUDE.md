# Birko.Health.Data.IndexManagement

A single `IHealthCheck` that answers *"are the indexes a host declares present, and shaped as declared?"* on a store
reached through `IIndexManager` (MongoDB, RavenDB, Cosmos DB).

## Project Location

`Birko.Health.Data.IndexManagement/`

## Overview

SQL derives its declared indexes from the model and checks them in `SchemaDriftHealthCheck` (TASK-622). The document
stores have no model-level index declaration: an index exists because a host or a migration created it from an
`IndexDefinition`. `IndexDeclarationDriftDetector` (Birko.Data.Patterns, TASK-635) compares that list with the index
manager's `GetInfoAsync`; this project is what reads it at startup.

## Components

| Type | Responsibility |
|---|---|
| `IndexDeclarationDriftHealthCheck` | Runs one `IndexDeclarationReport` probe per scope and reports one `HealthCheckResult`. |

## Conventions specific to this project

- **`Degraded`, never `Unhealthy`, for drift**, as the SQL and Elasticsearch drift checks.
- **"Could not compare" is never Healthy.** An index whose stored fields the provider cannot report (a RavenDB map it did
  not generate, a Cosmos path entry no name was registered for) or whose lookup failed is Degraded, with the reason.
- **Probes are supplied, never discovered.**

## Dependencies

- `Birko.Health` (`IHealthCheck`, `HealthCheckResult`)
- `Birko.Data.Patterns` (`IIndexManager`, `IndexDeclarationDriftDetector`, `IndexDeclarationReport`)

Not part of `Birko.Health.Data`, which is dependency-free by construction.

## Maintenance

See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
