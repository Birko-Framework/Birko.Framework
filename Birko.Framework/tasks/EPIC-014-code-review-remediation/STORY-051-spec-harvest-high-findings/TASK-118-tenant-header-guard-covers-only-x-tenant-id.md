---
id: TASK-118
parent: STORY-051
feature: FEATURE-014
status: review
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
pr: [Birko.Data.Tenant@c4dd307, Birko.Security.AspNetCore@0c4a494, Birko.Security.AspNetCore.Tests@4e60097, Birko.Data.Tenant.Tests@d2b8cb6]
github-issue: null
jira-key: null
findings: [SH-H048]
---

# The tenant header/claim guard covers only the hard-coded `X-Tenant-Id`

## Context

`../Birko.Security.AspNetCore/Tenant/TenantHeaderClaimGuardMiddleware.cs:57` — **CONFIRMED-WIDER**
(re-verified 2026-08-02; supersedes the filed CONFIRMED-NARROWER verdict). The original claim is right in
substance, and the "one detail" the earlier pass corrected it on was the correction that was wrong — see
§ Corrections below before scoping anything.

The guard added on 2026-07-28 compares `X-Tenant-Id` against the JWT `tenant_id` claim, on the stated grounds
that anything else *"resolves to no tenant in `HeaderTenantResolver`"*. That reasoning holds for
`HeaderTenantResolver` alone.

**Re-verified by hand 2026-08-02 (step 3). The finding holds and is broader than filed; two of the
corrections carried in this task were themselves wrong.** There are **two independent tenant-resolution
stacks**, not one, and the guard covers exactly one source across both:

**Stack A — `Birko.Security.AspNetCore`** (`ITenantResolver` + its own `Tenant/TenantMiddleware.cs`, wired by
`SecurityServiceExtensions.AddBirkoSecurity`):

| Source | Status |
|---|---|
| `HeaderTenantResolver` → `X-Tenant-Id` | **guarded** — the guard's hard-coded constant happens to match |
| `SubdomainTenantResolver` → `Request.Host` | **unguarded**, nothing correlated |
| any custom `ITenantResolver` (`TenantResolverType.Custom`) | unguarded by construction |

**Stack B — `Birko.Data.Tenant`** (`Middleware/TenantMiddleware.cs` + `TenantMiddlewareOptions`):

| Source | Status |
|---|---|
| `TenantQueryStringKey` | **unguarded** — `?tenant={victim}` |
| `TenantRouteKey` (`:111-120`) | **unguarded — and it DOES exist.** See the correction below |
| `CustomTenantResolver` | unguarded by construction |
| `TenantHeaderName` (default `X-Tenant-Id`) | **guarded only while left at its default** — the name is configurable, so a renamed header escapes the guard's hard-coded constant |

With `TenantQueryStringKey` configured, `?tenant={victim}` scopes every tenant-scoped read and write to the
victim while permissions stay home-tenant — the exact attack the 2026-07-28 guard was written to stop,
reached through a different door. A garbage header is waved through at line 81, so it can be sent alongside.

**The configurable-header gap is the more interesting half**, because it means the guard silently stops
working when a consumer customises `TenantHeaderName` — no error, no warning, and the deployment looks
correctly configured.

### Corrections to this task's own Context (step 3, 2026-08-02)

1. **The route source is real.** This task and SH-H048's verdict in the findings doc both assert that no
   `RouteValues` tenant source is implemented and instruct the fix to record it as nonexistent. That is
   false: `Birko.Data.Tenant/Middleware/TenantMiddleware.cs:111-120` reads
   `TenantMiddlewareOptions.TenantRouteKey` via `context.GetRouteValue(...)`. Acting on the old correction
   would have deliberately left a live, unguarded source out of the fix — the inverse of the usual
   misscoping, and the reason this task's acceptance list is amended rather than followed.
2. **Stack A was missed entirely.** The original Context attributes every alternative source to
   `Birko.Data.Tenant`. The subdomain gap it names actually lives in `Birko.Security.AspNetCore`'s own
   resolver chain, which the finding never mentions.
3. **The guard has no tests at all.** `Birko.Security.AspNetCore.Tests/Tenant/` contains resolver, adapter
   and middleware tests but nothing for `TenantHeaderClaimGuardMiddleware` — it shipped unpinned on
   2026-07-28. This task therefore authors the guard's first suite rather than extending one.

