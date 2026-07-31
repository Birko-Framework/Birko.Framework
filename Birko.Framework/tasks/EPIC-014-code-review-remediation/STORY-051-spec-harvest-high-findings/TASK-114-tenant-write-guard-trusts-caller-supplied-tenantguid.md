---
id: TASK-114
parent: STORY-051
feature: null
status: done
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: 10f5611 (Birko.Data.Tenant) / c8e3263 (Birko.Data.Tenant.Tests)
github-issue: null
jira-key: null
findings: [SH-H047]
picked-by: fix-next
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

- [x] An update from tenant *t* naming another tenant's row Guid is **refused** and does not mutate the
      target — with `item.TenantGuid` set to *t* (the attack shape), asserted
- [x] The same for delete, sync and async, single and bulk (and through `Save`)
- [x] A caller **cannot re-home** a row they own by submitting a different `TenantGuid`
- [x] `item.TenantGuid` is provably not consulted for authorization — asserted by a case where it disagrees
      with both the ambient tenant and the stored row
- [x] Legitimate same-tenant updates and deletes are unchanged
- [x] The all-tenants / permissive scopes still work as documented (they are intentional cross-tenant reach)
- [x] Regression tests in `Birko.Data.Tenant.Tests` with a two-tenant fixture, plus a SQLite-backed
      end-to-end case proving the guard holds against a real SQL store that keys the write on the primary
      field alone
- [x] `/specs regen` for `tenant-isolation`, spec diff reviewed

**Two criteria were corrected at step 3, before any code was written** (the originals were phrased for
approach (1), which turned out to be unreachable from this layer — see Outcome):

- "affects **zero rows**" → "is **refused**". Zero-rows-affected is what a tenant term in the write
  predicate buys; the wrapper cannot add one, and a clear refusal is the stronger outcome anyway.
- "proving the predicate reaches the SQL" → "proving the guard holds against a real SQL store that keys
  the write on the primary field alone". Same evidentiary value — it still exercises the stored-row read
  against a real translator rather than a compiled delegate — but describes what is actually built.

## Out of scope

- `SH-H036` (`ReadOne` extension querying the connector directly and bypassing the tenant wrapper) —
  unverified, `repository-contract`, but **the same species on the read side**; worth verifying next.
- `SH-H054` ([[TASK-115]]) and the `TenantSyncProvider` cluster ([[TASK-113]]).
- `SH-H019` (no base-class tenant assertion on any `Birko.Data.Tagging` path) — unverified, separate.

## Human test plan

N/A — covered by automated tests. A two-tenant fixture asserts the cross-tenant write is refused; nothing
here has a visual surface.

## Progress log

