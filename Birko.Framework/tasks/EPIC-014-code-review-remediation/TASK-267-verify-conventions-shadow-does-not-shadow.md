---
id: TASK-267
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-257]
findings: []
pr: "project-lifecycle-skills 4aad076 (generic skill: discovery step) + Birko.Framework 0e4a57a (rename + section rewrite)"
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

- [x] Root cause established: why the project-local file is not loaded, with the resolution order stated.
- [x] A mechanism chosen that **cannot** silently fail — i.e. if the Birko checks do not run, the gate says
      so rather than reporting a clean pass.
- [x] Proven able to fail: demonstrate the gate reporting the Birko checks as *not run*, then reporting
      them as run. "It resolved this time" is not evidence; show the negative case.
- [x] The other project-local skills audited for the same problem, since they share the mechanism.
- [x] `CLAUDE.md` § *Skills shipped by this repo* corrected — it currently states these "auto-load only
      inside this repo", which is the belief this task falsifies.

## Out of scope

- Changing what the Birko checks themselves assert. This task is about whether they run.

## Implementation plan

### Step 0 — measured 2026-09-07, before a line changed

Both probes are empirical, from the skill loader's own banner:

| probe | banner | conclusion |
|---|---|---|
| `Skill(verify-conventions)` | `C:\Users\FinStat\.claude\skills\verify-conventions` | the **user-level junction** -> the generic skill. Body carried none of checks 1-10. |
| `Skill(new-store-backend)` | `C:\Source\...\Birko.Framework\.claude\skills\new-store-backend` | project-local `.claude/skills/` **is** resolved. |

**Root cause, stated as a resolution order:** project-local skills are discoverable, but a name present
at *both* user level and project level resolves **user-level first**. Name-shadowing is therefore not a
supported mechanism in this runtime, and never was — so the rename that was the first fix could not have
worked, and neither could any second attempt at the same shape.

**Audit of every local skill (criterion 4).** Two collide with a user-level junction pointing at the
generic skill, and both lose: `verify-conventions` and `roll-changelog`. Four (`birko-new-project`,
`design-agent`, `new-birko-web-page`, `new-birko-web-component`) have user-level junctions pointing back
**into this repo**, so they resolve to the same bytes either way and are harmless by construction —
`install-skills.ps1` shares exactly that set. Two (`new-birko-subproject`, `new-store-backend`) have no
user-level entry at all and win locally.

**The false premise is written down in four places**, which is why it survived two fixes:

1. the generic skill's § Scope layering — *"Name it `verify-conventions`, exactly. Shadowing works by folder name."*
2. this repo's `.claude/skills/verify-conventions/SKILL.md` header — *"Project-local skills win by name inside their repo"*
3. `install-skills.ps1`'s header — *"deliberately share the generic skills' names so they SHADOW them here — that is the point"*
4. `CLAUDE.md` § *Skills shipped by this repo* — *"auto-load only inside this repo"*

### The fix — put the detector in the skill that WINS, not the one that loses

The mechanism that cannot silently fail has to live in whatever always runs, and that is the **generic**
skill. This is the same discipline § TASK-295 records for `CreateTable`: bookkeeping a rule depends on
goes in the non-bypassable wrapper, not in the implementation that gets overridden.

1. **Rename the local skill to `verify-birko-conventions`** — a distinct name, so it is directly
   invokable and collides with nothing. This also makes the five existing `[[verify-birko-conventions]]`
   references correct; they were not stale, they were early.
2. **Give the generic skill a discovery step** that globs the repo for a project-local extension, runs
   the generic pass, then reads and executes it, and **names it on the report header**. If an extension
   exists and was not executed, that is a **blocker**, not a clean pass. Fixes every project, not just
   this one.
3. **Correct all four statements of the false premise** with the measured resolution order.
4. **`roll-changelog`** -> `roll-birko-changelog`. Not a gate (nothing calls it automatically), so it
   needs reachability, not discovery.
5. **The local skill's step 0 becomes conditional** — reached via discovery the generic pass has already
   run, so re-running it would loop; invoked directly it must still run.

### Proven able to fail (criterion 3)

The negative case is already captured above: invoking `verify-conventions` loaded the generic body with no
Birko checks. The positive case is the same invocation after the fix naming the extension on its header.
Both are recorded, because "it resolved this time" is not evidence.

### Out of scope, deliberately

- **What the Birko checks assert** — unchanged; this task is about whether they run.
- **Any framework code.** Nothing under `Birko.Data.*` is touched.

## Human test plan

- [x] Run `/tasks close` (or `/verify-conventions`) in this repo and confirm from its own output that the
      project-local checks executed — the report must name the Birko-specific checks, not just the generic
      rulebook sweep. A human reads the report; that is the verification.

