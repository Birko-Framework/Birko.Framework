---
id: TASK-250
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-227, TASK-131]
findings: []
pr: "project-lifecycle-skills db652cd"
github-issue: null
jira-key: null
---

> **Renumbered from TASK-240 to TASK-250 on 2026-08-18.** Two different tasks were filed as TASK-240 on
> 2026-08-17 — this one and
> [A transaction boundary that async writes actually honour](TASK-240-a-transaction-boundary-async-writes-honour.md).
> ID allocation takes `max + 1`, so the collision could not self-heal and every `[[TASK-240]]` reference was
> ambiguous. The transaction task keeps the original id because it is the one cited by CLAUDE.md § Conventions
> (five times) and by [[TASK-241]], which was spawned from it; this one had a single referrer, [[TASK-227]],
> now updated. Found by `/tasks triage` — an incremental dashboard patch cannot see a duplicate id, which is
> also why the task counts had drifted by two.

# A spec source glob is not a git pathspec, so the staleness check never saw 124 files

## Context

Found while fixing [[TASK-227]], by running its worked example in a scratch repo instead of reasoning about
it. Third defect in the same staleness check, after [[TASK-131]] (external sources invisible) and TASK-227
(anchor too early) — and like both of those, it made the check answer confidently and wrongly.

`.map.yml` sources are written as globs (`../Birko.Data.SQL/**/*.cs`). `verify` passed them straight to
`git diff --name-only <sha>..HEAD -- <glob>`. **A git pathspec is not a glob unless you say `:(glob)`** — by
default `*` crosses `/`, so `X/**/*.cs` requires a literal `/` after `X/` and does not match a file sitting
directly in `X/`:

```
src/Stores/Store.cs    matched by  src/**/*.cs          ✓   nested — looks like it works
src/RedisCache.cs      matched by  src/**/*.cs          ✗   top level — invisible
src/RedisCache.cs      matched by  :(glob)src/**/*.cs   ✓
```

**Partial blindness is worse than total blindness.** TASK-131's defect reported *fresh forever*, which is at
least uniformly wrong. This one reports drift for nested files and nothing for root-level ones, so the check
looks alive while missing a whole class of change.

**Measured against this repo's map: 47 of 74 source globs point at a project with `.cs` files at its root —
124 files the staleness check could never see.** Among them `Birko.BackgroundJobs.*` (2–3 each),
`Birko.Caching.Hybrid`, `Birko.AI.Contracts`. Root-level files are not an edge case in this framework; a
small `Birko.X` project often keeps its whole surface there (`Birko.Redis` is two files, both at the root).

## Acceptance criteria

- [x] `verify`'s in-repo diff and its external-repo diff both pass `:(glob)`
- [x] The reason is written down where the next reader meets it, with the three-line matched/not-matched
      table — a rule with no example gets "simplified" away
- [x] Measured rather than asserted: the counts above come from walking the map, not from reading git docs

## Out of scope

- The anchor — [[TASK-227]], fixed in the same commit.
- Rewriting `.map.yml`'s glob syntax. The globs are correct as globs; it is the *consumer* that was passing
  them to an API with different matching rules.

## Human test plan

N/A — reproduced in a scratch repo; `git diff -- 'src/**/*.cs'` versus `-- ':(glob)src/**/*.cs'` against a
top-level file is the whole test.

## Outcome

Fixed with TASK-227 in `project-lifecycle-skills` db652cd.

**The lesson is where it was found.** TASK-227's worked example was written from reasoning and was wrong —
`git diff c1..HEAD` excludes c1's own changes, so the sequence as first drafted did not demonstrate the
defect at all. Running it exposed both that (the defect needs an *uncommitted* source change at harvest
time) and this pathspec bug, which no amount of re-reading the skill would have surfaced. **A worked example
is worth writing precisely because you have to run it.**