- step 2 — picked; ranked above TASK-113 because this is an authz bypass reachable from a model-bound request body, not a scoping omission behind a sync job, and it is self-contained with no open design question
- step 3 — verified: HELD exactly as written. `BelongsToCurrentTenant` (TenantStoreWrapper.cs:187) compares `item.TenantGuid`; the four item-level write paths (sync/async x single/bulk) pass it the caller-supplied item and forward to an inner store that keys on the primary field only. Narrowed scope: the FILTER-based bulk paths (`Update(filter,...)` / `Delete(filter)`) already compose `TenantFilter` and are NOT affected — only the item-level overloads are. Second defect in the same method confirmed: `Update` forwards the caller`s `TenantGuid` verbatim, so an owner can re-home their own row.
- step 4 — fix in Birko.Data.Tenant/Stores/{TenantStoreWrapper,TenantBulkStoreWrapper,AsyncTenantStoreWrapper,AsyncTenantBulkStoreWrapper}.cs; tests in Birko.Data.Tenant.Tests/{TenantWriteGuardStoredRowTests,TenantWriteGuardSqLiteEndToEndTests}.cs; suite 50/50 green
- step 5 — reverted fix: 17/50 failed; fix-dependent = all 17 cross-tenant / re-home / payload-ignored / one-read cases (13 InMemory + 4 SQLite); contract pins = Same_tenant_update_and_delete_still_work, An_update_of_a_row_that_does_not_exist_is_still_refused_when_the_payload_claims_another_tenant, WithAllTenants_still_reaches_across_tenants, Permissive_with_no_tenant_in_scope_still_writes_across_tenants. The batch-atomicity test initially passed under revert — it was throwing on the wrong item (the legitimate item carried a foreign payload tenant, which the OLD guard rejected); corrected to carry a genuinely-owned TenantGuid with a stale TenantName as the stamping probe, and it is now fix-dependent.
- step 6 — respecced `tenant-isolation`; requirements changed: "Item-level write authorization" (rewritten around the stored-row read; 7 scenarios added, the "guard reads the caller-supplied tenant" scenario inverted, "the inner store is never called" corrected since the read-back does reach it) and "Bulk collection writes are authorized all-or-nothing over a single materialization" (now describes the single `ModelsByGuid` resolve + authorize-before-stamp ordering)
- step 7 — committed 10f5611 (Birko.Data.Tenant) / c8e3263 (Birko.Data.Tenant.Tests) / aggregator below

## Outcome

**What was wrong.** `BelongsToCurrentTenant` compared `item.TenantGuid` — a public settable `ITenant`
property, routinely model-bound straight from a request body — against the ambient tenant, and the inner
stores key item-level writes on the primary field alone. A caller in tenant *t* submitting
`{ Guid = <a row belonging to tenant u>, TenantGuid = t }` passed the guard and overwrote or deleted that
row, on all four item-level paths. The guard checked the attacker's claim about the row rather than the row.

**The fix.** The four wrappers resolve the targeted row first (`ReadStoredItems` / `ReadStoredItemsAsync`)
and authorize against it. `BelongsToCurrentTenant` keeps its meaning and stays the consumer override seam;
only its subject changed. `PreserveStoredTenant` closes the re-home defect found in the same method.

**Step-5 split.** Reverting only the production change: **17 of 50 failed**.

- Fix-dependent (17): `Update_refuses_another_tenants_row_even_when_the_payload_claims_our_tenant`,
  `Delete_refuses_…`, `UpdateAsync_refuses_…`, `DeleteAsync_refuses_…`,
  `Bulk_Update_and_Delete_refuse_another_tenants_row`, `AsyncBulk_Update_and_Delete_refuse_another_tenants_row`,
  `Save_routes_an_existing_Guid_through_the_stored_row_guard`,
  `A_payload_TenantGuid_disagreeing_with_both_the_ambient_tenant_and_the_stored_row_is_ignored`,
  `Update_cannot_re_home_a_row_we_own_by_submitting_a_different_TenantGuid`, `UpdateAsync_cannot_re_home_a_row_we_own`,
  `Bulk_Update_cannot_re_home_a_row_we_own`, `A_refused_item_anywhere_in_a_batch_leaves_the_whole_batch_unwritten_and_unstamped`,
  `The_bulk_wrapper_resolves_a_whole_batch_in_one_read`, plus all four SQLite end-to-end cases.
- **Contract pins, not evidence** (4 — they pass with the fix removed and only assert that behaviour did
  not change): `Same_tenant_update_and_delete_still_work`,
  `An_update_of_a_row_that_does_not_exist_is_still_refused_when_the_payload_claims_another_tenant`,
  `WithAllTenants_still_reaches_across_tenants`, `Permissive_with_no_tenant_in_scope_still_writes_across_tenants`.
- All 29 pre-existing tests pass in both states. The 8 other suites that consume `Birko.Data.Tenant`
  (475 tests) are green.

**Judgement calls, and the stricter option rejected each time.**

1. **Read-then-guard, not a tenant term in the write predicate.** The ticket preferred approach (1).
   It is not reachable from this layer: `IStore<T>.Update(T)` takes no predicate, so honouring it would
   mean changing `UpdateCore`/`DeleteCore` in every backend, and stores that key on identity alone
   (InMemory) could not honour it at all. The wrapper is the only layer that knows about tenancy.
   **Cost accepted:** one read per item-level write (bulk resolves a whole batch in one `ModelsByGuid`
   read), and **a TOCTOU window remains** between the read and the write — narrow, and it requires racing
   a legitimate re-home, but it is real and is the price of (2).
2. **The stored-row read is deliberately unscoped by tenant.** The stricter-looking option — reading
   through `TenantFilter` — is the wrong one and would have silently reinstated the bug: a foreign row
   comes back `null`, indistinguishable from a missing row, and a missing row authorizes the write.
3. **The payload check was kept for the no-stored-row case.** Removing it entirely was my first
   implementation and it broke 8 pre-existing tests. They were right: with no row to authorize against
   the check is not authorization, but it still stops an upserting inner store from being made to create
   a row homed in another tenant, and it preserves a documented refusal. Stored-row authorization is
   *additional*, never a replacement.
4. **Re-homing stays permitted with no tenant in scope and inside `WithAllTenants`.** Those are the
   documented deliberate-cross-tenant scopes, and `SetTenantGuidIfNeeded` already trusts the caller's
   per-item `TenantGuid` there on create. Blocking it would have changed admin behaviour beyond the defect.

**Flagged, not fixed.**

- **TOCTOU** between the stored-row read and the write (see 1). Not closable at this layer; a backend that
  wants atomicity must add the tenant term in its own `UpdateCore`/`DeleteCore`.
- **`Update(null!)` now passes the null through to the inner store** instead of throwing
  `NullReferenceException` inside the guard. Both are API misuse of a non-nullable parameter and both end
  in a failure from the inner store; not worth a behavioural guard, but noted.
- **`SH-H036`** (`ReadOne` extension querying the connector directly, bypassing the tenant wrapper) — the
  same species on the **read** side, still unverified, still in `repository-contract`. Worth verifying next.
- **`SH-H019`** (no base-class tenant assertion on any `Birko.Data.Tagging` path) — unverified, separate.
- **NU1903** (`SQLitePCLRaw.lib.e_sqlite3` 2.1.10 advisory) now surfaces in `Birko.Data.Tenant.Tests`
  because it gained the SQLite chain. Pre-existing repo-wide — `Birko.Data.SQL.SqLite.Tests` reports the
  identical advisory untouched. Not introduced here, not addressed here.

**Note for whoever fixes the neighbouring findings:** the `TenantFilter` XML comment still claims the write
guards "already special-case all-tenants scope". That was only ever true in the no-tenant branch, and the
spec records the contradiction as a scenario rather than silently picking a side. Left as found — changing
it is a behaviour decision about `WithAllTenants` + ambient tenant, not part of this defect.
