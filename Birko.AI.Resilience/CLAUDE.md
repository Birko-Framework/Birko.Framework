# Birko.AI.Resilience

## Overview
Production resilience for LLM API calls — rate limiting, circuit breaker, cost tracking.

## Project Location
`Birko.AI.Resilience/`

## Namespace
`Birko.AI.Resilience.Services`, `Birko.AI.Resilience.Configuration`, `Birko.AI.Resilience.Stores`

## Components

### Services/ProviderRateLimiter.cs
- `ProviderRateLimiter` — Token bucket rate limiting per provider. `RecordRequest(provider, tokens)` counts a request; `RecordTokens(provider, tokens)` adds tokens alone (a stream's, at its end; TASK-715)

### Services/ProviderCircuitBreaker.cs
- `ProviderCircuitBreaker` — Circuit breaker pattern for provider failures

### Services/CostTrackingService.cs
- `CostTrackingService` — Token usage and cost tracking per provider. `CheckBudgetAsync` answers the first exceeded budget (daily, monthly, project), and only then the first warning (TASK-715: a warning used to hide a later exceeded budget)

### Services/TrackedLlmProvider.cs
- `TrackedLlmProvider` — Decorator wrapping `ILlmProvider` with rate limiting, circuit breaker, and cost tracking. Every request made counts against the rate limit, with or without usage in the reply and when it throws (TASK-715)

### Configuration/RateLimitConfiguration.cs
- `RateLimitConfiguration` — Rate limit settings (requests per minute, tokens per minute)

### Configuration/CostTrackingConfiguration.cs
- `CostTrackingConfiguration` — Cost tracking settings (price per token, budget limits)

### Stores/IUsageRepository.cs
- `IUsageRepository` — Interface for persisting usage records. Every method takes a `CancellationToken`;
  `GetTotalSpendAsync(from, to, provider)` filters by provider. Windows are half-open.

### Stores/ICircuitBreakerStore.cs
- `ICircuitBreakerStore` — Interface for persisting circuit breaker state, one per provider (`CancellationToken` on both)

Both are implemented over any `IAsyncBulkStore<T>` by **Birko.AI.Resilience.Data** (TASK-530). `ProviderCircuitBreaker`
persists `Reset` / `ResetAll` too; before TASK-530 a reset cleared memory only, so a reload reopened the circuit.
`TrackedLlmProvider` passes the request's token to the budget check, and records usage with none: once the call has
been made, its spend is recorded even if the caller cancels.

## Dependencies
- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Microsoft.Extensions.Logging** — logging abstractions

## Consumers
- Consumer applications needing production-grade LLM resilience
