---
id: TASK-149
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: human
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A story that tracks work without task files is invisible to every scheduler

## Context

Filed 2026-08-08 from the EPIC-017 episode, which showed the failure in **both** directions at once.

`EPIC-017 tenant-isolation-hardening` holds three stories and, until [[TASK-148]], zero task files.
STORY-044 and STORY-045 are `done` with their shipped work described in the story bodies; STORY-046 is
in-progress with most of its work landed and one outstanding bullet. Consequences:

- **Real, completed work read as untouched.** The dashboard renders the epic as
  `in-progress (0/0 tasks done)` — indistinguishable from an epic nobody has started. Acting on that
  reading, I reported a fixed fail-open tenant-isolation defect to the user as unscheduled and severe.
  The story bodies said otherwise; nothing in the *rollup* did.
- **A real gap sat where nothing could pick it.** STORY-046's "Not done yet" bullet
  (`ScopeRestorationBehavior`) was genuinely outstanding for three weeks. Only `status: todo` **tasks**
  are ranked by `pick`, by the `Next up` snapshot, or by `fix-next`, so a bullet under a story is
  scheduled by nobody. That is the skill's own rule — *"a checklist line is filed, not scheduled"* — and
  it was violated at the story level rather than inside a task, where the rule is usually applied.

**This is not "every story must have tasks".** The tasks skill explicitly permits tracking small work at
the parent level: *"Small conversational fixes can skip the per-change task and track at the parent EPIC
level."* STORY-044/045 are not small, but they are genuinely finished and backfilling tasks onto them now
would write a transcript rather than a target — also explicitly forbidden. So the answer is **not** a rule
that every story is decomposed; it is that a container tracking work in its own body must not *render* as
though it holds none.

**The sharpest part: a check for this already exists and did not run.** [[roadmap]]'s divergence rule
**DV12** flags "a review-intake epic whose findings were filed but never scheduled as tasks" — EPIC-017 is
`kind: review-intake` and had zero tasks, so DV12 is exactly the rule that should have fired. It did not,
because nothing ran `/roadmap`. That is the same shape as [[TASK-142]] (the spec map's unmapped-sources
check exists and does not run), and the two should probably be answered together rather than each growing
its own reminder.

**A third occurrence, on the same day, split the thesis in two — and it is already fixed.** Clearing the
review backlog turned up three tasks (TASK-104 / TASK-105 / TASK-107) parked at `review` for weeks with
**no `## Human test plan` section at all**. `/tasks close` step 5 *was* run every time and *did* have a
rule for this family — it warned when the plan held the template's placeholder text — but it had no arm
for a section that is **absent**, so the absence silently defaulted to `review`. Fixed outright in the
skills repo (`project-lifecycle-skills@4be1bad`): an absent section now stops the close until it is
resolved, and the rule is stated beside the review-vs-done semantics in `SKILL.md` as well as in the verb.

That is **not** the same failure as DV12 or the spec map, and the difference is the useful part:

| | Failure | Remedy |
|---|---|---|
| DV12 (this task), spec-map check ([[TASK-142]]) | the check is correct and **nothing invokes it** | wire it to something that runs |
| `close` step 5 (fixed) | the check **runs every time** and had a blind spot | widen the rule |

So "wire the unrun checks to something that runs them" is only half an answer. The other half is asking,
of each check that *does* run, whether it actually covers the case it appears to. A gate that fires
faithfully on the wrong condition is more dangerous than one nobody runs, because its green is believed.
The third instance is recorded here as **evidence and a lesson, not as scope** — nothing about it is
outstanding, and it must not be re-opened as a criterion below.

## Acceptance criteria

- [ ] **Decide** what a container tracking work in its own body must show. Options include: requiring at
      least one task once a story leaves `planned`; rendering `—` or a body-tracked marker instead of
      `0/0`; or a `tracked-in-body: true` frontmatter flag the rollup honours. Record why the rejected
      options were rejected
- [ ] The rollup line for such a container is unambiguous — a reader must be able to tell "no work
      recorded" from "work recorded, not as tasks" **without opening the file**. That distinction is the
      whole defect
- [ ] ⚠ HALF MET (2026-08-08) — DV12 is now invoked (see below); the `audit`/`triage` half is not. `/tasks audit` (or `triage`) flags the case, joining its existing parent-vs-children contradiction
      checks; and DV12's non-firing is addressed — establish whether it is unrun, unsurfaced, or would not
      have matched, exactly as [[TASK-142]] asks of the spec map's check
- [x] Run the resulting check once across the whole tree and record what it finds. EPIC-017 was found by
      accident; nothing says it is the only one
- [x] Whatever mechanism is chosen, state **which of the two failure modes it addresses** (see the table
      above) and confirm it is not assumed to cover the other. A check wired to run is still worthless on
      a condition it does not match — `close` step 5 ran faithfully every time and missed three tasks for
      weeks. Deciding "we will run the checks" without asking what each one actually matches would leave
      this task's own lesson unapplied
