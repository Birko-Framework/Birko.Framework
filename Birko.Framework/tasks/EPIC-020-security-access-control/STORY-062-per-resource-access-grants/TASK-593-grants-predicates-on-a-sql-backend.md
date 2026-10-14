---
id: TASK-593
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: [TASK-588, TASK-594]
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

**Depends on [[TASK-594]]** (found when this task was picked, 2026-10-14): grants have no SQL mapping, so they
cannot be stored on SQL at all yet. Run every check below on TASK-594's shipped `Birko.Security.Grants.SQL`
mapping, the way a consumer would, not on a test-local one.

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
- [ ] The invitation **claim** is single-use across two `InvitationService` instances (separate `KeyedLock`s) on
      SQLite: two users with one verified email, redeeming at once, yield one grant. TASK-589/590 document that this
      rests on the SQL bulk store running a filtered `PropertyUpdate` as one `UPDATE … WHERE`. The in-memory test
      double only models that, so this is the measurement (added 2026-10-14).
- [ ] The SQL `PropertyUpdate` path binds values raw, skipping `AbstractField.ToStorage`: `Birko.Data.SQL/Stores/PropertyUpdateSqlTranslator.cs:27`,
      found at TASK-590's close review. Check that `RedeemedAt` / `RevokedAt` / `Role` written through it read back
      identical to the `Action<T>` path, including a consumer mapping with `[UtcField]` on PostgreSQL semantics if the
      SQLite run cannot show it. A difference there is its own defect task against `Birko.Data.SQL`, not a grants fix.
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

_Drafted at pick, 2026-10-14._ The tests live in `tests/Birko.Security.Grants.SQL.Tests` (TASK-594's project): it
already carries the SQLite stack, and adding that stack to `Birko.Security.Grants.Tests` would put a native SQLite
dependency under the core suite.

1. **Parity.** Seed one dataset (users × tenants × exact / type-wide / admin grants) into an InMemory store and the SQL
   store. Run every `IResourceGrantStore` lookup over a grid of arguments (null and set tenant, null and set resource)
   and compare the returned Guid sets. Any mismatch is a translation defect, fixed in `GrantPredicates`.
2. **Tenant isolation** stated explicitly on SQL: no tenant in scope, another tenant.
3. **The claim across instances.** Two `InvitationService`s with separate `KeyedLock`s, two users, one verified
   email. A read-delay test double over the SQL invitation store forces both to read before either claims, and the
   SQL `UPDATE … WHERE` must give one winner. Prove it fails with the claim check removed.
4. **The `PropertyUpdate` path.** `RedeemedAt` / `RedeemedBy` / `RevokedAt` / `Role` written by the services read back
   identical (ticks and `Kind`) to an entity-written row. A `[UtcField]`-on-PostgreSQL difference cannot show on SQLite;
   record that.
