---
id: STORY-051
parent: EPIC-014
status: todo
created: 2026-07-30
source: FINDINGS.md
severity: mixed
finding-count: 749
finding-ids: SH-H001 … SH-H057, SH-M001 … SH-M365, SH-L001 … SH-L327
---

# Spec-harvest findings

## Progress

**0 / 749 closed.** 15 of the 57 high findings are hand-verified (13 CONFIRMED, 2 of those with corrected
scope, plus 1 CONFIRMED-NARROWER on a wrong trigger); the rest are unverified harvester claims. Per-finding
detail — including the verdicts — is in [`FINDINGS.md`](FINDINGS.md).

## User story

As a maintainer, I want the defects surfaced by the `/specs` capability harvest triaged and fixed, so that
behaviour the specs now record as wrong stops being the behaviour the framework ships.

## Where this came from

Generating `docs/specs/` (commits `3728969`, `acbbe9d`, `d40aba2`) meant reading 648 files across 19
cross-cutting areas at code HEAD `f3ac675`. The specs record behaviour **as-is**, defects included; this
story is the queue for changing it. That is a different provenance from STORY-024…027, which came from the
2026-06-17 review audit — so these carry an `SH-` prefix and do not renumber `CR-*`.

## Scope

`FINDINGS.md` holds all 749 with file:line and reasoning, grouped by severity then area. Severity is by
blast radius: **high** = silent data loss, cross-tenant leakage, auth bypass, a destructive op on the wrong
rows, or a predicate degrading to match-all on a write path.

## Verified high findings

These 15 were read and traced by hand rather than taken on trust. Highest-consequence first:

| ID | Verdict | What |
|---|---|---|
| SH-H*(Pbkdf2)* | CONFIRMED | `Pbkdf2PasswordHasher.Verify` returns `true` for **any** password against `PBKDF2-SHA512:600000::`. Empty string is valid Base64, so the CR-M233 guard never fires and `FixedTimeEquals` compares two zero-length spans. |
| SH-H*(bulk delete)* | CONFIRMED | `Delete(filter)` forwards an unchecked filter; zero conditions renders `DELETE FROM "T"` — a whole-table delete from a null or silently-dropped filter. |
| SH-H*(field types)* | CONFIRMED | `long`, `double`, `float`, `short`, `byte[]` properties map to no column and never persist, silently. `decimal` is mapped, so money is safe. |
| SH-H*(tenant write)* | CONFIRMED | `BelongsToCurrentTenant` compares the caller-supplied, settable `item.TenantGuid` and never reads the persisted row. |
| SH-H*(tenant sync)* | CONFIRMED | `ApplyTenantFiltering`'s own doc says only save filters are scoped; the delete arm therefore has no tenant check at all. (3 findings, one root cause.) |
| SH-H*(all-tenants)* | CONFIRMED | Reads test `IsAllTenantsScope` first and `WithTenant` never clears it, so a nested per-tenant loop reads every tenant. Writes test `HasTenant` first — writes narrow, reads do not. |
| SH-H*(order by)* | CONFIRMED | ORDER BY keys are interpolated verbatim, and `OrderBy<T>.ByName(string)` takes an arbitrary string — an injection sink wherever a sort column is user-supplied. |
| SH-H*(rule field)* | CONFIRMED | `new Condition(rule.Field, …)` puts an arbitrary rule field string in as the condition name, unresolved and unquoted. |
| SH-H*(In/NotIn/Like)* | CONFIRMED | `RuleSpecification` has no switch arm for them, so they become `Constant(true)` — a store filter matching every row. |
| SH-H*(Redis clear)* | CONFIRMED | `ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set, wiping the whole logical database. |
| SH-H*(IsNegated)* | **CONFIRMED-NARROWER** | Right mechanism, **wrong trigger**: the unresolved-field branch returns before the negation, so it is match-none. Only the non-string `BuildStringMethod` path becomes match-all. Fix under that description. |
| SH-H*(header guard)* | **CONFIRMED-NARROWER** | Query-string and subdomain sources really are unguarded, but "route" does not exist. Also unnoticed by the claim: `TenantHeaderName` is configurable, so a custom header escapes the guard too. |
| SH-H*(root provider)* | **CONFIRMED-NARROWER** | Real, but **not high**: the shipped `TenantContext` uses `AsyncLocal`, so the default registration is safe. Only a scoped `ITenantContext` holding per-request state in fields is affected. |

Exact IDs are in `FINDINGS.md`; the table names them by defect because IDs are assigned by generation order.

## Coverage gaps

Not to be mistaken for a complete audit:

- **3 areas are absent**: `serialization`, `validation-and-rules`, `llm-provider-and-agents` were only ever
  swept under an 8-per-area cap. Their true counts are unknown and their capped lists are excluded.
- **3 areas predate severity rating** and so appear in no severity section: `core-model-contracts` (4),
  `store-lazy-initialization` (6), `unit-of-work-and-transactions` (6).
- **42 of the 57 highs are unverified.** Of the 15 checked, 3 needed their scope corrected and 1 had the
  wrong trigger — roughly a quarter were imprecise. Verify before fixing.
- Missing test coverage was out of scope for the sweep and is not reported.

## Acceptance criteria

- [ ] The 42 unverified high findings are each confirmed, narrowed, or refuted against the code
- [ ] Every CONFIRMED high is fixed with a regression test, or explicitly waived with a reason
- [ ] The 3 absent areas are swept uncapped and their findings folded in
- [ ] Any finding whose fix changes behaviour triggers a `/specs regen` of its area, and the resulting spec
      diff is reviewed — the specs currently document the defective behaviour as shipped