---

## Worked 2026-09-07 — status `review`, pending the human read of one report

### What was measured, before a line changed

| probe | loader banner | conclusion |
|---|---|---|
| `Skill(verify-conventions)` | `~\.claude\skills\verify-conventions` | the user-level junction -> the **generic** file; body carried none of checks 1-10 |
| `Skill(new-store-backend)` | `...\Birko.Framework\.claude\skills\new-store-backend` | project-local `.claude/skills/` **is** resolved |

**Resolution order:** project-local skills are discoverable; a name present at both user and project
level resolves **user-level first**. So shadowing was never a mechanism, the first fix could not have
worked, and a second attempt at the same shape could not have either.

### What changed

- **`verify-conventions` -> `verify-birko-conventions`**, `roll-changelog` -> `roll-birko-changelog`
  (distinct names, no collision, both directly invokable). The five existing
  `[[verify-birko-conventions]]` references were **not stale — they were early**, and are correct again
  untouched.
- **The generic skill gained step 0** (`project-lifecycle-skills`, commit below): glob
  `.claude/skills/verify-*conventions*/SKILL.md`, run the generic pass, hand off, **name the extension on
  the report header**, and report a 🛑 for one found-but-not-run. The detector lives in the skill that
  *wins* resolution — § TASK-295's non-bypassable-wrapper rule, applied to a gate.
- **All four statements of the false premise corrected** — the generic skill's § Scope layering and its
  description, this repo's local skill header and description, `install-skills.ps1`'s header (which
  claimed renaming would *disarm* the gates, exactly backwards), and `CLAUDE.md` § *Skills shipped by
  this repo*. Three `CLAUDE-maintenance.md` references were repointed at the renamed skill, and a wrong
  check number (`#11`, which does not exist) corrected to `#7b`.
- **The local step 0 is now conditional on the entry door**, or the generic and local skills loop and
  every generic finding is reported twice.

### Proven able to fail

The negative case is the Step 0 table above: the gate loaded the generic body with no Birko checks.
The positive case is the same invocation afterwards — step 0 globbed, found
`.claude/skills/verify-birko-conventions/SKILL.md`, and ran it. **And it caught a real violation on the
change that made it able to fire:** check 9 (5+ files, no `Recent Updates` entry) reported the missing
entry, which is now written. Before the fix that check could not have run at all.

### Audit (criterion 4)

Two collided with a user-level junction pointing at the generic skill and both lost —
`verify-conventions` and `roll-changelog`. Four (`birko-new-project`, `design-agent`,
`new-birko-web-page`, `new-birko-web-component`) have junctions pointing **into this repo**, so they
resolve to the same bytes either way and were never at risk; `install-skills.ps1` shares exactly that
set. Two (`new-birko-subproject`, `new-store-backend`) have no user-level twin and always worked.

### ⚠ Deliberately not done, and why the status is `review`

- **A skill instruction is not an enforcement mechanism.** Step 0 is as hard as a skill system allows —
  in the file that always loads, at the top, with a blocker for the negative case — but nothing
  *compels* an agent to execute it. Only a pre-commit hook cannot be skipped, and the generic skill's
  § *Where this runs* already names [[update-config]] for that. Recorded, not claimed as closed; if the
  gate is seen to skip step 0 in practice, that is the escalation.
- **What the Birko checks assert is unchanged** — this task was about whether they run.
- **`roll-birko-changelog` got reachability, not discovery.** Nothing invokes it automatically, so it
  has no gate to go silent on; renaming is the whole fix.
- **No framework code touched.** Nothing under `Birko.Data.*`.
- **The human test plan is unrun by a human.** I executed the gate and read its report; the plan asks a
  human to. That is the only thing between `review` and `done`.

### Closed 2026-09-07 — the human read the report

The human test plan is run. `/verify-conventions` in this repo **named the project extension on its
report header and executed its concrete checks**, which is exactly what it could not do before this
change — the previous behaviour was a silent generic-only pass that claimed completeness. That was the
only thing between `review` and `done`.

⚠ **The same run reported a real 🛑 against this task's own `CLAUDE.md` entry** (and against TASK-264's
and TASK-266's): all three said *"The standing rule is in § Conventions"* while § Conventions mentioned
none of them. This task's entry was the false alarm of the three — it points at § *Skills shipped by this
repo*, which does carry the mechanism — but the other two were genuine, and the rules were written into
§ Conventions before this file was closed. **The gate caught a defect on the very change that made it
able to fire, then caught two more on its first ordinary use.** That is the return on the fix.

The pre-commit-hook gap recorded under *Deliberately not done* is spawned as [[TASK-300]] rather than
left as prose in a closed task.
