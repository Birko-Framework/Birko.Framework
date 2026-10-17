---
id: TASK-641
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A stored grant whose role was later retired makes permission checks for that user and type throw

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `security-and-authorization`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`GrantService.EffectiveActionsAsync` (`Birko.Security.Grants/Services/GrantService.cs:245`) and `ListResourcesAsync`
(`:56`) throw `GrantConfigurationException` on a grant whose role is no longer in the role table. So one such grant makes
every `CanAsync`, `ListResourcesAsync` and non-admin `AuthorizeAsync` for that user on that resource type throw. Only the
admin path was made tolerant of retired roles.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Decide what a retired role means for a check (ignore the grant, or refuse loudly at role retirement)
- [x] Red on the current code; the spec `docs/specs/security-and-authorization.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced, and partly by design.** The throw was a documented rule ("Undeclared names refuse … including a stored
grant whose role was later removed from the table"), pinned by `CanAsyncTests.StoredGrantWithUndeclaredRole_Refuses`.
What this task added is the measured blast radius. For one user with a retired-role grant on Presentation A, and
`owner` on B:
- `CanAsync` on A threw.
- `ListResourcesAsync` for Presentation threw, so the listing of the whole type broke.
- With a type-wide retired grant, `CanAsync` on every Presentation threw, including B.
- `CanAsync` on B with an exact retired grant, on another type, and a share on B all worked.

`GrantAwareUserPermissionResolver` (skip, "throwing here would 500 every endpoint") and invitations (reported as
`Invitation.Invalid`) had already chosen not to throw.

**Decision (user, 2026-10-17): deny, and be loud elsewhere.** A stored grant whose role is no longer declared grants
nothing, which fails closed. `GrantService.Held(role)` resolves a stored role through the new
`GrantRoleTable.IsRole`, in `EffectiveActionsAsync`, the listing filter, the role replaced by `GrantAsync`, the role
held in `GrantAtLeastAsync` and the role removed by `RevokeAsync`. So an owner with `share` may now replace or remove
a stale grant: it needs no actions, because it grants none. A role a caller passes in is still refused. The loud half
is `StaleGrantDetector.FindAsync(store, table)`. It lists grants whose role (`RoleNotDeclared`) or resource type
(`ResourceTypeNotDeclared`) is undeclared, and leaves out the reserved admin grant. The health check over it needs
a new `Birko.Health.Security.Grants` project, so it was spawned as [[TASK-654]] (STORY-062, which reopened along with
EPIC-020).

**Red check:** with the old throwing resolution restored in `Held` and in the listing filter, 6 tests went red,
including the inverted pin. Two stayed green: the caller-role refusal (a pin) and the detector (new). Tests:
`Task641RetiredRoleTests` (7). `StoredGrantWithUndeclaredRole_Refuses` was inverted, not deleted (rule 56): it is now
`StoredGrantWithUndeclaredRole_GrantsNothing_AndDoesNotThrow`.

**Regression:** the two importers, `Birko.Security.Grants.Tests` (184, the AspNetCore tests included) and
`Birko.Security.Grants.SQL.Tests` (24, live), are green.

**Docs:** the spec `security-and-authorization` (the role-table and authorization requirements, a new scenario),
`docs/security.md`, `Birko.Security.Grants/CLAUDE.md` (the rule rewritten), and Recent Updates (the oldest entry
rolled).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-641` — leave empty until then._
