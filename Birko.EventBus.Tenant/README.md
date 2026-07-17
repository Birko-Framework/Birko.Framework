# Birko.EventBus.Tenant

Restores tenant scope for background event dispatch — the bridge between `Birko.EventBus` and
`Birko.Data.Tenant`.

## Features
- `TenantEventScopeAccessor` — implements `Birko.EventBus.IEventScopeAccessor`, re-establishing the
  ambient tenant an event was published under before its handlers run in a background flow (outbox
  processor, message-queue consumer).
- Maps `EventContext.TenantGuid` → `WithTenantAsync` (tenant set) or `WithAllTenantsAsync` (null/empty,
  i.e. a system / cross-tenant event).
- `AddEventTenantScope()` DI extension — one call to opt in, alongside `TenantIsolationMode.Strict`.

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
