---
id: TASK-984
parent: EPIC-019
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `TrackedLlmProvider`: usage records and rate limits key on a configured provider name, not the client's display name

## Context

Asked for by DraCode (its TASK-193, 2026-11-25). `TrackedLlmProvider` (`Birko.AI.Resilience/Services`) uses `Name`,
which is `_inner.Name`, the client's display name (e.g. "vLLM (finstat)"), for `UsageRecord.Provider` (and so
`CostTrackingService`'s pricing lookup) and for every `ProviderRateLimiter` call (`CanMakeRequest`, `GetRetryAfter`,
`RecordRequest`, `RecordTokens`). DraCode configures providers by its own names: `finstat` and `lan2` are both vLLM
serving `finstat`, so:
- both record usage under one name, and cost reports cannot tell them apart;
- a limit configured as `ProviderLimits[].Provider = "finstat"` never matches "vLLM (finstat)", so it is never applied
  (seen in DraCode TASK-121's human test).

## Acceptance criteria

- [x] `TrackedLlmProvider` takes an optional configured name (`ProviderName`, an init property). When set, usage
      records, pricing and every rate-limiter call use it; when null, today's `Name`, so no existing consumer changes.
- [x] An empty or whitespace `ProviderName` is refused (`ArgumentException`), not silently treated as null: a
      configured-but-blank name is a configuration error.
- [x] `ILlmProvider.Name` stays the client's display name; log messages may keep using it.
- [x] Tests (`Birko.AI.Resilience.Tests`): two decorators over clients with one display name and different
      `ProviderName`s record usage under their own names; a limit under the configured name holds that provider's
      calls and not the other's; with no `ProviderName`, behaviour is unchanged. Red before the change.
- [x] Project README / CLAUDE.md mention the property, and that pricing must then be keyed by the configured name.

## Evidence

`ProviderName` is an init property on `TrackedLlmProvider`; a private `TrackingName` (`ProviderName ?? Name`) is what
`UsageRecord.Provider`, every `ProviderRateLimiter` call and the rate-limit messages use. A blank name throws
`ArgumentException`. `Name` is unchanged.
- `TrackedLlmProviderNameTests` (6): two decorators over one display name record and price usage under `finstat` (2 USD)
  and `lan2` (3 USD); a 1/min limit under `finstat` holds finstat's second call and not lan2's; with no `ProviderName`
  the display name is used for usage and limits as before; `Name` stays the display name; "" and "   " are refused.
  **Red:** without `ProviderName` the file does not compile; with the property but not wired in, the usage and limit
  tests fail (2 of 6).
- `Birko.AI.Resilience.Tests` 24/24 and `Birko.AI.Resilience.Data.Tests` 24/24 (the two importers of the projitems);
  no nullable warnings. README (the usage example also fixed: it passed a circuit breaker the constructor does not
  take), project CLAUDE.md and § Recent Updates updated.

## Out of scope

- Renaming `ILlmProvider.Name` or the providers' display names.
- DraCode passing its configured names from its factories (DraCode TASK-193).

## Human test plan

- [x] In DraCode, with two providers of one type (`finstat`, `lan2`): the cost report lists them separately, and a
      limit configured under `finstat` holds that provider's calls.
  - _Run 2026-11-25 by DraCode (Birko.Framework 16dc33f3, its TASK-193, a throwaway server):_ PASS. Two vLLM providers on
    one model, `finstat` ($1/M) and `lan2` ($10/M), a 2 RPM limit under `finstat` only. Usage records name `lan2` and
    `finstat`, each at its own price (lan2 6,344 tokens = $0.0634), listed as separate cost-report rows. The limit holds
    finstat ("Rate limit hit for finstat: 2/2 RPM", the third call waited ~58 s); three `lan2` calls ran in 0.7 s.
    Older records stay under "vLLM (finstat)" and still read. Found on the way: streamed calls record no usage at all
    (older, not caused by this): [[TASK-987]].

**Closed 2026-11-25** (already on Framework main and pushed, 16dc33f3).
