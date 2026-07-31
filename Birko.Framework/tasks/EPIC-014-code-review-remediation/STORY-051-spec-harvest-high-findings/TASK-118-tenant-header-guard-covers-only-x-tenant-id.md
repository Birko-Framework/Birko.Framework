---
id: TASK-118
parent: STORY-051
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H048]
---

# The tenant header/claim guard covers only the hard-coded `X-Tenant-Id`

## Context

`../Birko.Security.AspNetCore/Tenant/TenantHeaderClaimGuardMiddleware.cs:57` — **CONFIRMED-NARROWER**.
Read the correction before scoping the fix; the original claim is right in substance and wrong in one detail.

The guard added on 2026-07-28 compares `X-Tenant-Id` against the JWT `tenant_id` claim, on the stated grounds
that anything else *"resolves to no tenant in `HeaderTenantResolver`"*. That reasoning holds for
`HeaderTenantResolver` alone — but `Birko.Data.Tenant`'s `TenantMiddleware` accepts other sources:

| Source | Status |
|---|---|
| `TenantMiddlewareOptions.TenantQueryStringKey` | **genuinely unguarded** — `?tenant={victim}` |
| `SubdomainTenantResolver` (from `Request.Host`) | **genuinely unguarded**, nothing correlated |
| `CustomTenantResolver` | unguarded by construction |
| `TenantMiddlewareOptions.TenantHeaderName` | **the claim missed this** — the header name is configurable, so a custom header escapes the guard's hard-coded constant |
| route value | **does not exist** — the claim is unsubstantiated; no `RouteValues` tenant source is implemented |

With `TenantQueryStringKey` configured, `?tenant={victim}` scopes every tenant-scoped read and write to the
victim while permissions stay home-tenant — the exact attack the 2026-07-28 guard was written to stop,
reached through a different door. A garbage header is waved through at line 81, so it can be sent alongside.

**The configurable-header gap is the more interesting half**, because it means the guard silently stops
working when a consumer customises `TenantHeaderName` — no error, no warning, and the deployment looks
correctly configured.

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

Also correct the finding: the "route" source does not exist. Note it in the findings doc rather than
implementing a guard for a resolver that was never written.

## Acceptance criteria

- [ ] A mismatch between the resolved tenant and the `tenant_id` claim returns 403
      `Tenant.HeaderClaimMismatch` regardless of **which** source resolved it
- [ ] `?tenant={victim}` with `TenantQueryStringKey` configured is refused
- [ ] Subdomain-resolved tenants are correlated with the claim
- [ ] A **renamed** `TenantHeaderName` is still guarded — the guard does not depend on a hard-coded constant
- [ ] A `CustomTenantResolver` result is guarded, or the guard fails closed with a clear diagnostic if it
      cannot see the resolution
- [ ] The documented pass-throughs still pass: no tenant resolved, unauthenticated, wildcard `*` holder,
      unparseable source
- [ ] Secure by default — no consumer has to opt in to get the widened coverage
- [ ] Regression tests in `Birko.Security.AspNetCore.Tests`, one per source in the table above
- [ ] `docs/security.md` § Tenant header/claim guard and `docs/tenant.md` updated — they currently describe a
      header-only guard
- [ ] The findings doc is corrected: the "route" tenant source does not exist, and `TenantHeaderName` being
      configurable is added to the finding
- [ ] `/specs regen` for `security-and-authorization` and `tenant-isolation`, spec diffs reviewed

## Out of scope

- `SH-H049` — downgraded in [[STORY-051]] (the shipped `TenantContext` uses `AsyncLocal`), not tasked. But
  note it touches the same middleware wiring; if this task ends up changing how `ITenantContext` is resolved
  in `UseTenantMiddleware`, re-read that downgrade before relying on it.
- `SH-H040` (`ValidateToken` fails open when authentication is disabled or expands to nothing) — unverified,
  same area, separate task.
- Store-layer tenant enforcement — [[TASK-114]]. This task is about the HTTP boundary only, and neither
  substitutes for the other.

## Human test plan

- [ ] With `TenantQueryStringKey` configured, authenticate as a user in tenant A and request a tenant-scoped
      list with `?tenant={B}`. Expect 403 `Tenant.HeaderClaimMismatch` and **no rows from B** — worth doing
      by hand once because the failure mode this guards is "the request succeeds and returns the wrong
      tenant's data", which looks identical to a working request in a log.
- [ ] Rename `TenantHeaderName` in a test app, send the renamed header with a foreign tenant, and confirm it
      is still refused — this is the case an automated test can assert but that a real deployment gets wrong
      through configuration, so confirming it end-to-end once is worth the minute.