- [ ] The rule permits legitimate parent-level tracking rather than outlawing it — a check that forces
      decomposition of finished work would produce exactly the transcript tasks the skill forbids
- [x] Whatever is decided is recorded where the next person tracking at story level will meet it — for the DV12 half, in `fix-next/SKILL.md` step 1

## Progress (2026-08-08) — the DV12 half is done; the rendering question is NOT

**Decision taken (criterion 3, second half): `fix-next` invokes DV12 when building its pool.** It already
walks every `review-intake` epic, so evaluating DV12 over the same epics is nearly free, and it puts the
check in the verb that runs most often *and* that would act on the answer. Shipped in
`project-lifecycle-skills@e560f99`.

**Criterion 4 — ran DV12 across the whole tree, and the result changed the design.** 4 hits:
STORY-046 (genuine — now [[TASK-148]]), and STORY-053 / STORY-054 / STORY-055, each holding a findings
list. **Three of the four were false positives**: those stories say in their bodies *"Not pre-created.
Extract on demand, one task per `SH-Mxxx`"* — a recorded decision, not an unscheduled backlog, and DV12's
condition cannot tell the two apart. Shipping the wiring unamended would have produced three spurious
reports on **every** run; a check that nags about a decision somebody already made gets muted, and a muted
check is worth exactly what an unrun one is. Amended in `@222b453` to check for the declaration first.

That is criterion 5 arriving in practice rather than in theory, and it lands on this task's own thesis:
**the third failure mode is a check that runs, matches, and is right to be ignored.** Prose-matching is
itself a guess, so a machine-readable "decomposed on demand" marker is the better answer — and choosing
one is part of the rendering decision still outstanding below, not separate from it.

**Still open — and it is the part this task is named for.** Criteria 1, 2 and 6 are the *rendering*
question: what a container tracking work in its own body must display, so that "no work recorded" and
"work recorded, not as tasks" are distinguishable without opening the file. Nothing about that was
decided. EPIC-017 still demonstrates it: it reads `0/1 tasks done` today only because [[TASK-148]] was
filed, and any other story tracking in-body would still render `0/0`.

## Out of scope

- Backfilling tasks onto STORY-044 / STORY-045 — they are `done`; see above for why that is the wrong
  remedy, not merely unnecessary.
- [[TASK-148]] itself, which is the one genuinely outstanding item and is now filed.
- The spec map's equivalent unrun-check problem ([[TASK-142]]) — related, and likely worth solving in the
  same pass, but a different mechanism.

## Human test plan

- [ ] Take EPIC-017 as the fixture: with the change in place, confirm its rollup no longer reads as
      untouched work. Then take a genuinely empty planned epic and confirm it still does — the check is
      worthless if it cannot tell those two apart, which is the failure being fixed.

## Implementation plan

_Populated by `/tasks plan TASK-149` — leave empty until then._

## Measured 2026-08-18 — the scale, and the half that is NOT this defect

A full regeneration of the dashboard (`/tasks triage`) found **18 of the 56 stories hold zero task files**,
so each renders `(0/0)`. That number is *not* 18 instances of this task: the stories split cleanly into two
tracking models, and only one of them is the defect described above.

**Four are deliberate findings pools, and they are working as designed** — EPIC-014's severity stories
STORY-024 / 025 / 026 / 027. Each carries an explicit `## Tasks` section reading *"**Not pre-created.**
Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per `CR-Cxx` entry"*. The
schedulable pool is the audit document and tasks are spawned as they are picked, which is the same shape
[[intake]]/`fix-next` formalise. A `(0/0)` render there means "nothing currently extracted", not "nothing
tracked", and the audit doc flips each finding's `Status` as it lands. **Do not decompose these** — pre-creating
~200 finding tasks is exactly the transcript-not-target outcome this task's own Context forbids. (STORY-042,
the Docker-gated deferred pile, is `planned` and most likely the same model; its wording differs so it was not
matched mechanically.)

**Thirteen are the actual instance:** `done` stories whose shipped work exists only in the story body —
the Xaml build-out STORY-029 … STORY-038, STORY-043, and STORY-044 / STORY-045, the pair this task was
filed from. These are the ones that read as untouched while describing completed work, and backfilling
tasks onto them now is forbidden for the reason already stated.

**So the fix is narrower than the raw count suggests, and it is a rendering fix, not a decomposition one.**
A container must not render as holding no work when it either (a) tracks completed work in its body or
(b) points at an external pool. Both want a marker the dashboard can read — the same `kind:`/pool-marker
idea [[TASK-208]]-style prose cannot supply, noted in the Context above as a [[roadmap]] change rather than
an improvised one.

**The distinction matters more than the count.** Reporting all 18 as untracked work would have recommended
decomposing four stories whose no-decomposition policy is written into them — turning a correct design into
~200 stub tasks.
