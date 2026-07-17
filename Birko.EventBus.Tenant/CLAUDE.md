# Birko.EventBus.Tenant

## Overview
Bridge between `Birko.EventBus` and `Birko.Data.Tenant`. Implements `IEventScopeAccessor` so background
event dispatch (outbox processor / message-queue consumer) re-establishes the tenant scope an event was
published under — required for tenant-scoped event handlers to work under `TenantIsolationMode.Strict`
(STORY-046, EPIC-017).

## Project Location
- **Directory:** `C:\Source\Birko\Framework\Birko.EventBus.Tenant\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.EventBus.Tenant`

## Components

| File | Description |
|------|-------------|
| TenantEventScopeAccessor.cs | `IEventScopeAccessor` impl: maps `EventContext.TenantGuid` → `ITenantContext.WithTenantAsync` (set) / `WithAllTenantsAsync` (null/empty = system event) |
| Extensions/EventTenantScopeServiceCollectionExtensions.cs | `AddEventTenantScope()` — registers the accessor over `Tenant.Current`; overload takes an explicit `ITenantContext` |

## Why this project exists (layering)
`Birko.EventBus` defines the transport-agnostic `IEventScopeAccessor` hook (no-op by default) and cannot
depend on `Birko.Data.Tenant`. `Birko.Data.Tenant` must stay unaware of the event bus. This tiny sibling
is the only place that depends on **both**, so neither core project takes a dependency on the other.

## Usage
```csharp
// alongside adopting TenantIsolationMode.Strict:
services.AddEventTenantScope();        // registers IEventScopeAccessor over Tenant.Current
// OutboxProcessor (via AddOutbox) resolves it automatically and restores scope per entry.
```
`EventContext.TenantGuid` set → handler runs in that tenant's scope; null → runs under `WithAllTenants`
(deliberate cross-tenant for system events). See `Birko.EventBus.IEventScopeAccessor` and
`OutboxProcessor` for the consuming side.

## Dependencies
- Birko.EventBus — `IEventScopeAccessor`, `EventContext`
- Birko.Data.Tenant — `ITenantContext`, `Tenant.Current`, `WithTenantAsync` / `WithAllTenantsAsync`
- Microsoft.Extensions.DependencyInjection.Abstractions — `IServiceCollection`

## Maintenance
See the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md) for the new-project
checklist, registration points, and test/health requirements.