### Why `HttpContext.Items` and not just `ITenantContext`

Both stacks funnel into `ITenantContext`, so reading the resolved tenant there covers every source. But it
can **fail open** on a lifetime mismatch: stack B's `UseTenantMiddleware` captures its `ITenantContext` from
`ApplicationServices` at startup, while the guard resolves one from `RequestServices`. Under
`AddTenantContextScoped()` those are different objects, the guard would see no tenant and wave the request
through — a silent fail-open, which is exactly the failure class being fixed. So each middleware now also
publishes its resolution on `HttpContext.Items` under a fixed key, which is per-request by construction and
immune to both the lifetime mismatch and the configurable `TenantContextKey`.

## Approach

Guard the **resolved tenant**, not a specific transport. The guard currently reads one header by name; it
should compare whatever the resolution chain actually produced against the claim. That closes query-string,
subdomain, custom resolvers and renamed headers in one move, and it cannot be outflanked by a source added
later — which is precisely how this gap arose.

That requires the guard to see the resolved tenant *and* the authenticated principal. The ordering
constraint that produced the current design still holds: `TenantMiddleware` runs before
`UseAuthentication()`, so `context.User` is unpopulated there. So the guard stays a post-authentication step
and needs the resolution result **and its source** carried forward — likely on `HttpContext.Items` or a
scoped accessor set by `TenantMiddleware`.

Keep the deliberate pass-throughs from the original design and their reasons: no tenant resolved (the claim
is then the only source; SSE cannot set headers), unauthenticated (login/register/setup), wildcard `*`
holders (cross-tenant reach is intentional). **Stay secure-by-default** — the existing
`RequireTenantHeaderMatchesClaim = true` is the precedent, and an opt-in guard protects nobody.

~~Also correct the finding: the "route" source does not exist.~~ **Struck at step 3** — the route source
does exist (`TenantMiddleware.cs:111-120`); what needed correcting was the correction. The findings doc
carries the retraction instead, and the route source is guarded like every other.

## Acceptance criteria

- [x] A mismatch between the resolved tenant and the `tenant_id` claim returns 403
      `Tenant.HeaderClaimMismatch` regardless of **which** source resolved it
- [x] `?tenant={victim}` with `TenantQueryStringKey` configured is refused
- [x] A **route-resolved** tenant (`TenantRouteKey`) is refused — amended: the source exists, see correction 1
- [x] Subdomain-resolved tenants are correlated with the claim
- [x] A `CustomTenantResolver` (stack B) and a custom `ITenantResolver` (stack A) result are both guarded
- [x] A **renamed** `TenantHeaderName` is still guarded — the guard does not depend on a hard-coded constant
- [x] The guard cannot silently fail open when it cannot see the resolution — the resolution is published
      per-request, not read through a DI registration whose lifetime the guard does not control
- [x] The documented pass-throughs still pass: no tenant resolved, unauthenticated, wildcard `*` holder,
      unparseable source
- [x] The literal `X-Tenant-Id` check is **retained** alongside the resolved-tenant check, so an app that
      never wired tenant resolution is no less protected than before this task
- [x] Secure by default — no consumer has to opt in to get the widened coverage
- [x] Regression tests in `Birko.Security.AspNetCore.Tests`, one per source in both tables above — this is
      the guard's first suite (correction 3), so the pass-throughs need pinning too
- [x] `docs/security.md` § Tenant header/claim guard and `docs/tenant.md` updated — they currently describe a
      header-only guard
- [x] The findings doc is corrected: `TenantHeaderName` being configurable is added to the finding, **and
      SH-H048's "no RouteValues tenant source exists" verdict is retracted** — it does exist
- [x] `/specs regen` for `security-and-authorization` and `tenant-isolation`, spec diffs reviewed

## Out of scope

- `SH-H049` — downgraded in [[STORY-051]] (the shipped `TenantContext` uses `AsyncLocal`), not tasked. But
  note it touches the same middleware wiring; if this task ends up changing how `ITenantContext` is resolved
  in `UseTenantMiddleware`, re-read that downgrade before relying on it.
