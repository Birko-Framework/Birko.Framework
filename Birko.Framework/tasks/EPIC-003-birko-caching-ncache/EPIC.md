---
id: EPIC-003
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Caching.NCache]
---

# Birko.Caching.NCache

## Area of concern

Add NCache as a distributed cache provider for `Birko.Caching` (alongside the existing Memory/Redis/Hybrid implementations).

## Success criteria

- `Birko.Caching.NCache` sibling project exists, registered in `.slnx` / `.code-workspace` / aggregator
- `NCacheCache` implements `ICache` over `Alachisoft.NCache.Client`
- xUnit + FluentAssertions tests passing
