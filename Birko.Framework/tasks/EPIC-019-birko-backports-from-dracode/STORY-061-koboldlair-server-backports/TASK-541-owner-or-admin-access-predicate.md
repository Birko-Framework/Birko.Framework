---
id: TASK-541
parent: STORY-061
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Communication.AspNetCore: an owner-or-admin access check usable outside MapOwnedCrud

## Context

DraCode's `Api/ApiOwnership.cs` (30), used ~15 times in `ResourceEndpoints.cs` and in `RunsEndpoints`: a caller with a
wildcard / view-all permission sees everything; otherwise caller and owner must both be non-null and equal; otherwise
404, not 403. `OwnedCrudResults` already does 404-not-403, but only for an exact `Guid` owner match, with no admin
override and no string owners — and DraCode can't use `MapOwnedCrud` anyway (string ids, nested and async resources).

Adopted in the consumer by DraCode TASK-139 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] A predicate taking `ICurrentUser`, the owner, and an optional "sees everything" permission
- [ ] Guid and string owners; a null caller or null owner never matches
- [ ] `OwnedCrudResults` uses the same predicate — one producer of the rule
- [ ] Tests: owner, non-owner (404), admin override, null caller, null owner

## Human test plan

N/A — covered by tests.

## Implementation plan
