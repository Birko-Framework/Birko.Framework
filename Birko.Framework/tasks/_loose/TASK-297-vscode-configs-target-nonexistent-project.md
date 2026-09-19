---
id: TASK-297
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-09-01
depends-on: []
blocks: []
findings: [FIELD-005]
pr: null
github-issue: null
jira-key: null
---

# `.vscode/tasks.json` and `launch.json` target a project this repo does not contain

## Context

**Found 2026-09-01 by a cold-drill survey of this repo** (an `adopt-project` execution test run from
`project-lifecycle-skills`), verified by hand.

Both files are tracked (`git ls-files .vscode` → `launch.json`, `tasks.json`) and both point at the same
non-existent project:

| File | Line | Target |
|---|---|---|
| `.vscode/tasks.json` | 10 | `${workspaceFolder}/Birko.Framework/Birko.Framework.csproj` |
| `.vscode/launch.json` | 9 | `${workspaceFolder}/Birko.Framework/bin/Debug/net10.0/Birko.Framework.dll` |

There is **no `Birko.Framework/` subdirectory** in this repo, and **no `.csproj` anywhere in it** —
`find . -maxdepth 2 -name '*.csproj'` returns nothing. That is not an accident of layout: this repo is the
family's **aggregator**, holding `Birko.Framework.slnx`, `Birko.Packages.props`, the shared `CLAUDE*.md`
docs, `docs/` and `tasks/`. Every one of the solution's 343 project entries resolves to a sibling
directory outside this root, each its own git repo.

So the committed `build` task and the `Birko.Framework` launch configuration cannot have worked from this
repo under any resolution of `${workspaceFolder}`. The only file of that name on disk is
`../../Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj` — a consumer's aggregator project,
not this repo's.

### The likely history, and why it still matters

The paths look like a survivor of the workspace reorganisation tracked as this repo's own **TASK-036**
(*"Reorganize C:\\Source into Birko/{Framework,Framework.Tests,Consumers}"*). Before that, a
`Birko.Framework/Birko.Framework.csproj` plausibly existed at that relative position; afterwards the
aggregator kept the name and lost the project. This is the exact shape the lifecycle skills call *"a path
aimed at a directory that moved"*.

It matters more than a stale config usually would, because these two files are the **only** task-runner
targets in the repo — there is no CI workflow, no `Makefile`, no `justfile`, no `package.json`. So anything
asking *"what does this repo's gate actually run?"* finds `.vscode/tasks.json` first and is answered by a
broken build task. A cold survey hit exactly that: it had to fall back to the solution file and the guide's
prose to establish that a test runner exists at all.

### Three resolutions, and the choice is a real one

1. **Retarget at the solution** — `dotnet build Birko.Framework.slnx`, which is what `README.md`
   § *Getting Started* and `CLAUDE.md` § Testing already document. Matches how the repo is actually built.
2. **Delete both files.** An aggregator with no project of its own may have nothing meaningful to launch,
   and a `launch.json` for a library family is arguably noise. Cheapest, and loses whatever the original
   intent was.
3. **Keep a launch config but point it at a real consumer** — deliberate only if someone actually debugs
   through the aggregator, and it would mean this repo's IDE config depends on a sibling repo's layout,
   which is the coupling `$(BirkoSrc)` exists to keep out of source files.

Option 1 for `tasks.json` is close to obvious. `launch.json` is the genuine question: **what would anyone
launch from a repo with no entry point?** Answer that before editing it.

## Acceptance criteria

- [ ] No tracked file in `.vscode/` names a path that does not resolve from this repo
- [ ] `tasks.json`'s build target, if kept, is the one the guide and README already document
- [ ] The `launch.json` question is answered explicitly — retargeted, deleted, or kept with a recorded reason — rather than left pointing at a missing DLL
- [ ] Whatever lands does not make this repo's IDE config depend on a sibling repo's internal layout
- [ ] `dotnet build Birko.Framework.slnx` still succeeds

## Out of scope

- The `$(BirkoSrc)` / out-of-root solution paths generally. That is the documented aggregator design, not a defect, and § *CI a repo cannot pass* already classifies it.
- Adding a CI workflow. This repo cannot pass one in isolation — every build input is in a sibling git repo obtainable from no feed — and that is a separate, larger decision.
- `README.md` § *License* reading `Part of the Birko Framework.` while `License.md` is a full MIT grant. Prose staleness, noted by the same survey as an aside, not a broken path.

## Renumbered from TASK-290 (2026-09-04)

Filed as `TASK-290` on 2026-09-01. A second `TASK-290` was filed on 2026-09-02 under EPIC-014
(*"Name the mechanism behind the schema-ensure escape"*) by an agent that computed the next id from
`tasks/EPIC-*/` alone and never globbed `tasks/_loose/` — so the ID-generation rule ("global counters per
type, unique project-wide") was broken by a partial scan of exactly the folder this task lives in.

**This file renumbered rather than the newer one, against the usual first-claim rule, and the asymmetry is
the reason:** measured 2026-09-04, the EPIC-014 `TASK-290` is named in **6 commit messages** (`15e67c5`,
`436b050`, `987448e`, `b9357be`, `dc2b3a0`, `2e2fd4d`), in the `related:` lists and prose of six sibling
task files, in `CLAUDE.md` § Conventions and § Recent Updates, and in consumer Symbio's own TASK-602 as
*"Birko TASK-290"*. Commit messages are immutable, and a cross-repo citation cannot be rewritten from here
at all. This file was referenced **nowhere** but itself and the generated dashboard.

So renumbering the later claimant would have orphaned six commits and a cross-tree reference to fix a
collision that costs nothing to fix here. Recorded because the outcome otherwise reads as the rule simply
not holding.

## Human test plan

- [ ] Open the repo in VS Code and run the default build task; confirm it builds rather than erroring on a missing project
- [ ] Confirm every path in `.vscode/*.json` resolves from the repo root

## Implementation plan

_Populated by `/tasks plan TASK-297` — leave empty until then._
