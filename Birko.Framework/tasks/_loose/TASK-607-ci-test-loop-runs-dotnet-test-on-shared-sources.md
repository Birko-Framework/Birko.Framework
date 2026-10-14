---
id: TASK-607
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-045]
pr: null
github-issue: null
jira-key: null
---

# build-and-test runs `dotnet test` on `tests/Shared`, which has no project, so the job is red with every suite green

## Context

Seen on the push of `30efa4b1..33483c41` (2026-10-14). `.github/workflows/build-and-test.yml` § Run offline suites
loops `for p in tests/*/` and runs `dotnet test "$p"` on each folder. `tests/Shared` holds only shared test sources
(`StoreContract`, compiled into suites by TASK-555..557) and no `*.csproj`, so `dotnet test` fails with
`MSB1003: Specify a project or solution file`, the loop emits `::error::Shared failed`, and the job exits 1.

Measured: run 38564579213 (`33483c41`) and run 38524173984 both carry exactly one error annotation, `Shared failed`;
every real suite passed. A job that is red for a reason nobody acts on hides the next real failure.

## Acceptance criteria

- [x] The loop skips a `tests/` folder with no `*.csproj`, and says so in the log
- [x] Every folder with a project still runs (dry run over `tests/`: 172 folders, only `Shared` skipped)
- [x] The next build-and-test run on `main` has no `Shared failed` annotation

## Out of scope

- token-parity.yml being red (829034b9): separate, not investigated here.
- Moving `tests/Shared` elsewhere: the folder location is fine; the loop's assumption was wrong.

## Outcome

One `ls "$p"*.csproj || continue` line in the loop, plus a comment naming the reason. The skip is by "has no project",
not by the name `Shared`, so a future shared-sources folder doesn't reintroduce the failure.
