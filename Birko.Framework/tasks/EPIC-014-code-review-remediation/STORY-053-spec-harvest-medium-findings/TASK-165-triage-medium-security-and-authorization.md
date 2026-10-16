---
id: TASK-165
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M246, SH-M247, SH-M248, SH-M249, SH-M250, SH-M251, SH-M252, SH-M253, SH-M254, SH-M255, SH-M256, SH-M257, SH-M258, SH-M259, SH-M260]
pr: null
github-issue: null
jira-key: null
---

# Triage the 15 medium spec-harvest findings in `security-and-authorization`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **15** findings
that fall in the `security-and-authorization` area.

**Findings:** `SH-M246`–`SH-M260` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: security-and-authorization`, lines 1946–2037.

**The contract under review** is specced in [`docs/specs/security-and-authorization.md`](../../../docs/specs/security-and-authorization.md), harvested from
11 source globs — `../Birko.Security/**/*.cs`, `../Birko.Security.Jwt/**/*.cs`, `../Birko.Security.BCrypt/**/*.cs`, and 8 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/security-and-authorization.md` lying until `/specs regen security-and-authorization` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 15 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen security-and-authorization`, with the spec diff reviewed as the change's — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 406 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-176]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Resolution (2026-10-15)

**15 findings triaged: 13 confirmed, 2 confirmed-narrower, all 15 fixed.** Narrower: SH-M246 (0 / negative iterations were
already fixed by SH-H039; the unbounded half held) and SH-M250 (the binding short-circuit is refuted by design: a binding
restricts its token, so falling through to an unbound `Tokens` entry would make it decorative; the silent empty binding
held).

- **Hashing.** PBKDF2 refuses a stored count above `Math.Max(10_000_000, configured)` before deriving (SH-M246). BCrypt's
  shape check now requires a two-digit cost in 4..31, so `Verify` answers false where it threw (SH-M247).
- **JWT.** `ValidateToken` groups repeated claim types: `Claims` joins them with `;`, new `ClaimValues` lists them
  (SH-M248). It always checks issuer and audience, and throws `ArgumentException` when either is unconfigured, as
  `AddBirkoJwtBearer` effectively requires (SH-M257).
- **Client address (BREAKING behind a proxy).** New `AuthenticationConfiguration.TrustedProxies` (addresses or CIDR,
  empty by default). Forwarding headers are believed only from a trusted peer, `X-Forwarded-For` read from the right; the
  REST, SOAP, SSE and WebSocket transports go through the new `ResolveClientIp` (SH-M249). An emptied binding is logged
  at error level (SH-M250).
- **Secrets.** Vault escapes each key segment and refuses empty, `.` and `..` ones before any request (SH-M251); a KV v1
  body without `data` reads as empty (SH-M258). Azure Key Vault refuses a name outside `[0-9a-zA-Z-]{1,127}` (SH-M252) and
  follows `nextLink` on its own host only (SH-M260). New `ISecretProvider.IsHierarchical` (Azure: false) makes the
  configuration provider list a flat vault once and load `path--rest` at `rest` (SH-M254).
- **ASP.NET Core.** `UseResolvedPermissions` removes every `ICurrentUser`, and `AddBirkoSecurity` uses `TryAddScoped`, so
  the order does not matter (SH-M255). `?token=` is read only with no `Authorization` header, on a WebSocket upgrade, an
  `Accept: text/event-stream` request, or a path in the new `JwtAuthenticationOptions.QueryTokenPathPrefixes` (SH-M256).
- **OIDC.** The provider map is keyed and looked up by `Canonical` (SH-M253). `HttpOidcSigningKeySource` stops serving
  stale keys past `maxStaleAge` (default 24 h) and reports `LastServedStale` (SH-M259).

**Red-verified, as numbers.** Each mechanism was reverted with the new API kept (the `int.MaxValue` PBKDF2 case left out:
reverted, it derives for hours), and the 70 new tests run: **52 failed, 18 passed.** The 18 are contract pins, passing
either way by construction: a real hash still verifies (PBKDF2, BCrypt), a single claim reads as before, a configured
issuer refuses a foreign one, a provider without issuer still generates, `GOOGLE` resolved before too, SSE / WebSocket /
configured-prefix requests still read `?token=`, `AddBirkoSecurity` maps the prefixes, `UseResolvedPermissions` after
`AddBirkoSecurity` and `AddBirkoSecurity` alone, a listed folder's trailing `/`, a root-path flat load, Azure declaring
itself flat, `X-Real-IP` from a trusted proxy and a single forwarded hop through one.

**Regression:** the 15 suites importing `Birko.Security*` or the four transport projects, 801 tests, green. Symbio builds.

**Consumer impact, reported, not changed here:**
- **Symbio** replaces the bearer events with its own `OnMessageReceived` (`Program.cs:131`), which accepts `?token=` on
  every path (for `/api/media`): the SH-M256 exposure, outside this fix. Narrowing it to `/api/media` is a Symbio task.
- **DraCode**'s `RunEventsSseTests` opens its SSE stream without `Accept: text/event-stream`; a browser `EventSource` sends
  it, the test does not, so it will now be unauthenticated. Either the test adds the header or DraCode sets
  `QueryTokenPathPrefixes`. (DraCode does not build at the moment for an unrelated reason, `AgentOptions.CheckpointInterval`.)
- A deployment of the REST / SOAP / SSE / WebSocket token bindings **behind a reverse proxy** must list it in
  `TrustedProxies`, or every request is seen as coming from the proxy. No consumer in `Birko\Consumers` configures bindings.

**Spec regen pending**, batched: `security-and-authorization`.

## Human test plan

N/A: fully covered by automated tests; a library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-165` — leave empty until then._
