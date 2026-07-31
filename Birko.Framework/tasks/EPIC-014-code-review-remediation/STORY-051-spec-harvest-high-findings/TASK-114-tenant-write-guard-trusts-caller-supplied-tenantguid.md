---
id: TASK-114
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H047]
---

# The item-level tenant write guard trusts the caller-supplied `TenantGuid`

## Context

`../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs:187` — **CONFIRMED**.

`BelongsToCurrentTenant` is `item.TenantGuid == _tenantContext.CurrentTenantGuid`. It compares the
**in-memory item's public settable `ITenant.TenantGuid`** — routinely model-bound from a request body — and
never reads the persisted row.

The inner store then keys the write on the primary field **only**: `DataBaseStore.UpdateCore` / `DeleteCore`
build conditions from `GetPrimaryFields` (the Guid), `AbstractInMemoryStore` keys on `data.Guid`. No tenant
term is added anywhere.

So a caller in tenant *t* submitting `{ Guid = <a row belonging to another tenant>, TenantGuid = t }` passes
the guard and **overwrites or deletes another tenant's row**. The guard checks the attacker's claim about the
row rather than the row.

Same at `AsyncTenantStoreWrapper.cs:189` and on the bulk paths.

Note the shape: this is the store-layer sibling of the `X-Tenant-Id` / JWT-claim mismatch fixed on
2026-07-28. That fix established the principle — *a tenant assertion the caller controls is not a tenant
check* — and this is the same principle unapplied one layer down.

## Approach

The guard has to be resolved against **stored** state, not supplied state. Two ways, and they are not
equivalent:

1. **Add the tenant term to the write predicate** so `UPDATE … WHERE Guid = @g AND TenantGuid = @t` /
   `DELETE … WHERE …` simply affect zero rows cross-tenant. Atomic, no extra round trip, and it works
   uniformly across the store hierarchy — but the caller sees "0 rows affected" rather than a clear denial.
2. **Read the row first and compare its persisted `TenantGuid`.** Clearer error, but a round trip and a
   TOCTOU window.

Prefer (1) as the enforcement and layer (2)'s diagnostic on top only where the API must distinguish
not-found from not-yours. **Whichever is chosen, the item's own `TenantGuid` must stop being an input to the
decision** — that is the acceptance test, not the mechanism.

Check whether the write path should also *overwrite* `item.TenantGuid` from the ambient context before
persisting, so a caller cannot re-home a row they legitimately own by editing the field.

## Acceptance criteria

- [ ] An update from tenant *t* naming another tenant's row Guid affects **zero rows** and does not mutate
      the target — with `item.TenantGuid` set to *t* (the attack shape), asserted
- [ ] The same for delete, sync and async, single and bulk
- [ ] A caller **cannot re-home** a row they own by submitting a different `TenantGuid`
- [ ] `item.TenantGuid` is provably not consulted for authorization — asserted by a case where it disagrees
      with both the ambient tenant and the stored row
- [ ] Legitimate same-tenant updates and deletes are unchanged
- [ ] The all-tenants / permissive scopes still work as documented (they are intentional cross-tenant reach)
- [ ] Regression tests in `Birko.Data.Tenant.Tests` with a two-tenant fixture, plus a SQLite-backed
      end-to-end case proving the predicate reaches the SQL
- [ ] `/specs regen` for `tenant-isolation`, spec diff reviewed

## Out of scope

- `SH-H036` (`ReadOne` extension querying the connector directly and bypassing the tenant wrapper) —
  unverified, `repository-contract`, but **the same species on the read side**; worth verifying next.
- `SH-H054` ([[TASK-115]]) and the `TenantSyncProvider` cluster ([[TASK-113]]).
- `SH-H019` (no base-class tenant assertion on any `Birko.Data.Tagging` path) — unverified, separate.

## Human test plan

N/A — covered by automated tests. A two-tenant fixture asserts the cross-tenant write is refused; nothing
here has a visual surface.
