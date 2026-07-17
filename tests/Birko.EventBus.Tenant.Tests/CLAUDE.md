# Birko.EventBus.Tenant.Tests

## Scope
Tests for `Birko.EventBus.Tenant` — the tenant scope bridge (STORY-046, EPIC-017).

## Conventions
- xUnit + FluentAssertions (per the framework test standard).
- Uses the concrete `Birko.Data.Tenant.Models.TenantContext` to verify the accessor actually enters
  `WithTenantAsync` / `WithAllTenantsAsync` and restores scope afterwards.

## Coverage
| Test | Verifies |
|------|----------|
| Restores_specific_tenant_from_context | `EventContext.TenantGuid` set → body runs in that tenant's scope; restored after |
| Null_tenant_runs_in_all_tenants_scope | null tenant → `WithAllTenants` (system event) |
| Empty_tenant_is_treated_as_system_event | `Guid.Empty` treated as system, not a real tenant |
| AddEventTenantScope_registers_the_bridge_as_IEventScopeAccessor | DI extension wires `IEventScopeAccessor` |

## Running
```
dotnet test Birko.EventBus.Tenant.Tests.csproj
```
