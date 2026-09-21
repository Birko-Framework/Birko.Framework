---
id: TASK-482
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-09-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-011]
pr: null
github-issue: null
jira-key: null
---

# audit-declarations cannot tell a clean sweep from a sweep of nothing

## Context

Spawned from [[TASK-481]] while wiring the helper scripts into CI. The assertion for this script
could not be written, and the reason is a defect in the script.

Its entire summary is (`audit-declarations.cs:157`):

```
=== Undeclared: 0 (project,package) pairs across 0 projects
```

`projectCount` is computed at `:155` as the distinct projects **among the findings** — so it is a
count of **offending** projects. The script never reports how many projects it **scanned**. A run
that swept the whole tree and found nothing prints byte-for-byte what a run that swept *nothing*
prints.

⚠ **This is the defect class the script's own header was written about**, from the other side. That
header describes a previous version that *"reported 25 findings against 25 projects that were all
following the rule"* and calls it *"the failure mode this codebase keeps meeting — a checker that
cannot tell compliance from the defect."* It was fixed for false positives and left open for false
negatives.

It matters more since [[TASK-476]] than before it. That port fixed ten path expressions by hand for
Linux, and the failure mode of a wrong path expression here is **an empty scan** — not a crash.
`Paths.EnumerateFiles` returns an empty sequence for a missing directory by design, with the comment
*"callers that need absence to be loud check first"*. This caller does not check.

Its two siblings both report their denominator: `audit-dependencies` prints
`Auditing {N} projects under {root}` and `audit-consumer-versions` prints a per-consumer import count
precisely so a zero is legible. This one is the odd sibling out.

## Acceptance criteria

- [x] The summary reports the number of projects **scanned** and the number of `.projitems` read,
      distinctly from the count of offending projects
- [x] The run **refuses** rather than reports clean when it scanned nothing — the same shape as
      `audit-dependencies`' bucket guard, whose message is *"A partial sweep is not a clean sweep"*
- [x] Verified by pointing `--root` at an empty directory: it must fail, not print a clean summary
- [x] Re-proven against the real tree: the scanned count matches the `Birko.*` project directories
      actually present, and the existing clean verdict is unchanged
- [x] [[TASK-481]]'s `helper-scripts.yml` step for this script replaces its placeholder assertion
      (currently only "a summary line was printed") with the scanned count, and the comment naming
      this task is removed

## Out of scope

- The other four scripts. `audit-dependencies` and `audit-consumer-versions` already report their
  denominators; `install-skills` and `gen-cold-table-probes` are not surveys.
- Changing **what** counts as an undeclared package. This is about the denominator, not the rule.

## Human test plan

N/A — fully covered by the empty-root check in the acceptance criteria.

## Implementation plan

_Populated by `/tasks plan TASK-482` — leave empty until then._

---

## Outcome — 2026-09-21

`audit-declarations.cs` now reports its denominator and refuses an empty sweep.

```
=== Scanned: 173 projects, 1421 source files, 1706 using-declarations
=== Undeclared: 0 (project,package) pairs across 0 projects
```

| state | before | after |
|---|---|---|
| real tree | `0 pairs across 0 projects` — indistinguishable from nothing | denominator + **unchanged** clean verdict |
| a `Birko.*` dir with no projitems and no sources | **exit 0, reported clean** | **exit 1**, refuses |
| empty root | unhandled exception, exit 127, stack trace | exit 1, clean message |

### ⚠ The existing guard was real but aimed one level too high

The script already threw when it found **no `Birko.*` directories** (`:104`). That is not the
dangerous case. The dangerous one is directories that *do* resolve while the **file** enumeration
inside them comes back empty — which is exactly what a wrong path expression produces, because
`Paths.EnumerateFiles` answers a missing directory with an empty sequence **by design**, its own
comment saying *"callers that need absence to be loud check first."* This caller did not. Measured:
a hollow root sailed past the directory guard and printed a clean summary.

So the new guard is on what was actually **read**, not on what was found, and the old throw became a
`Report.Unknown` + `return 1` matching `audit-dependencies`' wording — *"A partial sweep is not a
clean sweep"* — rather than a stack trace.

### The CI assertion is now real

[[TASK-481]]'s step no longer just checks that a summary line appeared. It asserts
`scanned == (count of .projitems in the tree)` — **computed, not frozen**, independently confirmed at
**173** by `find` — and that at least 100 source files were read, so a *shrinking* scan is visible
before it ever reaches zero. The comment naming this task is gone.