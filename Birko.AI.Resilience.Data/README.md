# Birko.AI.Resilience.Data

Persists LLM circuit breaker state and usage on any Birko store.

## Features
- `StoreCircuitBreakerStore`: an open circuit stays open across a restart; one row per provider
- `StoreUsageRepository`: spend totals, per provider and per project, computed by the store's aggregation
  (`GetTotalSpendAsync(from, to, provider)` filters by provider)
- Works on any `IAsyncBulkStore<T>` (SQL, InMemory, JSON, MongoDB, …); usage needs one that aggregates

## Usage

```csharp
var breaker = new ProviderCircuitBreaker(store: new StoreCircuitBreakerStore(breakerStore));
await breaker.LoadPersistedStateAsync(ct);

var costs = new CostTrackingService(config, logger, new StoreUsageRepository(usageStore));
```

## Running tests

```
dotnet test tests/Birko.AI.Resilience.Data.Tests
```

## License
See the repository's root `LICENSE`.
