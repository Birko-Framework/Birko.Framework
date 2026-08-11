---
id: TASK-201
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-11
depends-on: [TASK-199]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Reps: declare `idPinned` on the client-minted creates — and not on the one that must not have it

## Context

Consumer-side follow-up to [[TASK-199]]. **The work is in the Reps repo**
(`C:\Source\Birko\Consumers\WorkoutTracker`). Hand-off prompt: `PROMPT-201-reps-idpinned.md`.

Reps already does the hard part: it mints entity guids client-side (`Reps.Web/src/api.ts:1412`, `1912` —
*"client-minted so an offline replay lands under the id already shown"*) and its server detects a clash
(`MapOwnedCrud` + `RequestGuid` → `CreateClash`, plus the hand-rolled equivalents in `PlanEndpoints.cs:64`
and `PhaseEndpoints.cs:48`). So Reps **cannot** duplicate a create on replay.

What it gets instead is the false conflict [[TASK-199]] fixed — but only for writes that **declare**
`idPinned`. Until Reps sets it, a replayed create still reports a conflict for a write that succeeded and
its outbox entry still wedges. The framework fix is inert here without the one-line-per-call-site opt-in.

**The trap, and the reason this is a task rather than a sweep:** `idPinned` asserts that the endpoint's
conflict status has **exactly one meaning**. Reps' own suite documents a counter-example —
`PlanHierarchyCrudTests` records **"slot uniqueness → 409"** on a POST that also carries a client guid.
Setting `idPinned` there would drain a genuinely rejected write and report it as saved. So this is
per-endpoint judgement, not a find-and-replace over every `api.post` with a guid in the body.

## Approach

For each queued `POST` in `Reps.Web/src/api.ts`, decide whether its endpoint's 409 can mean anything other
than "this id already exists", and set `idPinned: true` in the meta only where it cannot. Candidates:

| Call site | Server | `idPinned`? |
|---|---|---|
| `api.post('body-measurements', { guid, … })` (`api.ts:1913`) | `MapOwnedCrud` + `RequestGuid` | likely **yes** — verify 409 has no other source |
| `api.post(path, { guid, … })` plan hierarchy (`api.ts:1413`) | hand-rolled clash **plus** slot uniqueness | **no** for slots; per-resource elsewhere |
| `api.post('steps', { date, steps })` (`api.ts:1946`) | `steps.Upsert(...)` by date → 200 | **not needed** — never 409s |

The `DELETE` half of [[TASK-199]] needs no opt-in and is already live for Reps: the four queued deletes
(`sets/{id}`, `sessions/{id}`, `sessions/{id}/exercises/{id}`, `schedule-periods/{guid}`) stop being
re-sent forever once their row is gone.

Worth confirming while there: `RequireOwned` answers **404 for a foreign row** rather than disclosing it, so
a queued delete against someone else's entity now drains rather than retrying. That is deliberate and
documented in `deleteMissingIsApplied`, but Reps should decide it is acceptable rather than inherit it.

## Acceptance criteria

- [ ] Every queued `POST` classified: `idPinned` set, or explicitly reasoned as not eligible
- [ ] The slot-uniqueness POST (or any endpoint whose 409 is overloaded) is **not** pinned, with a comment
      saying why so it is not "fixed" later by someone reasoning from symmetry
- [ ] A replayed pinned create drains with no conflict raised to the user
- [ ] A replayed queued delete of an already-gone row drains
- [ ] Regression coverage in Reps' own suite, red-verified
- [ ] The foreign-row 404 drain is confirmed acceptable, or `deleteMissingIsApplied: false` is set with a
      reason

## Out of scope

- The framework classification — shipped in [[TASK-199]].
- Any change to the server's strict-REST 409, which is deliberate and pinned by
  `ExerciseCrudTests:90` (`// create-only: never a silent update`).

## Human test plan

- [ ] Log a body measurement offline → go online → confirm no conflict prompt and the badge reaches zero.
- [ ] Delete a set offline, delete the same set from another device, sync → confirm the entry drains
      instead of retrying every 30s.
