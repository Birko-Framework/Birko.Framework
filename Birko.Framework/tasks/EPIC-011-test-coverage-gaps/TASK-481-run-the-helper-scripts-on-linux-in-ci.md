---
id: TASK-481
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-21
depends-on: []
blocks: [TASK-476, TASK-477]
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Run the five helper scripts on Linux in CI, so their sign-off stops needing a human with a Linux box

## Context

[[TASK-476]] ported four root helper scripts off PowerShell to .NET 10 file-based apps, and
[[TASK-477]] did the same for a fifth that the first survey missed. Both are `status: review` —
**code complete, sign-off pending** — and both have been there since 2026-09-19.

Neither is waiting on attention. They are waiting on a **Linux box**, which is the one thing their
own subject matter requires and the machine they were written on does not have. Their
`## Human test plan` sections were written on 2026-09-20 (they had none at all before — see
[[TASK-479]]'s session) and name the exact steps, all of which are mechanical:

- all five run to completion with no unopenable path
- `audit-dependencies` reports **248** projects, not 81 — [[TASK-474]]'s bucket regression, which is
  **invisible on Windows** because a backslash resolves either way
- `audit-consumer-versions` reports a non-zero import count for a consumer known to import Birko;
  its own header calls a zero *"a defect in this script, not a clean result"*
- `install-skills` creates a working **symlink** (the junction path is Windows-only)
- `gen-cold-table-probes` writes both profiles, creates no file with a literal backslash in its name,
  and regenerates byte-identical output but for the provenance line — **including the blank line
  PostgreSQL has and SQLite does not**
- `#!/usr/bin/env dotnet` makes each directly executable — present in the files, never executed

**Every one of those is a machine check.** A human adds nothing except access to Linux, and a
verification step whose only human content is "own the right OS" is a step that does not get run: two
tasks have now sat in `review` for two days proving exactly that.

The scripts (all .NET 10 file-based apps, all carrying the shebang):

| script | location |
|---|---|
| `audit-declarations.cs` | `Birko.Framework/` |
| `audit-dependencies.cs` | `Birko.Framework/` |
| `audit-consumer-versions.cs` | `Birko.Framework/` |
| `install-skills.cs` | `Birko.Framework/` |
| `gen-cold-table-probes.cs` | `Birko.Framework/tools/` |

⚠ **`audit-consumer-versions` needs consumer repos to say anything**, and CI checks out the framework
alone. Resolving that is the one genuinely open design question here — see Acceptance. Do **not** let
it report a zero and call the job green; that is the precise failure its header warns about, and this
task exists to stop a script reporting a clean result it did not earn.

## Acceptance criteria

- [ ] A CI workflow runs all five scripts on `ubuntu-latest`, on push and on a schedule
- [ ] `audit-dependencies` asserts **248** projects (or whatever the count is *then*, read from the
      tree rather than hard-coded — a frozen number becomes wrong the day a project is added, and
      then gets deleted rather than fixed)
- [ ] `install-skills` is verified to produce a working symlink whose target is readable
- [ ] `gen-cold-table-probes` regenerates and the job asserts a clean `git diff` but for the
      provenance line
- [ ] **`audit-consumer-versions` either gets consumers to look at, or is explicitly excluded with
      the reason recorded in the workflow.** A run that reports 0 imports and passes is worse than
      not running it — decide which, in the open
- [ ] **Proven it can fail:** each assertion is demonstrated red before being believed (mutate the
      tree, or point a script at a deliberately broken fixture). The `audit-*` scripts each carry
      *"verify the check can fail before believing it"* in their own headers
- [ ] [[TASK-476]] and [[TASK-477]] close to `done` on the strength of the green run, and their
      `## Human test plan` sections record that CI now owns those steps

## Out of scope

- **Changing what the scripts check.** This runs the existing five and asserts their existing
  contracts. A finding they surface is its own task.
- **Fixing whatever the first Linux run turns up.** It is a real possibility — [[TASK-476]] says
  plainly that *"porting to C# fixes none of the path bugs by itself"* and that all ten path
  expressions were fixed by hand, unverified on Linux. If the run goes red, that is this task
  succeeding: spawn the fix, do not absorb it here.
- Porting anything else to Linux, or auditing for a sixth script. [[TASK-477]] already paid for the
  lesson that a survey rooted in the wrong directory answers a narrower question than the one asked;
  if a sixth exists, it gets its own task.

## Human test plan

N/A — this task's entire purpose is to convert a human test plan into a machine one. If it needs a
human step to verify, it has failed its own premise.

## Implementation plan

_Populated by `/tasks plan TASK-481` — leave empty until then._
