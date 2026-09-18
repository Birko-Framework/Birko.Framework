---
id: TASK-311
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H049, SH-H053]
pr: "[Birko.Data.Tenant@f374982, Birko.EventBus.Tenant@31cc2dd, Birko.Data.Tenant.Tests@d3a8526, Birko.EventBus.Tenant.Tests@03a8026, Birko.Security.AspNetCore.Tests@5e6a406]"
github-issue: null
jira-key: null
---

# Triage the 2 remaining high spec-harvest findings in `tenant-isolation`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **2** open findings in the `tenant-isolation` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H049` | UseTenantMiddleware binds ITenantContext from the root provider, so a Scoped registration is never observed | `Birko.Data.Tenant/Middleware/TenantMiddleware.cs:222` |
| `SH-H053` | AddEventTenantScope() binds Tenant.Current, which AddTenantContext* never registers, so events lose their tenant | `Birko.EventBus.Tenant/Extensions/EventTenantScopeServiceCollectionExtensions.cs:28` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: tenant-isolation`, lines 352-422.

**The contract under review** is specced in [`docs/specs/tenant-isolation.md`](../../../docs/specs/tenant-isolation.md),
harvested from 32 source files — `../Birko.Data.Sync.Tenant/Models/ITenantSyncKnowledgeItem.cs`, `../Birko.Data.Sync.Tenant/Models/TenantSyncKnowledgeItem.cs`, `../Birko.Data.Sync.Tenant/Models/TenantSyncOptions.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Mostly latent.** `UseTenantMiddleware` and `AddTenantContext*`: **0** consumer `.cs` files. `AddEventTenantScope` (`SH-H053`): **1**. So the middleware half cannot currently bite a consumer and the event half can.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** both claims are a tenant scope that **fails open** — the class of [[TASK-114]] (P0). And `SH-H049` is the one finding here whose status is already contested (see below).

