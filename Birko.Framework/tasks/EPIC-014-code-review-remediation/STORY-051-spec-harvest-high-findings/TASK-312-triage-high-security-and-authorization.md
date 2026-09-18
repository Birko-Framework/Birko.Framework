---
id: TASK-312
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
findings: [SH-H040]
pr: "[Birko.Security@7e16d32, Birko.Communication.SSE@e778052, Birko.Security.Tests@3d63cee, Birko.Communication.SSE.Tests@93ab6eb]"
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `security-and-authorization`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `security-and-authorization` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H040` | AuthenticationService.ValidateToken fails open when authentication is disabled or expands to nothing | `Birko.Security/Authentication/AuthenticationService.cs:76` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: security-and-authorization`, lines 292-306.

**The contract under review** is specced in [`docs/specs/security-and-authorization.md`](../../../docs/specs/security-and-authorization.md),
harvested from 43 source files — `../Birko.Security.AspNetCore/Authentication/JwtAuthenticationOptions.cs`, `../Birko.Security.AspNetCore/Authentication/JwtBearerExtensions.cs`, `../Birko.Security.AspNetCore/Authentication/JwtClaimNames.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **LIVE.** `AuthenticationService` is referenced from **6** consumer `.cs` files — the only area in this intake with measured production use of the exact type the finding names.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** token validation that **fails open** is the class of [[TASK-108]] (any password verifies against an empty-segment hash), which was P0.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/security-and-authorization.md` lying until `/specs regen security-and-authorization` runs, and **that spec
diff is the fix's evidence**.

## ⚠ Re-verified and RESCOPED 2026-09-08, before any code was written

The mechanism **holds**. The finding's stated **trigger is wrong**, and it names the one case that is
*safe*. Two further facts widen the fix. All measured, not read.

### The mechanism, confirmed

`AuthenticationService.cs:54` and `:76`:

```csharp
public bool IsAuthenticationEnabled()
    => _config.Enabled && (_expandedTokens.Count > 0 || _expandedBindings.Count > 0);

public bool ValidateToken(string? token, string? clientIp)
{
    if (!IsAuthenticationEnabled()) return true;   // ← allows everything, including a null token
```

So `IsAuthenticationEnabled()` answers **"enabled AND configured"**, and `ValidateToken` reads it as
**"enabled"**. Two very different states collapse into allow-all:

1. `Enabled == false` — the operator deliberately switched auth off. `true` is **correct** here and must
   keep working; this is the opt-out § SH-H037 requires to exist and be checked first.
2. `Enabled == true` with nothing configured — a **misconfiguration**, silently serving an open endpoint.

⚠ **The correct behaviour is already written and unreachable.** Further down the same method:

```csharp
_logger?.LogWarning("Authentication enabled but no tokens or bindings configured");
return false;
```

That branch cannot execute, because the top gate already returned `true` for exactly that state. So the
author's intent was fail-closed, and the defect is the gate standing in front of it — the
§ TASK-247 shape (a branch nobody can reach), here guarding an auth decision.

### ⚠ The filed trigger is the SAFE case — measured

The finding says *"A renamed `${VAR}` that leaves Tokens empty"*. Measured with a probe mirroring
`ExpandEnvironmentVariable`:

| config value | env state | expands to | dropped? | outcome |
|---|---|---|---|---|
| `${VAR}` | **never set / renamed** | `"${VAR}"` (the literal, via `?? value`) | **no** | auth stays enabled, every real token rejected — **fails CLOSED** |
| `${VAR}` | **set to empty** | `""` | **yes** | token set empties — **fails OPEN** |
| `${VAR}` | set to a value | the value | no | works |

A **renamed** variable is therefore harmless — `Environment.GetEnvironmentVariable` returns `null` and
`?? value` keeps the literal. The dangerous states are:

- **`Enabled: true` with empty `Tokens` and empty `TokenBindings`** — needs no environment variables at
  all, and is the plainest reachable form (a config section with the flag flipped and the list not yet
  filled in);
