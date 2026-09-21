---
id: TASK-481
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
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

- [x] A CI workflow runs all five scripts on `ubuntu-latest`, on push and on a schedule
- [x] `audit-dependencies` asserts **248** projects (or whatever the count is *then*, read from the
      tree rather than hard-coded — a frozen number becomes wrong the day a project is added, and
      then gets deleted rather than fixed)
- [x] `install-skills` is verified to produce a working symlink whose target is readable
- [x] `gen-cold-table-probes` regenerates and the job asserts a clean `git diff` but for the
      provenance line
- [x] **`audit-consumer-versions` either gets consumers to look at, or is explicitly excluded with
      the reason recorded in the workflow.** A run that reports 0 imports and passes is worse than
      not running it — decide which, in the open
- [x] **Proven it can fail:** each assertion is demonstrated red before being believed (mutate the
      tree, or point a script at a deliberately broken fixture). The `audit-*` scripts each carry
      *"verify the check can fail before believing it"* in their own headers
- [x] [[TASK-476]] and [[TASK-477]] close to `done` on the strength of the green run, and their
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

---

## ⚠ First CI run was RED, and the mutation step was wrong — not the script (2026-09-21)

Run [35601971967](https://github.com/Birko-Framework/Birko.Framework/actions/runs/35601971967). Four
steps passed, including both that carry TASK-476's actual Linux claim: the shebang executed, and
`audit-consumer-versions` found `Birko.Sandbox` with **207 projitems** rather than the 0 the unported
version produced on Linux. The mutation step failed.

**My first reading was that the script had a defect. It does not.** Reproduced locally: the planted
`Microsoft.Azure.Cosmos 2.0.0` really was in the file, the audit really did report
*"No consumer declares a framework-owned package"*, and that answer is **correct** — `Birko.Sandbox`
does not import `Birko.Data.CosmosDB.projitems`, and the ownership rule only applies to a package a
consumer **inherits**. A consumer declaring a package it does not inherit is its own business.

**A mutation aimed at the wrong target tests nothing, and looks exactly like a broken checker.** The
step now asserts its precondition first — the probe csproj must import the owning projitems — and
fails with an instruction to pick another package if that stops being true.

The package is now **`Npgsql 9.0.0` against the framework's `10.*`**, which is the real shape
[[TASK-473]] found sitting in Symbio's build for weeks. Verified locally before pushing, which is
what should have happened the first time:

```
=== 1 finding(s): 1 BELOW, 0 PINNED, 0 EQUAL, ...
Birko.Sandbox  Birko.Sandbox.csproj  Npgsql  Include=9.0.0  10.* (Birko.Data.SQL.PostgreSQL)  BELOW
```

The assertion also tightened from the package NAME to the **verdict** (`Npgsql.*BELOW`): "Npgsql"
appears in a clean run's output too, so the original grep could have passed without a finding.

**The intersection was computed, not guessed** — of the framework's 24 versioned declarations,
exactly **6** are inherited by Birko.Sandbox: `Microsoft.Data.SqlClient`, `Microsoft.Data.Sqlite`,
`Microsoft.Extensions.DependencyInjection.Abstractions`, `MySqlConnector`, `Newtonsoft.Json`,
`Npgsql`. Any of the six would work; the precondition names the one in use.

**The consumer checkout was restored** after each local experiment and verified clean
(`grep -c Npgsql` → 0) — `Birko.Sandbox` is a real repo on this machine, not a fixture.

---

## The local sweep finished: 16m41s, 249 projects, 4 findings (2026-09-21)

Two things came out of it, and one of them changed the workflow.

### ⚠ "248" was already wrong, two days after it was measured

The tree has **249** `.csproj` files under the declared buckets, not the 248 [[TASK-474]] recorded on
2026-09-19. One project was added in between. **A hard-coded 248 would have failed on this task's
first nightly run** — and a constant that fails for a legitimate reason gets deleted, not corrected,
which is how an assertion quietly becomes a comment. The step computes the expected count from the
tree with the script's own selection rule (`*.csproj` per bucket, excluding `bin`/`obj`), so the
number is never written down anywhere.

### ⚠ Findings WARN, they do not fail — an ownership decision, not leniency

The sweep found **4, two of them High**: `MessagePack 2.5.192` in `DraCode.AppHost` and
`Symbio.AppHost`, and `Microsoft.OpenApi 2.0.0` in `DraCode.KoboldLair.Server`/`.Tests`. All four are
already on file in [[TASK-475]] and **all four are consumer-owned** — the framework declares
`MessagePack 3.*`, which is unaffected.

The job as first written passed in silence with two High advisories present, which is the
green-while-broken shape this whole workflow exists to remove. But failing it would make the
framework repo red for packages it cannot bump, and a job that is red for someone else's reason gets
ignored. So findings raise a `::warning::` on the run summary and the job stays green. Both halves
are deliberate and the reasoning is in the file.

`16m41s` also confirms the split was right: on push this would have added ~17 minutes to every
change touching a script.

---

## Closed — 2026-09-21. Both jobs green; and the sweep's NAME was the last defect

Run [35602525346](https://github.com/Birko-Framework/Birko.Framework/actions/runs/35602525346):
`Helper scripts on Linux` **31s**, `Dependency sweep` **5m38s**. Every assertion produced real
output rather than an exit code:

```
shebang OK
Sandbox imports: 207
=== 1 finding(s): 1 BELOW, 0 PINNED, 0 EQUAL, ...
mutation caught - the check can fail
symlinks created: 4
every symlink resolves
expecting 170 projects / script swept 170 / whole tree swept
```

### ⚠ The sweep job found 0 while the family had 4, and its name hid that

CI checks out **one** consumer, so it swept **170** projects. The same script on a developer machine
with all 8 checked out sweeps **249** and reports **4 findings, 2 High** (`MessagePack 2.5.192`,
`Microsoft.OpenApi 2.0.0` — [[TASK-475]]). A nightly job called **"Dependency sweep"** going green
says the family is clean. It is not.

So the job is now **`Dependency sweep (framework + Sandbox only)`**, and its success line prints the
consumer count and states outright that this is not the whole-family audit. **Nothing about the
checking changed — only the claim.** That is the same defect as the three this session started
with, in its purest form: the check was honest, the label was not.

### Three times in one task, a green step was not testing what it claimed

Worth listing together, because none was caught by anything going red:

1. **The mutation aimed at a package the consumer does not inherit.** The step went red and my first
   reading blamed the script; the script was correct. A mutation at the wrong target tests nothing
   and looks exactly like a broken checker.
2. **The mutation's assertion matched the package NAME**, which appears in a clean run too. Tightened
   to the verdict (`Npgsql.*BELOW`) — it could have passed with no finding at all.
3. **`--check` never writes**, so it could not see [[TASK-477]]'s defect, which was the write path.
   A check-only step would have gone green against the exact bug it stood in for.

Each was found by asking *"what would this step have done against the original defect?"* — never by
a failure.

### What this did NOT prove

`audit-declarations` still carries the weakest assertion in the file — [[TASK-482]] — and the
whole-family vulnerability audit still needs every consumer and stays human-run.

---

## ⚠ A FOURTH one, found by the workflow failing on itself (2026-09-21)

Run 35603346855 went red at the generator step, and the generator was **perfect** — both profiles,
200 and 60 types, unchanged, on Linux. The failure was my own workflow interfering with itself:

```
##[error]regeneration changed the committed output:
 Birko.Framework/audit-declarations.cs | 0
 1 file changed, 0 insertions(+), 0 deletions(-)
```

**Zero insertions, zero deletions** — a **file mode** change, `100644 → 100755`, left by the
`chmod +x audit-declarations.cs` in the *shebang* step **four steps earlier**. The bare
`git diff --quiet` swept the whole tree and reported a sibling step's side effect as this step's
failure.

That is [[CLAUDE-conventions]] rule 61 — *"A test teardown that reaches process-wide state damages a
PARALLEL sibling, and the victim is never the file that caused it"* — arriving in a workflow instead
of a test suite. The diff is now scoped to the two generated paths, because **an assertion should
look only at what it is asserting about**.

Note the shape: this one WAS caught by something going red, unlike the three above — but it pointed
at the wrong file, and taking the red at face value would have sent someone into the generator,
which was faultless.