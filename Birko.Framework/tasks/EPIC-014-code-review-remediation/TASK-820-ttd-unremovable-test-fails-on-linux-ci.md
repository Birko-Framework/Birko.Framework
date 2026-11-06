---
id: TASK-820
parent: EPIC-014
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-302, TASK-305]
findings: []
pr: null
github-issue: null
jira-key: null
---

# CI red since TASK-302: the "cannot be removed" teardown test relies on a Windows file lock

## Context

Found checking CI on the user's request (2026-11-06). `build-and-test` failed on both pushes since [[TASK-302]] landed
(runs 41877201033 on `67cba6f8`, 42033906693 on 2026-11-05), each on one test:
`Birko.Data.SQL.SqLite.Tests.TestTempDirectoryTests.A_directory_that_cannot_be_removed_is_recorded_not_swallowed`.
It made a directory unremovable by holding a file open with `FileShare.None`, which stops a delete on Windows only;
CI runs on `ubuntu-latest`, where an open file can be unlinked, so the delete succeeded and the test failed. Every
later Framework push was `[skip ci]` or tasks-only, so [[TASK-305]]'s code had not been built in CI either.

## Acceptance criteria

- [x] The test makes the directory unremovable on both platforms: the held file on Windows, a read-only directory on
      Linux (stops a non-root user deleting its entries); it refuses to run as root on Linux, where neither works,
      rather than passing vacuously.
- [x] Measured in `mcr.microsoft.com/dotnet/sdk:10.0` as uid 1000: the old test fails (1 of 2, as in CI), the new one
      passes; Windows passes.
- [x] `Birko.Data.SQL.SqLite.Tests` 616/616 and `Birko.Data.SQL.Tests` 734/734 on Linux (TASK-305's busy-retry tests
      included).
- [ ] CI green on the next push.

## Human test plan

N/A — fully covered by automated tests.
