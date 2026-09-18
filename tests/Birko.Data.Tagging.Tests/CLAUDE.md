# Birko.Data.Tagging.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Tagging` (CR-H107).

## Scope

- `InMemoryTagService` — test double implementing every `TagServiceBase` abstract hook over in-memory
  lists. Instrumentation seams: `CreateTagCalls` / `GetEntityTagLinksCalls` counters (CR-M172 N+1
  regression), `AfterFindTagByName` callback + `SeedTag` (deterministic injection of a
  concurrently-created tag between lookups — the CR-L226 TOCTOU race, no `Task.WhenAll` flakiness).
- `TagServiceBaseTests` — the shared template-method logic: create dedup (existing name reused),
  update null/whitespace handling, delete cascade (links removed first), idempotent attach,
  `SetEntityTagsAsync` reconciliation (+ exactly one link query, CR-M172), attach-by-name
  create/find paths + the CR-L226 concurrent-create race (raced tag reused, no duplicate insert),
  batch load grouping/backfill.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable). Imports the core
  projitems chain (`Birko.Contracts` … `Birko.Data.Core`) + `Birko.Data.Tagging`. Offline, no store — the abstract hooks ARE the
  persistence boundary. Tenant filtering inside hooks is each platform implementation's duty
  (CR-L228) and is not testable here.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
