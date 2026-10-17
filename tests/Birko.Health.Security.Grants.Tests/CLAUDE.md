# Birko.Health.Security.Grants.Tests

## Overview
Offline tests of `StaleGrantHealthCheck` (TASK-654) over an in-memory `IResourceGrantStore`: Healthy with none (the
admin grant ignored), Degraded naming the stale grants by reason, Degraded when the store throws, the listing capped
while the count is not, and null arguments refused.

## Dependencies
xUnit, FluentAssertions; imports Birko.Health, Birko.Health.Security.Grants, Birko.Security.Grants, Birko.Data.InMemory
and their dependencies.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
