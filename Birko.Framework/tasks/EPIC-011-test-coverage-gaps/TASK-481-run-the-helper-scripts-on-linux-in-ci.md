---
id: TASK-481
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: in-progress
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

Drafted 2026-09-21 at `/tasks pick`, after reading all five scripts and running three of them
locally. **The open question resolved a third way, which neither branch of the acceptance criterion
anticipated** — see step 2.

1. **A new `helper-scripts.yml`**, not a job bolted onto `live-tests.yml`. These need no server, and
   the live workflow's path filter is about `Birko.Data.*`; this one triggers on the scripts,
   `tools/AuditCommon/**` and itself, plus a schedule.

2. **⚠ The consumers problem: a FIXTURE consumer, not "clone them" and not "exclude it".** The
   acceptance criterion offered two branches and both are wrong:
   - *Exclude it* reproduces the exact narrowing [[TASK-230]] measured as harmful — a sweep scoped to
     one tree *"produced a fix that looked complete and was not"* — and `audit-dependencies` would
     not run at all, because it **declares** `{root}/Consumers` and stops when a declared bucket is
     missing. That guard is [[TASK-474]]'s fix; CI must satisfy it, never weaken it.
   - *Clone the consumers* needs credentials for private repos and makes this job fail whenever an
     unrelated repo moves.

   So CI builds the layout the scripts contract for: the checkout at `<root>/Framework`, and
   `<root>/Consumers/` holding **one committed fixture consumer** — a csproj that imports a real
   `.projitems` through `$(BirkoSrc)` and declares one package **deliberately below** the framework's.
   That gives `audit-consumer-versions` something it must find, so a zero is a **failure** rather than
   a shrug, which is what its own header demands. **This does not make the CI job "the audit"**: the
   real vulnerability sweep is whole-family and stays periodic and human-run. The workflow says so.

3. **Assert counts, not exit codes**, because every recorded Linux defect here is a silent-wrong-
   answer: a backslash that does not resolve, a bucket that vanishes, a `bin|obj` filter that never
   matches. Exit 0 is what all of them produced.
   - `audit-dependencies` — assert the swept project count against the tree, **computed, not frozen**.
   - `audit-consumer-versions` — assert the fixture consumer is found with a **non-zero** projitems
     count and its planted BELOW row is reported.
   - `gen-cold-table-probes --check` — already fails on stale output; no git diff needed.
   - `install-skills` — assert a **symlink** whose target is readable (the junction path is
     Windows-only and is the half that has never run).
   - `audit-declarations` — see step 5.

4. **Prove each assertion red before believing it.** Each `audit-*` header carries *"verify the check
   can fail before believing it"*. Done by mutation against the fixture, not the real tree.

5. **⚠ `audit-declarations` cannot be asserted as written, and that is a finding about the checker.**
   Its summary is `Undeclared: 0 (project,package) pairs across 0 projects`, where the count is of
   **offending** projects — it never reports how many it **scanned**. So a run that swept nothing
   prints exactly what a clean run prints. That is this repo's own recurring defect living inside one
   of its checkers. **Spawn it** — adding a scanned count changes the script's output, and this task's
   § Out of scope forbids absorbing it. Until then, assert this one by mutation only.

---

## Outcome — 2026-09-21

`.github/workflows/helper-scripts.yml`: a `scripts` job (the four fast ones, on push + schedule) and
a separate `sweep` job (`audit-dependencies`, **not** on push).

### ⚠ The open question resolved a THIRD way, and better than the plan's answer

The acceptance criterion offered "get consumers to look at" or "exclude it, with the reason". The
plan chose a third — a synthetic fixture consumer — and that was also wrong, because
**`build-and-test.yml` already checks out a real consumer into exactly this layout**: the framework
at `birko/Framework` and `Birko.Sandbox` at `birko/Consumers/Birko.Sandbox`, with
`token: ${{ secrets.BIRKO_CI_TOKEN || github.token }}`. The shape the scripts contract for was
already an established CI pattern in this repo. Reused rather than reinvented, and a **real**
consumer means a zero import count is a genuine failure signal instead of a fixture's tautology.

**Three answers were considered and the one that shipped was in none of the task's branches.** Worth
recording: the acceptance criteria were written before reading `build-and-test.yml`.

### What each step asserts, and why it is a COUNT

Every Linux defect these scripts carried exited 0 — `set -e` would have caught none of them:

| step | assertion |
|---|---|
| shebang | `./audit-declarations.cs --help` prints `Usage:` — the `#!/usr/bin/env dotnet` line has never executed |
| `audit-consumer-versions` | `Birko.Sandbox` reports a **non-zero** projitems count (207 locally) |
| ↳ mutation | a `Microsoft.Azure.Cosmos 2.0.0` planted below the framework's `3.*` **is reported**, then reverted |
| `gen-cold-table-probes --check` | regenerates to memory, fails on stale output — no `git diff` needed |
| `install-skills` | a **symlink** is created and every one resolves to a readable `SKILL.md` |
| `audit-dependencies` | swept count **equals** a count computed from the tree, and nothing is `COULD NOT BE AUDITED` |

### ⚠ audit-dependencies is not on push, on measurement

Started locally and **still running past 25 minutes** — it restores every swept project to ask NuGet
for vulnerability rows. Its own header already says it is *"a PERIODIC check, not a build setting"*,
so it runs nightly and on demand, with a 45-minute timeout. Putting it on push would have added ~25
minutes to every push that touches a script.

### ⚠ One script could not be asserted, and that is now [[TASK-482]]

`audit-declarations` reports the count of **offending** projects and never of **scanned** ones, so a
clean sweep and a sweep of nothing print the same line. Its CI step carries the weakest assertion in
the file — "a summary line was printed" — with a comment naming the task and the line to replace.
**Spawned rather than absorbed:** adding a scanned count changes the script's output, which § Out of
scope forbids here.

### Pre-flight before pushing

All seven `run:` blocks extracted from the YAML and checked with `bash -n`; the embedded heredoc
dedents to column 0 correctly under the block scalar. `--help` confirmed to print `Usage:`.
`audit-declarations` defaults `--root` to one level above the script and `audit-consumer-versions` to
two, which the CI layout satisfies without a flag — passed explicitly anyway where it matters.