---
id: TASK-984
parent: EPIC-019
feature: null
status: todo
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

- [ ] `TrackedLlmProvider` takes an optional configured name (`ProviderName`, an init property). When set, usage
      records, pricing and every rate-limiter call use it; when null, today's `Name`, so no existing consumer changes.
- [ ] An empty or whitespace `ProviderName` is refused (`ArgumentException`), not silently treated as null: a
      configured-but-blank name is a configuration error.
- [ ] `ILlmProvider.Name` stays the client's display name; log messages may keep using it.
- [ ] Tests (`Birko.AI.Resilience.Tests`): two decorators over clients with one display name and different
      `ProviderName`s record usage under their own names; a limit under the configured name holds that provider's
      calls and not the other's; with no `ProviderName`, behaviour is unchanged. Red before the change.
- [ ] Project README / CLAUDE.md mention the property, and that pricing must then be keyed by the configured name.

## Out of scope

- Renaming `ILlmProvider.Name` or the providers' display names.
- DraCode passing its configured names from its factories (DraCode TASK-193).

## Human test plan

- [ ] In DraCode, with two providers of one type (`finstat`, `lan2`): the cost report lists them separately, and a
      limit configured under `finstat` holds that provider's calls.
