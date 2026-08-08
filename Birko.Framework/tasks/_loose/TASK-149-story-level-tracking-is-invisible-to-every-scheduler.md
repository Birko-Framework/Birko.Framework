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

## Acceptance criteria

- [ ] **Decide** what a container tracking work in its own body must show. Options include: requiring at
      least one task once a story leaves `planned`; rendering `—` or a body-tracked marker instead of
      `0/0`; or a `tracked-in-body: true` frontmatter flag the rollup honours. Record why the rejected
      options were rejected
- [ ] The rollup line for such a container is unambiguous — a reader must be able to tell "no work
      recorded" from "work recorded, not as tasks" **without opening the file**. That distinction is the
      whole defect
- [ ] `/tasks audit` (or `triage`) flags the case, joining its existing parent-vs-children contradiction
      checks; and DV12's non-firing is addressed — establish whether it is unrun, unsurfaced, or would not
      have matched, exactly as [[TASK-142]] asks of the spec map's check
- [ ] Run the resulting check once across the whole tree and record what it finds. EPIC-017 was found by
      accident; nothing says it is the only one
- [ ] The rule permits legitimate parent-level tracking rather than outlawing it — a check that forces
      decomposition of finished work would produce exactly the transcript tasks the skill forbids
- [ ] Whatever is decided is recorded where the next person tracking at story level will meet it

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