- **`${VAR}` where the variable exists and is blank** — `GetEnvironmentVariable` returns `""`, not
  `null`, so the `?? value` fallback never fires and `IsNullOrWhiteSpace` drops the token. Reachable from
  `docker -e VAR=`, an empty systemd `Environment=`, or a blank CI variable;
- every configured token being empty or whitespace in config.

**Why this correction matters rather than being pedantry:** anyone testing the finding as filed would set
a wrong variable name, observe a **401**, and conclude the report was a false positive.

### ⚠ Widened: there are TWO independent fail-open gates, not one

Measured across the framework — `AuthenticationService` is the shared engine behind four transports:

| Caller | Gate | Reached by |
|---|---|---|
| `Birko.Communication.REST.Server/Middleware/RestAuthenticationMiddleware.cs:24` | `ValidateToken` | every REST request |
| `Birko.Communication.WebSocket/Middleware/WebSocketMiddleware.cs:107` | `ValidateToken` | every WS handshake |
| `Birko.Communication.WebSocket/Middleware/WebSocketEndpointExtensions.cs:83` | `ValidateToken` | every WS handshake |
| **`Birko.Communication.SSE/Middleware/SseAuthenticationService.cs:94`** | **`IsAuthenticationEnabled()` directly** | every SSE connection |

The SSE one is a **second instance of the same root cause in a different file**: it returns
`SseAuthenticationResult.Success(...)` before it ever extracts a token. Fixing `ValidateToken` alone
would leave SSE fail-open, so both are in scope — § TASK-215's *guard the whole verb family or none of
it*, applied to an auth boundary. It carries no `SH-` id of its own; it is the same defect and is fixed
here rather than spawned, because shipping half a fix for an auth bypass is not a partial improvement.

### The existing suite does not encode the defect — the gap is a COMPOSITION

`Birko.Communication.WebSocket.Tests/WebSocketAuthenticationServiceTests.cs` already asserts both halves
separately and both are honest:

- `IsAuthenticationEnabled_False_WhenEnabledButNoTokens` → `false`. Truthful about what that method
  reports, and a **contract pin**: this fix must not change that return value, since five transports
  expose the method publicly.
- `ValidateToken_AllowsAny_WhenAuthenticationDisabled` → uses `enabled: false`, the legitimate opt-out.
  Must keep passing.

Nobody ever asserted `ValidateToken` with `enabled: true` **and** no tokens. The vulnerability lives in
the gap between two passing tests, which is why a green suite said nothing.

### Consumer reach

