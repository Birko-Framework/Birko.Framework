---
id: TASK-708
parent: STORY-063
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-03]
pr: null
github-issue: null
jira-key: null
---

# Data.Tenant's middleware leaves the tenant set when a later step throws

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `TenantMiddleware` calls `ClearTenant()` after `await _next(context)` with no `try`/`finally`, so an exception downstream leaves the tenant set, which leaks it when the context is a singleton or `Tenant.Current`. The Security.AspNetCore middleware clears in a `finally`. Also: the Purpose prose of the tenant spec says a missing tenant is a 400, the middleware returns 401.

Evidence: Birko.Data.Tenant/Middleware/TenantMiddleware.cs:96,109-112.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-03, before the fix)

A downstream throw, then reading `HasTenant` in the catching caller and in a later flow (`Task.Run`); then a request
with no tenant after an outer flow set one.

| Context | After a throw: catch / later flow | Outer tenant after a no-tenant request |
|---|---|---|
| `TenantContext` (AsyncLocal; also behind `Tenant.Current`) | false / false | kept |
| A field-backed `ITenantContext` | **true / true** | **discarded** |

- **The leak claim** reproduces only on a context that does not keep its state in `AsyncLocal`: `TenantContext`'s
  value set inside the async `InvokeAsync` is undone when it returns. The interface does not require `AsyncLocal`,
  so it is fixed for every implementation. No consumer implements `ITenantContext` or calls `UseTenantMiddleware`
  (Symbio has its own middlewares), so nothing shipped was exposed.
- **Found here** (the spec's second scenario): a request with no tenant called `ClearTenant()` anyway.
- **The 400 claim does not reproduce as a contradiction.** The Purpose prose's `400` is a host answering
  `TenantScopeRequiredException` (a store refusing an unscoped request); the middleware's `RequireTenant` answer is
  `401`, and its own requirement says so. Nothing changed.

## Implementation plan

When the middleware set a tenant, `await _next` in a `try` and `ClearTenant()` in the `finally`; when it set none,
clear nothing. As `Birko.Security.AspNetCore.TenantMiddleware` already does.

## Built (2026-11-03)

`tests/Birko.Data.Tenant.Tests/Task708MiddlewareClearTests.cs`: 4 cases on both contexts (8). Without the fix the
three field-backed ones fail and the AsyncLocal ones pass, as measured. Spec `tenant-isolation.md` requirement
rewritten, and the Security.AspNetCore scenario no longer calls this one divergent (scoped; `generated-at` left).
