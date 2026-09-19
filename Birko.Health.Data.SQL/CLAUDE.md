# Birko.Health.Data.SQL

A single `IHealthCheck` that answers *"is my schema still what my models think it is?"*.

## Project Location

`Birko.Health.Data.SQL/`

## Overview

Birko never reconciles an existing table's columns — `CREATE TABLE IF NOT EXISTS` plus a schema-ensure
that only creates. So a model change, or an upgrade past one of the column-typing fixes (TASK-257,
TASK-264, TASK-265, TASK-266, TASK-275), silently leaves the old column in place. TASK-269 added the
detection; this project is what reads it.

## Components

| Type | Responsibility |
|---|---|
| `SchemaDriftHealthCheck` | Runs `AbstractConnector.DetectDrift` over a host-supplied list of entity types, folds in `AbstractConnector.IndexCreationFailures`, and reports one `HealthCheckResult`. |

## Conventions specific to this project

- **`Degraded`, never `Unhealthy`, for drift.** Drift is not unreachability. Reporting it `Unhealthy`
  would remove an instance from a load balancer for a condition only a human can fix, which turns a
  diagnostic into an outage. Connectivity is `Birko.Health.Data`'s `SqlHealthCheck`.
- **"Could not determine" is never reported as healthy.** An unsupported provider or an as-yet-uncreated
  table is called out explicitly — the whole defect class this exists to close is a silence that reads
  like good news (see § Conventions on TASK-204's unread channel).
- **The entity list is supplied, never scanned.** Assembly scanning would report every DTO and view
  model as a missing table.
- **The connector arrives as a factory.** Connectors are cached process-wide per (type, settings id);
  capturing one at registration time is the shared-state shape § Conventions records under TASK-270.

## Dependencies

- `Birko.Health` (`IHealthCheck`, `HealthCheckResult`)
- `Birko.Data.SQL` (`AbstractConnector`, `Birko.Data.SQL.SchemaDrift`)

**It is deliberately not part of `Birko.Health.Data`**, which is dependency-free by construction. The
reasoning, and the measurement distinguishing this case from TASK-234's Redis one, is written in
`Birko.Health.Data.SQL.projitems`.

## Maintenance

See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
