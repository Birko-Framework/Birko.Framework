# Birko.Health.Security.Grants

## Overview
A health check over `Birko.Security.Grants`: reports the stored resource grants whose role or resource type the
`GrantRoleTable` no longer declares (TASK-654). Since TASK-641 such a grant grants nothing instead of throwing, which is
safe but quiet: a role renamed in configuration silently removes access through the old name. This check makes it loud.

## Project Location
`C:\Source\Birko\Framework\Birko.Health.Security.Grants\`

## Components
- `StaleGrantHealthCheck` (`IHealthCheck`) over `StaleGrantDetector.FindAsync(store, table)`:
  - **Healthy** when no stored grant is stale (`Data["stale"] = 0`)
  - **Degraded** when some are: `Data["stale"]` (the full count), `roleNotDeclared`, `resourceTypeNotDeclared`, and
    `staleGrants` (descriptions, at most `MaxListed` = 50); the description names the undeclared roles
  - **Degraded** when the store cannot be read, with the exception; never Healthy, never Unhealthy (the contract of the
    schema and index drift checks: a stale grant needs a person, not a load balancer)
  - The reserved admin grant (`*` / `*`) is never reported. The check reports; it never migrates or revokes.

```csharp
// After AddBirkoGrants(...), which registers the IResourceGrantStore and the GrantRoleTable:
var check = new StaleGrantHealthCheck(
    sp.GetRequiredService<IResourceGrantStore>(),
    sp.GetRequiredService<GrantRoleTable>());
```

## Dependencies
- Birko.Health (`IHealthCheck`, `HealthCheckResult`)
- Birko.Security.Grants (`StaleGrantDetector`, `IResourceGrantStore`, `GrantRoleTable`) and what it imports
  (Birko.Data.Core, Birko.Data.Stores, Birko.Time.Abstractions)

Its own project for the reason the other `Birko.Health.*` checks are: `Birko.Health` stays dependency-free.

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.Health.Security.Grants.Tests`.
