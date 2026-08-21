---
id: TASK-267
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The project-local `verify-conventions` did not run at the close gate, again

## Context — observed live during TASK-257's close

This repo ships `.claude/skills/verify-conventions/SKILL.md`, whose own header says:

> Shadows the generic `verify-conventions` — and therefore must EXTEND it, not replace it. Project-local
> skills win by *name* inside their repo, so this file is what `/tasks close` step 5b and `/fix-next`
> step 8 actually load here. **It was previously called `verify-birko-conventions`, which shadowed
> nothing** — those gates silently ran the generic lint and none of the concrete checks below.

That rename was the fix for the first instance. **It did not work.** Invoking the skill during TASK-257's
close gate loaded the generic skill from `C:\Users\FinStat\.claude\skills\verify-conventions` — the
project-local file was not used. The concrete Birko checks (nullable-warning regressions, `*Core` override
violations, missing tests for new public surface, `$(BirkoSrc)` paths, `RemoteSettings` construction,
`Recent Updates` entries, `.slnx` / `.code-workspace` / `.csproj` registration, doc-index membership) were
therefore **not** run by the gate. They were only run because the closer noticed and executed them by hand
— and one of them (check 5, missing tests for new public surface) found a genuine miss, which is exactly
the evidence that the gap costs something.

So the repo currently believes it has a project-local convention gate at every close and does not have one.

## Why it matters

Every `/tasks close` and every `/fix-next` run in this repo has been linting against the generic skill
alone. The failure is silent by construction: the generic skill produces a plausible report, so a clean
pass from the wrong linter is indistinguishable from a clean pass from the right one. This is the same
class of defect the framework rules keep naming — a gate whose output looks identical whether or not it
ran.

## What to investigate

- **Does this runtime resolve project-local `.claude/skills/` at all**, or only user-level
  `~/.claude/skills/`? Check whether other project-local skills in this repo (`new-birko-subproject`,
  `roll-changelog`, `new-store-backend`, …) are reachable by name, and whether they resolve to the local
  copy or to a junctioned user-level one. `install-skills.ps1` creates junctions for the shared subset,
  which may mean the user-level entry and the repo entry are the same inode for some skills and different
  for others — establish which.
- **Is the collision the problem?** A user-level skill and a project-local skill with the *same* name may
  resolve user-level-first in this runtime, in which case name-shadowing is simply not a supported
  mechanism here and the strategy has to change.
- **Was the first fix ever verified?** The rename from `verify-birko-conventions` was made on the
  assumption that name equality produces shadowing. Nothing appears to have confirmed it — which is the
  § *verify the escape hatch opens* rule: a fix whose mechanism was never executed.

## Options to weigh

1. **Make the gate explicit rather than implicit** — have this repo's `CLAUDE.md` instruct the close gate
   to read `.claude/skills/verify-conventions/SKILL.md` by path, so resolution order is irrelevant.
2. **Keep a distinct name and reference it explicitly** from `CLAUDE.md` § Conventions, abandoning
   shadowing entirely (it failed twice).
3. **Fold the concrete checks into a script** (`verify-conventions.ps1`) the gate runs, so the checks
   cannot be lost to skill resolution at all.

Option 1 or 3; 2 is what the original name did, and it is what failed the first time.

## Acceptance criteria

- [ ] Root cause established: why the project-local file is not loaded, with the resolution order stated.
- [ ] A mechanism chosen that **cannot** silently fail — i.e. if the Birko checks do not run, the gate says
      so rather than reporting a clean pass.
- [ ] Proven able to fail: demonstrate the gate reporting the Birko checks as *not run*, then reporting
      them as run. "It resolved this time" is not evidence; show the negative case.
- [ ] The other project-local skills audited for the same problem, since they share the mechanism.
- [ ] `CLAUDE.md` § *Skills shipped by this repo* corrected — it currently states these "auto-load only
      inside this repo", which is the belief this task falsifies.

## Out of scope

- Changing what the Birko checks themselves assert. This task is about whether they run.

## Human test plan

- [ ] Run `/tasks close` (or `/verify-conventions`) in this repo and confirm from its own output that the
      project-local checks executed — the report must name the Birko-specific checks, not just the generic
      rulebook sweep. A human reads the report; that is the verification.
