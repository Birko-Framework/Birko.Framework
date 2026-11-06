---
id: TASK-176
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: in-progress
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L217, SH-L218, SH-L219, SH-L220, SH-L221, SH-L222, SH-L223, SH-L224, SH-L225, SH-L226, SH-L227, SH-L228, SH-L229, SH-L230, SH-L231, SH-L232, SH-L233, SH-L234, SH-L235, SH-L236, SH-L237, SH-L238, SH-L239]
pr: null
github-issue: null
jira-key: null
---

# Triage the 23 low spec-harvest findings in `security-and-authorization`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **23** findings
that fall in the `security-and-authorization` area.

**Findings:** `SH-L217`–`SH-L239` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: security-and-authorization`, lines 4344–4483.

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

- [ ] All 23 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen security-and-authorization`, with the spec diff reviewed as the change's
      evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [ ] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 364 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-165]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

Cannot be written yet: which steps a human adds depends on which findings survive triage. **Resolve this
section before `/tasks close`** — an absent plan is not an `N/A` one, and defaulting it parks the task on a
step that may not exist (SKILL.md § Lifecycle). Expected outcome for this area is
`N/A — fully covered by automated tests`, since it is a library contract with no UI surface; write that
explicitly with its reason rather than leaving the section as-is.

## Implementation plan

_Populated by `/tasks plan TASK-176` — leave empty until then._

## Progress — verified 2026-11-06

All 23 checked against the code: **every one holds**; three are narrower than written (SH-L217: the option IS read,
by `TenantHeaderClaimGuardMiddleware`, and ignored by the endpoint filter, the permission checker and owner access;
SH-L225: the PBKDF2 half was already fixed by SH-M246, BCrypt's is not; SH-L226: the verify-time ceiling exists, the
missing piece is `NeedsRehash`). Judgement calls made and recorded (no consumer is affected; the user can overrule):
the wildcard option is honoured by every ASP.NET Core check and `AuthorizationContext` accepts `"*"`; the token
adapter writes the `JwtClaimNames` (`sub`, `email`), as Symbio's own issuer does; a JWT secret under 32 characters is
refused at startup (Symbio's are 44-45); `NeedsRehash` and key generation join their interfaces (only framework
classes implement them; Symbio only registers the PBKDF2 hasher).

Fixed in groups by root cause, each one commit with its tests, red-verified against the code before the fix:

- **JWT registration** (SH-L221, SH-L222, SH-L227, SH-L228): `Task176JwtRegistrationTests`, 6 of 7 red before the fix;
  `A_32_character_secret_is_accepted` passes either way (a contract pin).
- **Authorization** (SH-L217, SH-L219, SH-L229, SH-L235): `Task176AuthorizationTests`, 7 of 9 red with each fix
  disabled in place (the old code cannot compile against the new constructor parameter); the wildcard-on default and
  the authenticated-caller grant pass either way (contract pins).
- **Token adapter** (SH-L218, SH-L236, SH-L237): `Task176TokenAdapterTests`, 6 of 8 red against the original adapter;
  the round trip and the bearer handler's inbound mapping pass either way (contract pins).
- **Static tokens and dispose** (SH-L220, SH-L238): `Task176StaticTokenTests`, **all 10 are contract pins** (green
  before and after): comparison timing is not observable in a unit test, and a double `ReaderWriterLockSlim.Dispose` was
  already harmless. They pin that the fixed-time comparison decides exactly as `==` did.
