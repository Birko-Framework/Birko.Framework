---
id: TASK-201
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
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

## Outcome — done in the Reps repo, 2026-08-11/12

Landed as **Reps `c00cf64`**, then narrowed by **`a135ba8`** (TASK-167, below). Tracked consumer-side as **Reps
TASK-166**, which holds the verified per-endpoint table and the red-verify records. This file stays at `review`
for the same reason that one does: the manual steps below are unrun.

**Final shape: two endpoints declare `idPinned` — `POST /exercises` and `POST /body-measurements`.** Of fifteen
queued `POST`s, seven never answer 409 at all (they upsert) and the plan tree turned out not to be queueable
once it was fixed to honour its own decision.

Four corrections to this task's own write-up, worth carrying into how the next hand-off is framed:

1. **The candidate table was wrong in two of three rows.** `POST /schedule-periods` is listed as a
   `MapOwnedCrud` clash route; it is not mapped through the skeleton at all and its `POST` is an
   upsert-by-pinned-id that always answers 200, so the flag is inert there. And it under-counted: there are
   **15** queued `POST`s, not three.
2. **"Reported to the user as a conflict" is not true of Reps.** It registers no `onConflict` listener and its
   sync chip counts `getPending()`, which excludes `conflict` rows. Nothing was ever shown to anyone. The real
   cost was a silently unremovable outbox row, which makes this latent rather than user-visible — and means the
   *visible* half of TASK-199 was the `DELETE` retry loop, which needed no opt-in.
3. **The excluded endpoint could not be excluded at the call site.** `POST /slots` shared `createEntity` with
   five resources that should be pinned, so the verdict first had to become a required argument. A hand-off
   saying "set one field in the meta object" would have produced either an unsafe sweep or five missed
   endpoints.
4. **…and then the excluded endpoint stopped being reachable at all, which is the finding worth having.**
   Classifying the queued writes exposed that plan-tree writes were **queueable against their own documented
   decision**: they are online-only, but that was enforced by a `navigator.onLine` check, and `ApiClient` also
   queues whenever the *fetch fails* (`status === 0`). Fixing that (Reps TASK-167 — withhold `meta`, since
   `_sendWrite` queues only `if (meta)`) removed the last path by which Reps can produce a `conflict` row, so
   the required argument became dead code and was deleted a day after it shipped.

**The lesson for the framework, not just for Reps:** `idPinned` is only meaningful for writes that are actually
queueable, and "is this queueable?" turned out to be the question nobody had asked. A consumer can hold a
documented no-queue decision and still queue, because an `onLine` guard cannot cover the failed-fetch path —
and `ActionMetadata.idPinned`'s doc says when *not* to set the flag but not "check first whether this write can
reach the outbox at all". Worth a sentence there, and worth asking of the other consumer (TASK-200/Symbio)
before it adopts the flag: **a write that must never queue is protected by withholding `meta`, never by
`navigator.onLine`.**

Also spawned in Reps from the review pass: **TASK-167** (done), **TASK-168** (a *genuine* conflict has nowhere
to go — the other half of this defect; now `blocked`, since TASK-167 made the state unreachable), **TASK-169**
(TASK-160's regression test could not fail — all four instances passed with the fix reverted) and **TASK-170**.

## Human test plan

- [ ] Log a body measurement offline → go online → the reading appears once and the sync chip reaches zero.
      (Note: there is no conflict prompt to look for — Reps renders nothing on a conflict. The outbox is the
      real observable and only the automated specs can read it.)
- [ ] Delete a set offline, delete the same set from another device, sync → confirm the entry drains
      instead of retrying every 30s.
