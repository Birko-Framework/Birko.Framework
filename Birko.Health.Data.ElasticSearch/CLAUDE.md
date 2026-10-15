# Birko.Health.Data.ElasticSearch

A single `IHealthCheck` that answers *"is each Elasticsearch index still mapped the way its store declares?"*.

## Project Location

`Birko.Health.Data.ElasticSearch/`

## Overview

An index is mapped once (`AutoMap`, or the descriptor passed to `Init`) and never revisited, so a model changed later
queries an index mapped for the old one, and Elasticsearch answers with empty results rather than errors (TASK-613).
TASK-621 added `DetectMappingDriftAsync()` to the stores; this project is what reads it (TASK-624).

## Components

| Type | Responsibility |
|---|---|
| `ElasticSearchMappingDriftHealthCheck` | Runs one `DetectMappingDriftAsync` probe per store and reports one `HealthCheckResult`. |

## Conventions specific to this project

- **`Degraded`, never `Unhealthy`, for drift.** Same reasoning as `Birko.Health.Data.SQL`: drift needs a human, and
  Unhealthy would pull a serving instance out of a load balancer.
- **"Could not determine" is never Healthy.** A store with no client is Degraded. An index not created yet is Healthy,
  with a description that says it was not checked.
- **Probes are supplied, never discovered.** One delegate per store, because the stores are generic per entity.

## Dependencies

- `Birko.Health` (`IHealthCheck`, `HealthCheckResult`)
- `Birko.Data.ElasticSearch` (`MappingDriftReport`, the stores' `DetectMappingDriftAsync`)

Not part of `Birko.Health.Data`, which is dependency-free by construction (see the `.projitems` comment).

## Maintenance

See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
