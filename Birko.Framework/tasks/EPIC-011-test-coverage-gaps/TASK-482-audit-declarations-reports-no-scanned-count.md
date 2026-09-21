---
id: TASK-482
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
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

- [ ] The summary reports the number of projects **scanned** and the number of `.projitems` read,
      distinctly from the count of offending projects
- [ ] The run **refuses** rather than reports clean when it scanned nothing — the same shape as
      `audit-dependencies`' bucket guard, whose message is *"A partial sweep is not a clean sweep"*
- [ ] Verified by pointing `--root` at an empty directory: it must fail, not print a clean summary
- [ ] Re-proven against the real tree: the scanned count matches the `Birko.*` project directories
      actually present, and the existing clean verdict is unchanged
- [ ] [[TASK-481]]'s `helper-scripts.yml` step for this script replaces its placeholder assertion
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
