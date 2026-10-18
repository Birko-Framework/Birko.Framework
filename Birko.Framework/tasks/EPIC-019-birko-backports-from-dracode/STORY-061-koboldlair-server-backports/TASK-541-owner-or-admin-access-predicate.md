---
id: TASK-541
parent: STORY-061
feature: null
status: done
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

**The 404 here is one row of the framework's visibility rule** (settled 2026-10-13 in [[STORY-062]],
decision 2b): if the caller may read the resource, a denied action is 403; if not, it is 404. Owned data
is private, so a non-owner gets 404, as this task already says. Keep the predicate's 404 consistent with
that rule; don't introduce a 403 here for a caller who cannot read the entity.

## Acceptance criteria

- [x] A predicate taking `ICurrentUser`, the owner, and an optional "sees everything" permission
- [x] Guid and string owners; a null caller or null owner never matches
- [x] `OwnedCrudResults` uses the same predicate — one producer of the rule
- [x] Tests: owner, non-owner (404), admin override, null caller, null owner

## Human test plan

N/A — covered by tests.

## Outcome (2026-10-18)

- **The rule.** `OwnerAccess.CanAccess(caller, owner, seesEverything)` in `Birko.Communication.AspNetCore` has Guid and
  string forms. A caller that sees everything passes; otherwise both identities must be present (non-null, non-empty)
  and equal, with strings compared ordinally. A denial is answered 404, not 403 (rule 65).
- **One producer.** `OwnedCrudResults` (`ReadOwned`, `CreateClash`, `RequireOwned`) now decides through it. Nothing
  changes for MapOwnedCrud callers: their caller id is a non-null Guid, and a null owner already got 404.
- **The `ICurrentUser` form (decided by the user).** It lives in `Birko.Security.AspNetCore` as
  `CurrentUserOwnerAccessExtensions`:
  - `user.CanAccess(Guid? | string? owner, viewAllPermission?)` and `user.SeesEverything(viewAllPermission?)`.
  - "Sees everything" means `*` or the named view-all permission. The string form compares `UserGuid` in "D" form, as
    DraCode's `ApiOwnership` did.
- **New project edge.** `Birko.Security.AspNetCore` imports `Birko.Communication.AspNetCore` (Dependency Flow and the
  project CLAUDE.md updated). This repo's importers were updated: Security.AspNetCore.Tests, Grants.Tests and the
  Sandbox.
- **Consumer aggregators that must add the import** (`$(BirkoSrc)\Birko.Communication.AspNetCore\…projitems`) before
  their next build against this commit: Symbio (`Symbio.Birko`), DraCode (`DraCode.KoboldLair.Server`),
  `FisData.Stock.Angular.Server` and `GrantsSmoke`. Reps imports Communication.AspNetCore without Security.AspNetCore,
  and is unaffected.
- **Tests.**
  - `OwnerAccessTests` (7): owner, non-owner, admin override, null caller, null owner, ordinal strings, and
    `OwnedCrudResults` deciding through the rule. The API is new; mutation instead: null matching null fails 1.
  - `CurrentUserOwnerAccessTests` (4). Mutation: without the wildcard, 1 fails.
- **Regression.** Communication.AspNetCore 18, Security.AspNetCore 140, Grants 184. Sandbox builds, and Reps.Birko
  builds.
- **Docs.** `security.md` (ICurrentUser), `communication.md`, both project CLAUDE.md files, and the catalog.