`AuthenticationService` is named in **6** consumer `.cs` files — the only area in this whole intake with
measured live consumer use of the type a finding names (the other 14 areas' types have 0). Combined with
reachability from an untrusted token, that is why this outranked the four other P0s.

### The fix, and the stricter option that was rejected

**Separate the two questions at one producer**, and have both gates consult it:

- `IsAuthenticationEnabled()` — **unchanged** (enabled AND configured). Public surface, pinned by a test.
- `IsAuthenticationDisabled` — `!_config.Enabled`, i.e. *the operator switched it off*. The opt-out, and
  the only thing a gate may treat as allow-all.
- `IsMisconfigured` — `_config.Enabled` with nothing configured. Recorded so a host can detect it, per the
  framework's report-rather-than-swallow convention (§ TASK-204/289), plus a `LogError` at construction.

⚠ **Rejected: throwing from the constructor.** It is the loudest option and § SH-H037 would permit it
(the opt-out exists and is checked first). Rejected on blast radius, per § TASK-256's inversion of the
same instinct: a consumer currently running `Enabled: true` with no tokens is running an *open* endpoint,
and throwing converts their running service into a start-up failure. Refusing at validate time closes the
hole without that, and the `LogError` + `IsMisconfigured` give an operator the signal. If a later
measurement shows no consumer is in that state, the throw becomes affordable — that is a separate call.

## Acceptance criteria

_Rewritten 2026-09-08 from the generic triage list, before any code was written, because re-verification
rescoped the finding (see above). The original list asked for confirm-or-refute; that is now done, and
these are the fix._

- [x] `SH-H040` marked **CONFIRMED, trigger corrected** in `SPEC-HARVEST-FINDINGS-2026-07-30.md`, naming
      the three reachable states and recording that a *renamed* variable fails **closed**
- [x] `Enabled: true` with **no tokens and no bindings** rejects every caller, including one presenting a
      null token — and the previously unreachable "enabled but no tokens or bindings configured" branch is
      the code that runs
- [x] `${VAR}` resolving to an **empty** variable rejects, rather than emptying the token set into
      allow-all
- [x] `Enabled: false` still allows everything, token or no token. **This is the opt-out and breaking it
      would be the worse defect** — asserted explicitly, not left to the pre-existing test
- [x] **The SSE gate is fixed too**: `SseAuthenticationService.AuthenticateConnection` no longer returns
      `Success` on a misconfiguration. Both gates read one producer, so a third caller cannot reintroduce
      the split
- [x] `IsAuthenticationEnabled()`'s return value is **unchanged** for all three of its states — pinned,
      because five transports expose it publicly
- [x] A misconfiguration is **detectable**: `IsMisconfigured` plus a `LogError` at construction. A guard
      that silently refuses everything is as hard to diagnose as one that silently allows everything
- [x] Tests in `Birko.Security.Tests` (the engine's own project, not a transport's), naming `SH-H040` and
      stating the mechanism
- [x] Proven able to fail: reverting the gate must red the new tests and leave the opt-out test green.
      Report the split as numbers and name the contract pins as pins, not as evidence
- [x] `/specs regen security-and-authorization`, with the spec diff reviewed as the fix's evidence

## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 38 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-165]] owns those.
- Low-severity findings in this area — [[TASK-176]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

**N/A — fully covered by automated tests, and a human pass would be strictly weaker here.**

Resolved at close (2026-09-08) rather than left as the placeholder. The reason, not just the verdict: the
whole behaviour is a boolean decision taken by a library method from a configuration object — no UI, no
rendering, no hardware. Every state is asserted directly: the three fail-open triggers, the opt-out, the
absent-variable contrast, both new properties across all three configuration states, and the SSE gate
end-to-end. 15 new tests in two projects, with two disjoint mutations proving they can fail (3 of 57 and
2 of 24).

A human could only re-run the same booleans by hand, and would have **no way to see** the case that
matters most: that `Enabled = true` with an empty token list now rejects rather than accepts. A service
refusing everything looks identical by eye to one that is merely misconfigured — which is exactly why
`IsMisconfigured` and the constructor's `LogError` exist, and both are asserted mechanically.

## Implementation plan

_Populated by `/tasks plan TASK-312` — leave empty until then._

## Outcome

**What was broken.** A Birko service configured with authentication switched **on** but with no usable
token accepted every caller, including one presenting no token at all. `ValidateToken` gated on
`IsAuthenticationEnabled()`, which answers *"enabled **and** configured"* — so a config section with the
flag set and the token list empty (or holding only a blank `${VAR}`) collapsed into the same allow-all
branch as a deliberate `Enabled = false`. Four transports share that engine, and a second, independent
gate in the SSE middleware granted `Success` before it ever looked for a token.

**The fix** separates the two questions at one producer. `IsAuthenticationDisabled` (`!_config.Enabled`)
is now the only state a gate may treat as allow-all; `IsMisconfigured` reports the bad state and the
constructor logs it at error level. Both gates read that producer, so a third caller cannot reintroduce
the split. `IsAuthenticationEnabled()` is untouched — five transport wrappers expose it publicly.

⚠ **The fix mostly made existing code reachable.** `ValidateToken` already ended with
`LogWarning("Authentication enabled but no tokens or bindings configured"); return false;` — dead code,
because the gate in front of it returned `true` for exactly that state. The author's intent was
fail-closed; the defect was the gate standing in front of it.

### Step 6 — proven able to fail, two disjoint mutations

| Mutation | Red |
|---|---|
| engine gate back to `!IsAuthenticationEnabled()` | **3 of 57** — `Enabled_WithNothingConfigured_RejectsEveryCaller`, `Enabled_WithOnlyWhitespaceTokens_RejectsEveryCaller`, `Enabled_WithAnEnvironmentVariableSetToBlank_RejectsEveryCaller` |
| SSE gate reverted, **engine left fixed** | **2 of 24** — `Enabled_WithNothingConfigured_RefusesTheConnection`, `..._RefusesEvenWhenATokenIsPresented` |

The second mutation is the one that matters for scope: with the engine already correct, SSE still failed
open. Widening past the filed finding was necessary, not tidy.

**Contract pins — green under both mutations, and pins rather than evidence:**
`Disabled_StillAllowsEveryCaller`, `Disabled_WithNothingConfigured_IsNotReportedAsMisconfigured`,
`IsAuthenticationEnabled_KeepsAllThreeOfItsAnswers`,
`Enabled_WithAnAbsentEnvironmentVariable_AlreadyFailedClosed`, and the SSE opt-out plus happy/invalid
pair. They prove nothing about the fix; they are what fails if someone "fixes" this differently and
breaks the opt-out or redefines the public method.

**Suites:** Security 57/57, SSE 24/24, WebSocket 33/33, REST.Server 7/7, SOAP 7/7 — **128 green, 0 failed.**

### Judgement calls, and the stricter option rejected

- **⚠ The finding's stated trigger was wrong, and it named the safe case.** It blamed *"a renamed
  `${VAR}`"*. Measured: an absent variable makes `GetEnvironmentVariable` return `null`, so
  `ExpandEnvironmentVariable`'s `?? value` keeps the literal `"${VAR}"` — non-blank, so it is retained,
  auth stays on, and every real token is refused. **That fails closed.** Anyone reproducing the finding as
  filed would have seen a 401 and closed it as a false positive. The real triggers are nothing configured
  at all, or a variable that exists and is *blank* (`""`, where the `??` never fires). Both are now tested,
  and the safe case is pinned as the contrast so nobody "fixes" the fallback into a fail-open.
- **Rejected: throwing from the constructor.** Loudest, and § SH-H037 would permit it since the opt-out
  exists and is checked first. Rejected on blast radius, per § TASK-256's inversion of the same instinct:
  a consumer currently in this state is running an *open* endpoint, and a throw converts their running
  service into a start-up failure. Refusing at validate time closes the hole; `LogError` +
  `IsMisconfigured` give the operator the signal. If a measurement later shows no consumer is in that
  state, the throw becomes affordable — a separate call, deliberately not made here.
- **Rejected: changing `IsAuthenticationEnabled()`.** It is honestly named for what it answers, five
  wrappers expose it, and an existing test pins all three of its values. Moving the *gate* was the smaller
  and safer change, and the pin above is what keeps it that way.
- **`IsMisconfigured` requires BOTH collections empty**, so an IP-bound-only deployment is not reported
  broken and refused. Tested.
- **Widened beyond the filed finding** to `SseAuthenticationService.AuthenticateConnection`, which carries
  no `SH-` id. Same root cause, different file; § TASK-215's *guard the whole verb family or none of it*.
  Shipping half a fix for an auth bypass is not a partial improvement.

### Flagged, not fixed

- **⚠ No spec area covers ANY of the four transports.** Measured: `docs/specs/.map.yml` has **0** globs
  reaching `Birko.Communication.SSE`, `.WebSocket`, `.REST.Server` or `.SOAP`, and **0** of
  `docs/specs/*.md` mention `SseAuthenticationService`. So the SSE half of this fix produced **no spec
  diff** — the evidence this epic normally relies on did not exist, and the regression tests carried it
  alone. Appended as the **third instance** to [[TASK-142]] (*"The spec map silently under-covers, and
  nothing detects it"*), which already records two, rather than spawned as a duplicate. The measurement is
  what upgrades that task from tidiness to a security-coverage gap: the spec layer describes the shared
  engine and none of the four boundaries that call it.
- The engine's pre-existing tests live in `Birko.Communication.WebSocket.Tests` rather than
  `Birko.Security.Tests`. Not moved — moving another suite's tests is churn unrelated to this defect — but
  the new engine tests were put in `Birko.Security.Tests`, where they belong.

## Progress log

- step 2 — picked; ranked above TASK-311 because auth bypass outranks cross-tenant leakage on key 1, and
  it also wins keys 2 (reachable from an untrusted token; `AuthenticationService` named in 6 consumer
  `.cs` files, the only measured live reach in this intake), 3 (fails *open*, so a plausible valid answer
  rather than a throw) and 4 (one finding, one method, no open design question). Key 6 inert — no story in
  this tree declares `theme:`, so all 83 pool candidates are undeclared.
- step 3 — verified: **rescoped**. Mechanism holds; the filed trigger (a *renamed* `${VAR}`) is the SAFE case — measured, it falls back to the literal and fails CLOSED. Real triggers: `Enabled: true` with nothing configured, or a `${VAR}` whose variable is set-but-empty. Widened to a **second** fail-open gate sharing the root cause (`SseAuthenticationService.cs:94`). Context and acceptance criteria rewritten before any code.
- step 4 — layer: **local**. Root cause is in `Birko.Security`'s own `AuthenticationService`, not in a dependency; the four transports are consumers of the defect, not causes of it.
- step 5 — fix in `Birko.Security/Authentication/AuthenticationService.cs` (gate + `IsAuthenticationDisabled` + `IsMisconfigured` + constructor `LogError`) and `Birko.Communication.SSE/Middleware/SseAuthenticationService.cs` (second gate); tests in `Birko.Security.Tests/AuthenticationMisconfigurationFailsClosedTests.cs` (10 new) and `Birko.Communication.SSE.Tests/SseAuthenticationMisconfigurationFailsClosedTests.cs` (5 new). Suites: Security 57/57, SSE 24/24, WebSocket 33/33, REST.Server 7/7, SOAP 7/7 — **128 green, 0 failed**.
- step 6 — two disjoint mutations. **A** (engine gate back to `!IsAuthenticationEnabled()`): **3 of 57** red — `Enabled_WithNothingConfigured_RejectsEveryCaller`, `Enabled_WithOnlyWhitespaceTokens_RejectsEveryCaller`, `Enabled_WithAnEnvironmentVariableSetToBlank_RejectsEveryCaller`. **B** (SSE gate reverted, engine left fixed): **2 of 24** red — `Enabled_WithNothingConfigured_RefusesTheConnection`, `..._RefusesEvenWhenATokenIsPresented`, which proves the SSE gate is an *independent* hole rather than a tidy-up. Contract pins that stayed green under both, and are **pins not evidence**: `Disabled_StillAllowsEveryCaller`, `Disabled_WithNothingConfigured_IsNotReportedAsMisconfigured`, `IsAuthenticationEnabled_KeepsAllThreeOfItsAnswers`, `Enabled_WithAnAbsentEnvironmentVariable_AlreadyFailedClosed`, and the SSE opt-out/happy-path trio.
- step 7 — respecced `security-and-authorization`; requirement retitled *"Static-token authentication allows everything only when deliberately switched off"* (the old title asserted the defect), its SHALL rewritten, the *"Enabled but nothing configured → returns true"* scenario inverted, and two scenarios added for the blank-variable trigger and the absent-variable contrast. ⚠ The SSE half produced **no** spec diff — no area covers any of the four transports; recorded on [[TASK-142]].
