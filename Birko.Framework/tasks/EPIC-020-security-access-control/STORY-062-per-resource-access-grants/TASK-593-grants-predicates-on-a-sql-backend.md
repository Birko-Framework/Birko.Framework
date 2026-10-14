---
id: TASK-593
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: [TASK-588]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Grants: run the grant predicates against a SQL backend, not only InMemory

## Context

Spawned at TASK-588's close (2026-10-14) from its `## Out of scope`. This is [[STORY-062]]'s first listed risk.

`Birko.Security.Grants/Stores/GrantPredicates.cs` is the one producer of every grant query. It has a separate branch for
each null argument, so a null renders as an explicit `== null` and never as `col == @p`, which matches nothing in
SQL (rule 5). It also combines expressions with a parameter-replacing `AndAlso`, and its lambdas capture
pattern-match variables (`tenantGuid is not Guid tenant`, `(Guid r, Guid t)`). TASK-588's 67 tests run only on
`Birko.Data.InMemory`, which evaluates the expressions as LINQ to objects. So none of that has been checked against
a backend that **translates** the expressions.

What could differ on SQL:
- **Null terms:** `ResourceId == null` / `TenantGuid == null`.
- **Disjunctions:** `(TenantGuid == null || TenantGuid == t)`.
- **Captured variables:** the captured `Guid` locals.
- **The combinator:** the `And` combinator's rewritten parameter.

Any of these may translate differently, or be refused by the translator (rule 42: a translator refuses what it
cannot express).

## Acceptance criteria

- [ ] On the SQLite backend (`Birko.Data.SQL.SqLite`, file or in-memory database), every `IResourceGrantStore`
      lookup returns the same rows as on InMemory, for:
      - a tenant in scope and no tenant;
      - an exact and a type-wide grant;
      - the admin grant;
      - `CountRoleAsync`.
- [ ] A tenant-bound grant is not returned with no tenant in scope, and not in another tenant (rules 38/39), on SQLite.
- [ ] The tests run in the default `dotnet test` (SQLite needs no Docker). If they live outside
      `Birko.Security.Grants.Tests` because of the extra imports, say where and why.
- [ ] Any predicate a translator refuses or mistranslates is fixed in `GrantPredicates` (the one producer), and its
      test fails without the fix.

## Out of scope

- Other SQL providers (MSSQL / PostgreSQL / MySQL live suites). SQLite exercises the shared SQL expression
  translator; a provider-specific gap found later is its own task.
- MongoDB / ElasticSearch translation: not needed until a consumer stores grants there.

## Human test plan

N/A — fully covered by automated tests. The question is what rows a translated query returns, which a test asserts
exactly.

## Implementation plan

_Populated by `/tasks plan TASK-593` — leave empty until then._
