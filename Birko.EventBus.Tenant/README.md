# Birko.EventBus.Tenant

Restores tenant scope for background event dispatch — the bridge between `Birko.EventBus` and
`Birko.Data.Tenant`.

## Features
- `TenantEventEnricher` (**publish side**) — implements `Birko.EventBus.Enrichment.IEventEnricher`,
  stamping `EventContext.TenantGuid` from the ambient `Tenant.Current` so the outbox entry / envelope is
  attributed to the right tenant in **every** flow (HTTP, background jobs, explicit `WithTenant` scopes)
  — not just authenticated HTTP requests. Drop your hand-rolled HttpContext tenant enricher.
- `TenantEventScopeAccessor` (**consume side**) — implements `Birko.EventBus.IEventScopeAccessor`,
  re-establishing that tenant before handlers run in a background flow (outbox processor, MQ consumer).
- Both map `EventContext.TenantGuid` ↔ `WithTenantAsync` (tenant set) / `WithAllTenantsAsync`
  (null/empty = system / cross-tenant event).
- `AddEventTenantScope()` DI extension — one call registers **both halves**, alongside adopting
  `TenantIsolationMode.Strict`.

## Usage
```csharp
services.AddOutboxEventBus();
services.AddInMemoryOutbox();
services.AddEventTenantScope();   // <-- tenant scope is now restored per outbox entry
```
Without this bridge the outbox processor uses the framework's no-op `IEventScopeAccessor`, so background
handlers run with no tenant — which throws under Strict. Registering it makes dispatch tenant-aware with
zero handler changes.

## Running tests
```
dotnet test ../../Framework.Tests/Birko.EventBus.Tenant.Tests/Birko.EventBus.Tenant.Tests.csproj
```

## License
MIT — see [License.md](License.md).
