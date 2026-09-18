---
id: TASK-328
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P1
assignee: human
created: 2026-09-09
depends-on: []
blocks: []
related: [TASK-311, TASK-127]
findings: []
pr: null
github-issue: null
affects: [Birko.EventBus.Tenant, Birko.Data.Tenant]
---

# Decide: should the event↔tenant bridge REFUSE to widen when it cannot establish a tenant?

## Context — escalated from [[TASK-311]], deliberately not decided there

`SH-H053` was confirmed and closed with a **documentation** fix, because the defect is genuinely
undetectable at the point it does harm. This task owns the design question that fix declined to answer
unilaterally.

**The mechanism, established by measurement in TASK-311.** `AddEventTenantScope()` binds
`Tenant.Current`. Every `AddTenantContext*` overload registers `typeof(TenantContext)`, and that type
keeps its state in **instance** `AsyncLocal` fields — so the container's instance and `Tenant.Current`
share nothing. In that wiring `TenantEventEnricher` sees `HasTenant == false`, leaves
`EventContext.TenantGuid` **null**, and `TenantEventScopeAccessor` runs the handler inside
`WithAllTenantsAsync`: a tenant-scoped event dispatched across **every** tenant, with `Strict`
repositories following it there (the tenant wrappers deliberately fail open on `HasTenant == false`,
CR-L229).

⚠ **Why it could not simply be fixed.** A mis-wired bridge and a **genuine system event** are
byte-identical from the event — both arrive with `TenantGuid == null`. Narrowing the null branch would
break cross-tenant system events, which are that branch's documented purpose. `TASK-311` pinned both
outcomes, including `A_genuine_system_event_still_widens_and_must_keep_doing_so`, precisely so this
decision is taken deliberately rather than by someone "tidying" the widening.

## The question

Should the bridge be able to tell "no tenant, deliberately" from "no tenant, because I am reading the
wrong object" — and refuse in the second case?

Detecting it is possible but not free: the bridge would need an `IServiceProvider` /
`IServiceScopeFactory`, would create a scope per dispatch, resolve `ITenantContext` there, and compare
by reference with the instance it holds. That is DI plumbing pushed into two **singletons**, plus a
behaviour change on a path a live consumer uses.

## Options

1. **Leave as documented (status quo after TASK-311).** Zero risk, zero cost. The hazard is named on the
   API, on `TenantEventScopeAccessor`, in the spec, and pinned by tests. ⚠ But it is still a
   configuration that silently widens tenant isolation, and § Conventions is repeatedly unkind to
   "documented footgun" as a resolution — the framework's own history is defects that were documented
   and then met anyway.
2. **Detect and refuse.** The bridge compares its context against the one the dispatch scope resolves and
   throws (or skips the dispatch, recording it) on a mismatch. Turns a silent cross-tenant widening into
   a loud failure. ⚠ Costs a scope per dispatch, puts a provider into two singletons, and changes
   behaviour for the one consumer already on this path.
3. **Make the mismatch impossible to wire.** Have `AddEventTenantScope` register the bridge halves so
   they resolve the context the same way stores do, or refuse the registration when the app's
   `ITenantContext` is not process-wide. ⚠ Measured inert as a *registration-time* check — the sole
   consumer calls `AddEventTenantScope()` **before** `AddBirkoSecurity`, so no descriptor exists yet —
   so this would have to be a deferred/startup validation, which is heavier than it looks.
4. **Report without refusing.** Record the mismatch on a diagnostic channel in the framework's existing
   idiom (`IndexCreationFailures`, `SubscriberFailures`, `SchemaEscapes`) and leave dispatch alone. Cheap
   and non-breaking; ⚠ but the channels this framework already has were measured at **0** consumer
   subscribers (TASK-269, TASK-283), so on current evidence nobody would read it.

## Recommendation

**Option 4 then 2, in that order, and not option 1 alone.** A diagnostic is cheap, non-breaking and can
ship immediately; option 2 is the real fix but wants the lifetime question below settled first. Option 1
is where TASK-311 left things because it was the only choice available without a decision — not because
it is adequate.

⚠ **The prior question, which may make this moot:** with `AddTenantContextScoped` / `Transient` the
bridge cannot work **at all** — both halves are registered *and consumed* as singletons
(`OutboxProcessor` takes `IEventScopeAccessor` from the root provider; enrichers are `AddSingleton`), so
there is no per-request instance for them to hold. If that lifetime is changed, several of the options
above change shape. Settle the lifetime first.

## Acceptance criteria

- [ ] A decision recorded on the four options, with the reason, and the rejected ones left readable so
      nobody re-litigates them
- [ ] The lifetime question answered first, or explicitly deferred with its reason — it is upstream of
      options 2 and 3
- [ ] If anything ships, the **live consumer** is checked first: measured 2026-09-09, one consumer calls
      `AddEventTenantScope()` and it is correctly wired (`AddBirkoSecurity` → `Tenant.Current`), so a
      behaviour change must not break the configuration that is already right
- [ ] ⚠ Whatever is chosen, `A_genuine_system_event_still_widens_and_must_keep_doing_so` keeps passing,
      or its removal is the *explicit* subject of the decision. A genuine system event losing its
      cross-tenant dispatch would be a regression in the opposite direction
- [ ] Proven able to fail, if code ships

## Out of scope

- **`SH-H053` itself** — confirmed and closed by [[TASK-311]] with the documentation fix and the
  mechanism pinned. This task is only about whether to go further.
- **`SH-H049`** — fixed in TASK-311 (per-request injection).
- The all-tenants-scope semantics generally — [[TASK-127]] owns the `IsAllTenantsScope` / ambient-tenant
  decision.

## Human test plan

- [ ] N/A until the decision is taken; a code option would need its own plan.