- `SH-H040` (`ValidateToken` fails open when authentication is disabled or expands to nothing) — unverified,
  same area, separate task.
- Store-layer tenant enforcement — [[TASK-114]]. This task is about the HTTP boundary only, and neither
  substitutes for the other.

## Progress log

- step 2 — picked; ranked above TASK-113 (`TenantSyncProvider` cross-tenant reads/deletes, P0) because this
  is an authorization bypass reachable directly from untrusted input (`?tenant={victim}` on an authenticated
  request), where TASK-113 is cross-tenant leakage reachable only via a background sync job's configuration —
  one rung down on both severity and reachability. TASK-113 wins only on self-containment (key 4).
- step 3 — verified: **held, rescoped wider**. Confirmed unguarded: query-string, route, subdomain, both
  custom-resolver hooks, renamed header. Three corrections to the task itself, all written into `## Context`:
  (1) this task's own instruction to record the route source as nonexistent is **wrong** — it exists at
  `Birko.Data.Tenant/Middleware/TenantMiddleware.cs:111-120`; (2) there are two resolution stacks and the
  finding only described one; (3) the guard has **no tests at all**, so this authors its first suite.
  Acceptance list amended accordingly *before* any code was written.
- step 4 — layer: local (both production repos are this family's own: `Birko.Security.AspNetCore` owns the
  guard, `Birko.Data.Tenant` owns stack B's middleware; no upstream package involved)
- step 5 — fix in `Birko.Data.Tenant/Middleware/ResolvedTenant.cs` (new), `Birko.Data.Tenant/Middleware/
  TenantMiddleware.cs`, `Birko.Security.AspNetCore/Tenant/TenantMiddleware.cs`,
  `Birko.Security.AspNetCore/Tenant/TenantHeaderClaimGuardMiddleware.cs`; tests in
  `Birko.Security.AspNetCore.Tests/Tenant/TenantHeaderClaimGuardMiddlewareTests.cs` (18, the guard's first);
  suites green — Birko.Security.AspNetCore.Tests 95/95, Birko.Data.Tenant.Tests 59/59
- step 6 — reverted fix (git stash on both production repos): **9/16 failed**, and 2 more could not compile
  against pre-fix code at all. Fix-dependent (11) = `QueryStringSource_AddressingAnotherTenant_IsRefused`,
  `RouteValueSource_…`, `RenamedTenantHeader_…`, `CustomTenantResolver_…`, `SubdomainResolver_…`,
  `CustomITenantResolver_…`, `RefusalBodyNamesTheSourceThatWasUsed`,
  `ConfiguredKeyNamesCannotBreakOutOfTheJsonBody`, `SystemScopeToken_CannotAddressARealTenant`, plus
  `ResolutionIsPublishedPerRequest_NotThroughATenantContextRegistration` and
  `ResolvedTenant_RoundTripsThroughHttpContextItems` (compile-level). Contract pins (7, green both ways,
  evidence of nothing) = `LiteralTenantHeader_AddressingAnotherTenant_IsStillRefused`, `MatchingTenant_…`,
  `NoTenantAddressed_…`, `UnparseableSource_…`, `UnauthenticatedRequest_…`, `WildcardHolder_…`,
  `GuardDisabled_IsAllowed`. **The revert reclassified one test**: `SystemScopeToken_…` was filed under
  contract pins and failed — it reaches the victim through the query string, so it never pinned old
  behaviour. Moved into the fix-dependent section with that reasoning in its comment.
- step 7 — respecced `tenant-isolation`. Requirements changed: *X-Tenant-Id must agree with the caller's
  tenant claim* → *Every tenant a request addresses must agree with the caller's tenant claim* (title
  asserted the old behaviour); *Deliberate pass-throughs of the header/claim guard* → *…of the tenant/claim
  guard* (the unparseable-header branch is no longer a separate check — it folds into addresses-no-tenant);
  *Birko.Data.Tenant HTTP tenant resolution order* (now returns a source description); *…middleware sets
  HttpContext.Items…* (now also publishes under the fixed key); *Birko.Security.AspNetCore tenant middleware
  always clears in a finally* (now publishes). Added *The resolved tenant is published per-request under a
  fixed key*. Frontmatter re-stamped to `ce65678` with `ResolvedTenant.cs` added to `sources`.
  **`security-and-authorization` needed no change** — `AddBirkoSecurity`, the resolver selection and both
  defaults are untouched by this fix; checked rather than skipped.

## Outcome

**What was fixed.** The tenant/claim guard checked one hard-coded `X-Tenant-Id` header, so a caller
authenticated in tenant A could reach tenant B through any other door — a query-string key, a route value, a
subdomain, either custom-resolver hook, or simply a renamed `TenantMiddlewareOptions.TenantHeaderName`. The
request succeeded: repository scoping followed the resolved tenant while permissions stayed with the token,
so the victim's rows were read and written with nothing logged and nothing failing. The guard now checks
**the tenant the resolution chain actually produced**, which each resolving middleware publishes as a
`ResolvedTenant` on `HttpContext.Items`, so every source is covered and one added later is covered too.

**Step-6 split.** Reverting both production repos: **9 of 16 tests failed**, and 2 more do not compile
against pre-fix code at all — 11 fix-dependent. The 7 that stayed green are contract pins (the literal-header
refusal, the matching-tenant pass, and the four documented pass-throughs plus the opt-out); they pin
behaviour the fix must not change and are **evidence of nothing**. Names are in the step-6 progress-log line.

**Judgement calls, and the stricter option rejected in each case:**

- **`HttpContext.Items`, not `ITenantContext`.** Reading the resolved tenant off `ITenantContext` is simpler
  and covers the same sources — and would **fail open**: `UseTenantMiddleware` binds its context from the
  root provider (SH-H049), so under `AddTenantContextScoped()` the guard's request-scoped instance is a
  different object, sees no tenant, and waves the request through silently. The fixed `HttpContext.Items` key
  cannot be broken by a container lifetime, and — unlike `TenantContextKey` — cannot be renamed out from
  under the guard, which is the same class of configuration change that defeated the header constant.
- **The literal `X-Tenant-Id` check was kept, not replaced.** Dropping it in favour of the resolved-tenant
  check alone would be cleaner, and would have made this a *coverage regression* for any app that never
  wired a tenant middleware but reads the header in its own code — the exact premise the original guard was
  written on. Additive instead.
- **`ITenantContext` is a required `InvokeAsync` parameter**, so a missing registration fails the request
  rather than degrading the guard to a pass-through. An optional lookup with a null fallback was rejected for
  the obvious reason: a security check that quietly disables itself is the bug being fixed.
- **The 403 code stayed `Tenant.HeaderClaimMismatch`.** It is consumer-visible and renaming it would break
  clients for a cosmetic gain; only the human-readable half changed, to name the door that was used.

**Flagged, not fixed:**

- **SH-H048's filed verdict was wrong about the route source** and this task carried the error forward as an
  instruction ("record that the route source does not exist"). Both are corrected in place — see `## Context`
  correction 1 and the retraction written into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Nothing was left for a
  follow-up, but it is worth knowing a *correction* to a finding can be the thing that is wrong.
- **SH-H049 is not fixed here.** This task routes around it rather than through it: the guard no longer
  depends on the `ITenantContext` registration, but `UseTenantMiddleware` still binds from the root provider
  and every other consumer of that instance still has the problem. It remains open in STORY-051.
- The `UseBirkoTenantHeaderGuard()` / `RequireTenantHeaderMatchesClaim` names are now historical — both say
  "header" for something that is no longer header-scoped. Renaming is a breaking API change for consumers,
  so both keep their names with the discrepancy documented on each.

## Human test plan

- [ ] With `TenantQueryStringKey` configured, authenticate as a user in tenant A and request a tenant-scoped
      list with `?tenant={B}`. Expect 403 `Tenant.HeaderClaimMismatch` and **no rows from B** — worth doing
      by hand once because the failure mode this guards is "the request succeeds and returns the wrong
      tenant's data", which looks identical to a working request in a log.
- [ ] Rename `TenantHeaderName` in a test app, send the renamed header with a foreign tenant, and confirm it
      is still refused — this is the case an automated test can assert but that a real deployment gets wrong
      through configuration, so confirming it end-to-end once is worth the minute.
- step 8 — closed review (human test plan outstanding); c4dd307 / 0c4a494 / 4e60097 / d2b8cb6
