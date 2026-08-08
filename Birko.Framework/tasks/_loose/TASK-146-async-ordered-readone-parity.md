---
id: TASK-146
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Nothing pins that the async repository has no connector-bypassing read

## Context

Filed from [[TASK-125]]'s Outcome, listed there as unverified.

TASK-125 removed a `ReadOne(filter, orderBy)` extension that read through `repository.Connector` — thereby
skipping every store decorator including the tenant one — and replaced it with a decorator-safe instance
method on `AbstractViewModelRepository`.

`AbstractAsyncViewModelRepository` has `ReadOneAsync(IFilter?, ct)` and **no ordered twin**, so there is
nothing to bypass on the async side today. That is the current state, not a guarantee.

The sync defect existed because an ordered read was wanted while `Store` is `protected`, leaving
`Connector` as the only reachable handle. **The same pressure applies verbatim to the async repository**
the moment someone wants an ordered async single read — and `AsyncDataBaseRepository.Connector` resolves
through `GetUnwrappedStore` exactly as the sync one did (`AsyncDataBaseRepository.cs:34-40`).

No test asserts any of this. The absence is load-bearing and undefended: the next person to add the
overload will meet the same dead end and reach for the same wrong handle.

## Acceptance criteria

- [ ] A test asserts the async repository's single-read path is decorator-scoped, using the same
      two-tenant fixture shape as `ReadOneDecoratorBypassTests` — so the property is pinned rather than
      merely true by omission
- [ ] **Decide** whether to add `ReadOneAsync(IFilter?, OrderBy<TModel>?, ct)` now, mirroring the sync
      overload, or to leave the async surface without one; record the reasoning
- [ ] If added: it reads through `Store`, degrades to the unordered read when the store is not an
      `IAsyncBulkReadStore<T>` (as `AsyncTenantStoreWrapper` is not), and carries the same two-tenant
      assertions as its sync twin
- [ ] If not added: the reason is recorded where a future implementer will meet it — on
      `ReadOneAsync` itself — naming `Connector` explicitly as the wrong way to serve it

## Out of scope

- The sync defect and its fix ([[TASK-125]], closed).
- Documenting the escape hatch generally ([[TASK-145]]).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-146` — leave empty until then._
