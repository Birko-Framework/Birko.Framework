# Birko.EventBus.Tenant.Tests

## Scope
Tests for `Birko.EventBus.Tenant` — the tenant scope bridge (STORY-046, EPIC-017).

## Conventions
- xUnit + FluentAssertions (per the framework test standard).
- Uses the concrete `Birko.Data.Tenant.Models.TenantContext` to verify the accessor actually enters
  `WithTenantAsync` / `WithAllTenantsAsync` and restores scope afterwards.

## Coverage
Consume side (`TenantEventScopeAccessorTests`):
| Test | Verifies |
|------|----------|
| Restores_specific_tenant_from_context | `EventContext.TenantGuid` set → body runs in that tenant's scope; restored after |
| Null_tenant_runs_in_all_tenants_scope | null tenant → `WithAllTenants` (system event) |
| Empty_tenant_is_treated_as_system_event | `Guid.Empty` treated as system, not a real tenant |
| AddEventTenantScope_registers_both_bridge_halves | DI extension wires both `IEventScopeAccessor` + `IEventEnricher` |

Publish side (`TenantEventEnricherTests`):
| Test | Verifies |
|------|----------|
| Stamps_tenant_from_ambient_scope | ambient tenant → `EventContext.TenantGuid` |
| Leaves_null_when_no_ambient_tenant | no scope → null (system event) |
| Nested_WithTenant_inside_WithAllTenants_captures_the_specific_tenant | cross-tenant job per-entity attribution — inner tenant wins |
| Does_not_overwrite_an_existing_value_with_null | never clears an already-attributed event |

## Running
```
dotnet test Birko.EventBus.Tenant.Tests.csproj
```
