# Birko.AI.Resilience

## Overview
Production resilience for LLM API calls — rate limiting, circuit breaker, cost tracking.

## Project Location
`Birko.AI.Resilience/`

## Namespace
`Birko.AI.Resilience.Services`, `Birko.AI.Resilience.Configuration`, `Birko.AI.Resilience.Stores`

## Components

### Services/ProviderRateLimiter.cs
- `ProviderRateLimiter` — Token bucket rate limiting per provider

### Services/ProviderCircuitBreaker.cs
- `ProviderCircuitBreaker` — Circuit breaker pattern for provider failures

### Services/CostTrackingService.cs
- `CostTrackingService` — Token usage and cost tracking per provider

### Services/TrackedLlmProvider.cs
- `TrackedLlmProvider` — Decorator wrapping `ILlmProvider` with rate limiting, circuit breaker, and cost tracking

### Configuration/RateLimitConfiguration.cs
- `RateLimitConfiguration` — Rate limit settings (requests per minute, tokens per minute)

### Configuration/CostTrackingConfiguration.cs
- `CostTrackingConfiguration` — Cost tracking settings (price per token, budget limits)

### Stores/IUsageRepository.cs
- `IUsageRepository` — Interface for persisting usage records

### Stores/ICircuitBreakerStore.cs
- `ICircuitBreakerStore` — Interface for persisting circuit breaker state

## Dependencies
- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Microsoft.Extensions.Logging** — logging abstractions

## Consumers
- Consumer applications needing production-grade LLM resilience
