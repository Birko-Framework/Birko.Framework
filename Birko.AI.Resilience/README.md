# Birko.AI.Resilience

Production resilience for LLM API calls — rate limiting, circuit breaker, cost tracking.

## Overview

Birko.AI.Resilience wraps `ILlmProvider` instances with production-grade resilience patterns. The `TrackedLlmProvider` decorator composes rate limiting, circuit breaker, and cost tracking into a single transparent wrapper.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `ProviderRateLimiter` | `Services` | Token bucket rate limiting per provider |
| `ProviderCircuitBreaker` | `Services` | Circuit breaker for provider failures |
| `CostTrackingService` | `Services` | Token usage and cost tracking |
| `TrackedLlmProvider` | `Services` | Decorator combining all resilience features |
| `RateLimitConfiguration` | `Configuration` | Requests/tokens per minute settings |
| `CostTrackingConfiguration` | `Configuration` | Price per token, budget limits |
| `IUsageRepository` | `Stores` | Persistence interface for usage records |
| `ICircuitBreakerStore` | `Stores` | Persistence interface for circuit breaker state |

## Dependencies

- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Microsoft.Extensions.Logging** — logging abstractions

## Usage

```xml
<Import Project="..\Birko.AI.Resilience\Birko.AI.Resilience.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Resilience.Services;

var tracked = new TrackedLlmProvider(provider, rateLimiter, costTracker, logger)
{
    // Optional: the name usage records, pricing and rate limits key on. Without it, provider.Name (the client's
    // display name, e.g. "vLLM (finstat)") is used, so two providers of one type would share one name.
    ProviderName = "finstat",
};
```

`ProviderName` (TASK-984) is the consumer's own name for the provider. When set, `UsageRecord.Provider`, the
`CostTrackingService` pricing lookup and every `ProviderRateLimiter` call use it, so configure pricing and
`ProviderLimits[].Provider` under that name. `Name` stays the client's display name. A blank name throws
`ArgumentException`.

## License

MIT License - see [License.md](License.md)
