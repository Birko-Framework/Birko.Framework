---
id: TASK-328
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
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

## Decision (2026-10-15, by the user)

- **Lifetime: global only, fail fast.** The bridge requires the process-wide tenant context. Registering a per-request
  or per-use (`AddTenantContextScoped` / `Transient`) context together with `AddEventTenantScope` fails **at startup**,
  with a message that says what to change. Deferred to startup because registration order makes a registration-time
  check inert (the consumer calls `AddEventTenantScope()` before `AddBirkoSecurity`).
- **Approach: startup check + diagnostic** (option 3 for the wiring, plus option 4). Any remaining mismatch at dispatch
  is recorded on a diagnostic channel in the framework's idiom. **Genuine system events keep running across all
  tenants**: `A_genuine_system_event_still_widens_and_must_keep_doing_so` must keep passing.
- *Rejected, left readable:* option 1 (status quo: a silent cross-tenant widening stays possible), option 2 (a check on
  every dispatch: per-event scope creation and DI plumbing in two singletons, for a case the startup check already
  closes), a scope-aware bridge supporting every lifetime (more code for no current consumer), and diagnostic only
  (measured: nobody reads the existing channels).
- The live consumer (Symbio: `AddBirkoSecurity` → `Tenant.Current`) is already correctly wired and must stay
  unaffected; verify that before shipping.

## Acceptance criteria

- [x] A decision recorded on the four options, with the reason, and the rejected ones left readable so
      nobody re-litigates them
- [x] The lifetime question answered first, or explicitly deferred with its reason — it is upstream of
      options 2 and 3
- [x] If anything ships, the **live consumer** is checked first: measured 2026-09-09, one consumer calls
      `AddEventTenantScope()` and it is correctly wired (`AddBirkoSecurity` → `Tenant.Current`), so a
      behaviour change must not break the configuration that is already right
- [x] ⚠ Whatever is chosen, `A_genuine_system_event_still_widens_and_must_keep_doing_so` keeps passing,
      or its removal is the *explicit* subject of the decision. A genuine system event losing its
      cross-tenant dispatch would be a regression in the opposite direction
- [x] Proven able to fail, if code ships

## Resolution (2026-10-15)

**The check is identity, not lifetime, and that is a correction to the decision's wording, found before shipping.**
The decision said "a per-request or per-use context fails at startup". But `AddBirkoSecurity`, the live consumer's
wiring, registers `ITenantContext` **scoped**, as `_ => Tenant.Current`. A lifetime rule would have refused exactly the
configuration the acceptance criteria say must keep working. What actually breaks the bridge is a *different object*
(`TenantContext` keeps its state in instance `AsyncLocal` fields). So the check resolves the app's context and compares
it by reference. That refuses `AddTenantContextScoped` / `Transient`, `AddTenantContext()` (default Scoped), and also
`AddTenantContextSingleton`, a process-wide but different instance the old doc listed as safe. It accepts any
registration that yields the bridge's own object. This keeps the decision's intent ("global only, fail fast") without
the wording's false positive.

- **When.** `TenantContextWiringCheck` (`Birko.EventBus.Tenant/Extensions/`) runs once, when the container first builds
  either half; both are now registered through factories. One throwaway scope per process, not per dispatch (option 2
  stays rejected). A registration-time check would be inert, because the consumer calls `AddEventTenantScope()` before
  `AddBirkoSecurity`. With the outbox hosted, the processor resolves `IEventScopeAccessor` when built, so the failure
  is at host start; otherwise it is the first publish, before any event is enriched. No `Hosting` dependency was added.
- **Diagnostic.** `TenantEventScopeAccessor.AllTenantsDispatches`, current state keyed by `EventContext.Source` (rule
  59), plus `OnAllTenantsDispatch`; a throwing subscriber is counted in `SubscriberFailures` and never fails the
  dispatch (rule 25). It reports, never refuses: a null-tenant dispatch still widens.

**Tests.** `tests/Birko.EventBus.Tenant.Tests/TenantContextWiringCheckTests.cs` (11): the four separate-instance
wirings, either registration order, and a mismatched explicit context are refused (6, **all red on the old
registration**). The scoped `_ => Tenant.Current` shape, no registered context and a matching explicit context are
accepted (green before and after), plus the diagnostic and its subscriber safety. The existing SH-H053 pins all
pass, `A_genuine_system_event_still_widens_and_must_keep_doing_so` included (26/26).
`tests/Birko.Security.AspNetCore.Tests/Extensions/EventTenantScopeCompositionTests.cs` runs the **real**
`AddBirkoSecurity` in Symbio's order (bridge first): accepted. Adding a scoped `TenantContext` after it is refused
(97/97; the project now imports `Birko.Rules`, `Birko.EventBus` and `Birko.EventBus.Tenant`).

**Live consumer.** Symbio's `Symbio.Tests.Unit` against this change: 3,951 passed, 21 skipped. Two failed, and both
are an artefact of the run: a running `Symbio.Api` held the default output files, so the build went to another
directory, and those two tests locate locale files at a fixed depth from their binary. `OutboxDispatchScopeTests`
(which calls `AddEventTenantScope()`) passed. `Program.cs` wires exactly the composition the security test pins.

## Out of scope

- **`SH-H053` itself** — confirmed and closed by [[TASK-311]] with the documentation fix and the
  mechanism pinned. This task is only about whether to go further.
- **`SH-H049`** — fixed in TASK-311 (per-request injection).
- The all-tenants-scope semantics generally — [[TASK-127]] owns the `IsAllTenantsScope` / ambient-tenant
  decision.

## Human test plan

- [ ] N/A until the decision is taken; a code option would need its own plan.
