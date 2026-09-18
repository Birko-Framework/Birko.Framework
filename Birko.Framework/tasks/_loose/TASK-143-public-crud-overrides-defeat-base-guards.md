---
id: TASK-143
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

# Stores that override public CRUD instead of `*Core` defeat every base-class guard

## Context

Filed from [[TASK-109]]'s Outcome, where the contained fix was chosen deliberately and this was named as
the correct one, needing its own decision.

`CLAUDE.md` § Conventions is explicit: *"Concrete stores override `protected *Core` methods, **NOT** the
public CRUD methods. The base class handles lazy-init in the public wrapper."* The reason is exactly this
task — a public override bypasses whatever the base wrapper does, including guards added later.

**Measured during TASK-109: 10 such overrides across 3 backends.**

| Backend | Overrides | State |
|---|---|---|
| ElasticSearch | 4 | already covered by `ParseRequiredFilterQuery` (CR-H047) — a different mechanism at the same boundary |
| InMemory | 2 | had to **repeat** TASK-109's null-filter guard verbatim |
| MongoDB | 4 | same repeat |

They predate the guard, so nobody introduced them knowingly. But the cost is now concrete: every future
base-class invariant must be hand-copied into ten places, and the copy is invisible to whoever adds the
next invariant. [[TASK-141]] exists *only* because one of those copies is untested — which is the failure
mode arriving already.

The counter-argument is real and belongs in the decision: converting ten methods across three backends is
a behaviour change to shipped stores, and the public overrides may be doing something the `*Core` seam
cannot express. That has not been checked; checking it is part of this task, not an assumption to make now.

## Acceptance criteria

- [ ] For each of the 10 overrides, establish whether its behaviour is expressible through `*Core` at all
- [ ] **Decide** — convert, or amend the convention to acknowledge the exception — with the reasoning
      recorded either way
- [ ] If converting: each backend's suite proves behaviour is unchanged, and the repeated guards are
      **removed** rather than left as dead defence in depth
- [ ] If not converting: `CLAUDE.md` § Conventions says so explicitly and names the obligation it creates
      — "a new base-class invariant must be replicated in these 10 methods" — with the list
- [ ] A mechanism exists to notice the *next* public override: a convention check, a test, or a documented
      review step. The current answer is "someone remembers", which is what produced this task
- [ ] `CLAUDE.md` is updated in the same change, whichever way the decision goes

## Out of scope

- The specific null-filter guard those overrides repeat ([[TASK-109]], closed) and its missing MongoDB
  test ([[TASK-141]]).
- Backends that do not override the public methods.

## Human test plan

- [ ] If converted, exercise one CRUD path per affected backend and confirm lazy-init still runs before
      the first operation. That is what the public wrapper exists to guarantee and is precisely what a
      botched conversion drops silently — the store still works, just uninitialised on first use.

## Implementation plan

_Populated by `/tasks plan TASK-143` — leave empty until then._
