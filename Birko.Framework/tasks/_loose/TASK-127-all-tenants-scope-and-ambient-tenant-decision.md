---
id: TASK-127
# parent deliberately null: TASK-059/TASK-106 declare a parent while living in _loose/ and the dashboard
# flags them for rendering in two places. Related to EPIC-017 in substance; see the body.
parent: null
feature: null
status: todo
priority: P2
assignee: human
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-114, TASK-115]
pr: null
github-issue: null
jira-key: null
---

# Decide what `WithAllTenants` means when a tenant is also in scope

**This is a decision, not an implementation.** Filed in `_loose` alongside [[TASK-059]] / [[TASK-106]]
because it settles the semantics of a public API across reads *and* writes — larger than the defect that
surfaced it ([[TASK-114]]), and it cannot be answered by reading the code, because the code currently
answers it two different ways.

Substantively it belongs to **EPIC-017** (tenant-isolation hardening), but `parent` is left null on purpose:
[[TASK-059]] and [[TASK-106]] declare a parent while living here, and the dashboard flags them for rendering
in two places. Move this under the epic directory if that flag is ever resolved the other way.

## Context

Surfaced while fixing [[TASK-114]] and deliberately left as found, because resolving it changes behaviour
rather than fixing a defect.

`Birko.Data.Tenant` has an explicit all-tenants (admin) scope. Inside it, with a tenant **also** set:

- **Reads widen.** `TenantFilter` computes
  `IsAllTenantsScope ? (Guid?)null : CurrentTenantGuid`, so an active all-tenants scope drops the tenant
  predicate even when `HasTenant` is true. This is deliberate and documented — otherwise
  `WithAllTenants(...)` would silently keep scoping reads to the ambient tenant.
- **Item-level writes do not widen.** `BelongsToCurrentTenant` only consults `IsAllTenantsScope` in its
  `!HasTenant` branch. With a tenant set it compares against `CurrentTenantGuid` regardless of the scope.

So inside `WithAllTenants` with an ambient tenant, you can **read** every tenant's rows and then be refused
when you write one back.

The `TenantFilter` XML comment asserts the opposite of what the code does:

> Only the read/count/filter-write path flows through here; the write-authorization guards
> (`BelongsToCurrentTenant` / `SetTenantGuidIfNeeded`) **already special-case all-tenants scope**.

They special-case it only when no tenant is set. `docs/specs/tenant-isolation.md` records the contradiction
as a scenario ("All-tenants scope does not widen item writes when a tenant is set") rather than picking a
side — per the specs rule that where code and its own documentation disagree, the *disagreement* is the
finding.

## The decision

Which is the intended contract?

1. **`WithAllTenants` widens reads only** (today's behaviour). Admin code can survey across tenants but must
   enter the target tenant to write. Safer; the read/write asymmetry is surprising and must then be
   documented deliberately, and the misleading comment fixed.
2. **`WithAllTenants` widens reads and writes** (what the comment claims). Consistent and matches
   "operate across tenants on purpose", but it turns an ambient-tenant + admin-scope combination into a
   cross-tenant write capability — and that combination arises in request-scoped code where the ambient
   tenant came from a header or claim.
3. **`WithAllTenants` with an ambient tenant is an error.** Forces the caller to be explicit. Cleanest
   semantics, most disruptive: it breaks any consumer relying on today's read-widening from inside a
   request scope.

Whichever is chosen, `PreserveStoredTenant` (added by [[TASK-114]]) follows it — it currently skips
re-home protection inside an all-tenants scope on the assumption that cross-tenant writes are intentional
there. Under option (1) that assumption is wrong and should be revisited.

## What to produce

- The chosen option, with reasoning, recorded here
- Whether it is a breaking change for consumers, and which
- Follow-up implementation task(s) if the answer is not (1)
- Either way: fix the `TenantFilter` comment (both wrappers) so it stops asserting something untrue, and
  re-run `/specs regen tenant-isolation` so the scenario records a decision rather than a contradiction

## Not to be picked up by `/fix-next`

Acceptance is "decide X", so it cannot run unattended — same class as [[TASK-059]], [[TASK-106]],
[[TASK-119]]. It should surface in the closing report of a `/fix-next` run, not be selected by one.