⚠ **`SH-H049`'s status is contested and must be re-read before it is worked.** Three closed tasks
record it as *"downgraded in [[STORY-051]], not tasked"*, on the grounds that the shipped `TenantContext`
uses `AsyncLocal`. But [[TASK-118]] then wrote the opposite into its own body: the guard *"would **fail
open**: `UseTenantMiddleware` binds its context from the root provider (SH-H049), so under
`AddTenantContextScoped()` the guard's request-scoped instance is a different object, sees no tenant, and
waves the request through silently"* — and it closed with **"SH-H049 is not fixed here. This task
routes around it rather than through it."** So a downgrade and a live exploit path are both on the
record. Per § TASK-283, **re-measure the downgrade's premise before relying on it**; a stale
downgrade is exactly how a P0 stays invisible.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/tenant-isolation.md` lying until `/specs regen tenant-isolation` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 2 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen tenant-isolation`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 37 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-161]] owns those.
- Low-severity findings in this area — [[TASK-185]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

**N/A — fully covered by automated tests.**

Resolved at close (2026-09-09) rather than left as the placeholder. The reason: both halves are library
wiring with no rendered surface. The middleware half is asserted through a real `ServiceCollection` with
`validateScopes: true`, so the DI lifetime that *was* the defect is exercised rather than simulated; the
event-bridge half is a chain of boolean/Guid states asserted directly, including the all-tenants widening
and the system-event case that must keep widening. A human clicking through an app could not distinguish
the mis-wired bridge from a correctly-wired one — that indistinguishability is the finding — so a manual
pass would be strictly weaker than the tests.

## Implementation plan

_Populated by `/tasks plan TASK-311` — leave empty until then._

## Outcome

Two findings, **both confirmed**, and they needed **different kinds of fix** — which is the single most
useful thing to carry out of this task.

### `SH-H049` — a code fix

`UseTenantMiddleware` resolved `ITenantContext` from `builder.ApplicationServices` (the **root**
provider) and passed it as a constructor argument. Under the documented `AddTenantContextScoped()` the
middleware therefore set the tenant on an instance no request-scoped store ever read — and because
`TenantContext` keeps its state in **instance** `AsyncLocal` fields, those stores saw
`HasTenant == false`. The tenant wrappers **deliberately fail open** there (CR-L229, pinned by
`TenantFailOpenTests`), so they read and wrote across every tenant.

Fixed by taking the context **per request**: ASP.NET Core injects an extra `InvokeAsync` parameter from
the request scope, which is the only way a singleton middleware can observe a scoped registration. The
constructor parameter is kept and still wins, so a hand-built pipeline is unaffected. The friendly
start-up error survives via `IServiceProviderIsService`, which answers *"is it registered?"* **without
instantiating** — deliberately, because resolving a scoped service from the root provider is the very
thing being removed and throws under `ValidateScopes`.

### `SH-H053` — a documentation fix, because the code cannot detect it

`AddEventTenantScope()` binds `Tenant.Current`, and its doc claimed that was *"the same context
`AddBirkoSecurity` / `AddTenantContext*` register"*. Traced link by link:

1. `AddBirkoSecurity` registers `_ => Tenant.Current` — the claim is **true** here.
2. every `AddTenantContext*` registers `typeof(TenantContext)`, so the container builds a **different**
   instance — the claim is **false** here;
3. `TenantContext`'s `AsyncLocal` fields are **instance**, not static, so those two share nothing;
4. so `TenantEventEnricher` sees `HasTenant == false` and leaves `EventContext.TenantGuid` **null**;
5. and null is exactly how a *genuine system event* is spelled, so `TenantEventScopeAccessor` runs the
   handler inside `WithAllTenantsAsync` — a tenant-scoped event dispatched across **every** tenant, with
   `Strict` repositories following it there.

⚠ **Step 5 is why this could not be fixed in code.** The mis-wired case and a legitimate system event are
**byte-identical from the event**, so nothing at the dispatch point can tell them apart, and narrowing the
null branch would break cross-tenant system events — which are its documented purpose. The false claim
appeared in **two** files (the registration extension and the accessor's own remarks); both corrected,
each now naming which registrations are safe and why.

⚠ **And a harder limit, recorded rather than papered over:** with `AddTenantContextScoped` /
`Transient`, **no** overload of `AddEventTenantScope` can work. Both bridge halves are registered *and
consumed* as singletons (`OutboxProcessor` takes `IEventScopeAccessor` from the root provider; enrichers
are `AddSingleton`), so there is no per-request instance for them to hold. That is a property of the
bridge's lifetime, not of the wiring, and it is now stated on the API.

### Step 6 — three mutations, and ⚠ the first fix attempt was WRONG in a way only the third caught

| Mutation | Result |
|---|---|
| remove the `InvokeAsync` per-request parameter (the literal revert) | **build break** — `CS1501` at **5** call sites in two test projects; no test can run to fail |
| keep the signature, ignore the injected context (`_tenantContext ?? Tenant.Current`) | **2 of 76** red — `The_request_scoped_context_is_the_one_that_receives_the_tenant`, `A_scoped_registration_is_genuinely_observed_end_to_end` |
| `UseTenantMiddleware` resolves from `builder.ApplicationServices` again (the original defect) | **1 of 76** red — `UseTenantMiddleware_resolves_the_context_from_the_REQUEST_scope` |

The literal revert is a **compiler error, not a red test** — a stronger signal (it cannot be ignored) but
not a split, which is why the second and third mutations were run. The third is the real regression
prover: it restores the exact defect and a test names it.

⚠ **And the third mutation only exists because the security/correctness pass found the wiring had no test
at all** — `UseTenantMiddleware` has 0 production callers, so nothing covered it, and **the first version
of this fix was broken there.** It passed `null` to `UseMiddleware<TenantMiddleware>(null, options)`;
that helper matches args to constructor parameters through `ActivatorUtilities`, which **cannot bind a
null**, so the optional `ITenantContext` parameter would have been left unmatched and filled from the
**application (root) provider** — silently reinstating the very capture this task removes. Every test in
the suite passed with that version, because none of them went through `UseTenantMiddleware`. The shipped
wiring resolves from `ctx.RequestServices` instead, which is unambiguous, and the new test is what makes
the difference visible. *A fix in a code path nothing tests is a guess.*

⚠ **My own first draft of that test then resolved a scoped service from the root provider** (a leftover
`provider.GetRequiredService<ITenantContext>()` assertion) and failed with *"Cannot resolve scoped service
from root provider"* — the exact sin the fix removes, committed inside its own regression test. Recorded
because it is a fair illustration of how easy the mistake is.

**Contract pins, green throughout and pins rather than evidence:**
`UseTenantMiddleware_still_fails_at_startup_when_the_context_is_unregistered` (the friendly wiring-time
error survived the move to `IServiceProviderIsService`),
`A_constructor_supplied_context_still_wins_so_hand_built_pipelines_keep_working`,
`The_tenant_is_cleared_on_the_instance_it_was_set_on`, `InvokeAsync_takes_the_tenant_context_as_a_parameter`,
`The_constructor_context_is_optional`, and **all five** SH-H053 mechanism tests — those pin a *premise*
(instance-level `AsyncLocal`; the chain's terminus) rather than a behaviour change, because that finding's
remedy was prose. `A_genuine_system_event_still_widens_and_must_keep_doing_so` is the one that stops a
future reader "fixing" the widening.

**Suites:** Data.Tenant 76/76, EventBus.Tenant 15/15, Security.AspNetCore 95/95, EventBus.Outbox 9/9 —
**195 green, 0 failed.**

### Judgement calls

- **⚠ The contested downgrade is resolved, not overridden.** Three closed tasks recorded `SH-H049` as
  *"downgraded, not tasked"*; [[TASK-118]] described a live fail-open through it. Both are right about
  different things: TASK-118's concern was the **guard**, and TASK-118 itself routed the guard around the
  `ITenantContext` registration (it reads `HttpContext.Items`), so that path is already closed. What was
  left is the middleware/store mismatch, which is what this task fixed. **Re-measured scope:**
  `UseTenantMiddleware` has **0** production callers in the framework or any of the 16 consumer repos —
  only doc comments — so this was latent, and that is exactly what made the signature change affordable.
- **The failure mode splits by environment, which is worth keeping.** Development throws
  (`ValidateScopes` refuses a scoped resolve from root); Production is silent. The dangerous configuration
  is the one nobody sees.
- **Rejected: making the event bridge refuse to widen when it cannot establish a tenant.** It is the
  change that would turn SH-H053 from documented into prevented, and it was **not** made — see the escalation
  below. Doing it silently would have been the wrong call: it needs DI plumbing into two singletons and it
  changes semantics documented as intentional.
- **Rejected: a registration-time guard on `AddEventTenantScope`.** Inspecting the `IServiceCollection`
  for an `ITenantContext` descriptor looked attractive and is **measurably inert**: the sole consumer calls
  `AddEventTenantScope()` at `Program.cs:101` and `AddBirkoSecurity` at `:104`, so at guard time no
  descriptor exists. A guard that cannot fire on the only real call pattern is worse than none.
- **The sole consumer is correctly wired.** Symbio uses `AddBirkoSecurity` → `Tenant.Current`, so its
  bridge sees the right instance. Both findings were latent; neither was an active leak.

### Flagged, not fixed — and one escalation

- **⚠ ESCALATION for the user (deliberately not decided here).** Should the event bridge *refuse to
  widen* when it cannot establish a tenant, instead of dispatching across all tenants? It would need an
  `IServiceProvider`/`IServiceScopeFactory` in two singletons plus a per-dispatch scope to detect the
  mismatch, and it changes behaviour that is documented as intentional for system events — with a live
  consumer on this path. That is a design decision with consumer impact, so per the guardrails everything
  non-controversial shipped and this is asked rather than guessed. **Recommendation:** file it as its own
  task rather than widening this one; the documentation + mechanism pins make the current behaviour
  legible in the meantime.
- **`AddTenantContextScoped` remains unusable with the event bridge** by construction (singleton
  lifetime). Now documented on the API; making it work is the same design question as above.

## Progress log

- step 2 — picked; ranked above TASK-310 because `SH-H049` makes a tenant **guard fail open** (TASK-118:
  *"waves the request through silently"*), which is the bypass end of key 1, while TASK-310's cross-tenant
  cache-key collision is leakage one tier down. Also wins key 3 (silent). ⚠ TASK-310 is **stronger** on
  key 4 — three findings in one project, no contested status — but key 1 outranks key 4, and resolving
  `SH-H049`'s contested downgrade is step 3's work rather than an open decision. Key 6 inert: no story in
  this tree declares `theme:`, so all 82 pool candidates are undeclared.
- step 3 — verified: **both hold; the contested framing on `SH-H049` is RESOLVED and its scope re-measured.** `SH-H049`'s recorded `CONFIRMED-NARROWER` stands — `UseTenantMiddleware` still resolves from `builder.ApplicationServices` (the root provider) and passes it as a ctor arg. The contradiction with [[TASK-118]] dissolves on reading: TASK-118's fail-open was about the **guard**, which TASK-118 itself routed around (it reads `HttpContext.Items`), so the guard no longer depends on the registration. What remains is the middleware/store instance mismatch — and it is **0 production callers**: `UseTenantMiddleware` appears nowhere in the framework or any of the 16 consumer repos except doc comments. `SH-H053` holds **exactly as written**, traced link by link (see the Outcome).
- step 4 — layer: **local**, both. `Birko.Data.Tenant`'s own middleware wiring and `Birko.EventBus.Tenant`'s own registration doc.
- step 5 — fix in `Birko.Data.Tenant/Middleware/TenantMiddleware.cs` (per-request injection + `IServiceProviderIsService` presence probe) and `Birko.EventBus.Tenant/{Extensions/EventTenantScopeServiceCollectionExtensions,TenantEventScopeAccessor}.cs` (the false claim, in both files); tests in `Birko.Data.Tenant.Tests/TenantMiddlewarePerRequestContextTests.cs` (6) and `Birko.EventBus.Tenant.Tests/MismatchedTenantContextWidensToAllTenantsTests.cs` (5), plus two existing call sites updated. Suites: Data.Tenant 74/74, EventBus.Tenant 15/15, Security.AspNetCore 95/95, EventBus.Outbox 9/9 — **193 green, 0 failed**.
- step 6 — ⚠ **the revert is a BUILD BREAK, not a red test**, and that is reported as what it is: removing the `InvokeAsync` parameter fails compilation with `CS1501` in **5** call sites across two test projects, so no test can run to fail. A compiler error is a stronger signal than a red test (it cannot be ignored), but it is not a split. So a second mutation was run that the compiler tolerates — keep the signature, ignore the injected context (`var ambient = _tenantContext ?? Tenant.Current`): **2 of 74** red, exactly `The_request_scoped_context_is_the_one_that_receives_the_tenant` and `A_scoped_registration_is_genuinely_observed_end_to_end`. Contract pins green throughout and named as pins: `A_constructor_supplied_context_still_wins_...`, `The_tenant_is_cleared_on_the_instance_it_was_set_on`, both reflection pins, and **all 5** of the SH-H053 mechanism tests — those pin a premise, not a behaviour change, because that finding's remedy was documentation.
- step 7 — respecced `tenant-isolation`. Requirement **retitled** from *"UseTenantMiddleware resolves ITenantContext from the application root provider"* (the title asserted the defect) to *"…takes ITenantContext per request, not from the root provider"*, its SHALL rewritten, the *"captures one context instance for the app's lifetime"* scenario replaced by *"A scoped registration is observed by the request's own stores"*, and a *"hand-supplied context still wins"* scenario added. ⚠ The `AddEventTenantScope` requirement needed **no correction** — unlike the code doc comments, the spec already said the bridge *"only matches store/repository behaviour if the app's `ITenantContext` registration is that same instance"*. What it never recorded was the **consequence** of a mismatch, so a scenario was added for the all-tenants widening the new tests pin.
- step 6 (amended) — a **third** mutation was added after the correctness pass found `UseTenantMiddleware` had no coverage: restoring the root-provider resolve reds `UseTenantMiddleware_resolves_the_context_from_the_REQUEST_scope`, **1 of 76**. That pass also found the **first version of this fix was wrong** — `UseMiddleware<TenantMiddleware>(null, options)` cannot bind a null through `ActivatorUtilities`, so the context would have come from the root provider again; every existing test passed with it. Rewired to resolve from `ctx.RequestServices`.
- step 8 — closed done; see the Outcome. Escalation filed as [[TASK-328]] rather than decided.
