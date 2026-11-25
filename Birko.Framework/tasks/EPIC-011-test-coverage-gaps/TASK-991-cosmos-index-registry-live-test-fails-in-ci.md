---
id: TASK-991
parent: EPIC-011
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The Cosmos DB index-registry live test fails in CI, and the CI log does not say why

## Context

`CosmosDBIndexManagerRegistryLiveTests.The_registry_is_its_own_container_and_entity_reads_never_see_it` (TASK-628) fails
in the `live-tests` workflow's CosmosDB job: 606 of 607 pass. History on main (all `push`):

| Run | Commit | Result |
|---|---|---|
| 2026-11-21 | d942dd9f | success |
| 2026-11-22 | 463ae324 | success |
| 2026-11-23 | e731446d | success |
| 2026-11-24 | e731446d | **failure** (same commit as the day before) |
| 2026-11-25 | fb5394b0, a6fd43a5 | **failure** |

The same commit passed and then failed, so the cause is not a code change: the emulator image (`vnext-preview`, pulled
fresh per run), timing, or a test that depends on state. The test creates an index on an existing container, then
asserts the container holds no documents and the `__birko_indexes` registry has an entry naming `IX_Code` with path
`/Code/?`.

The workflow runs the suite with `dotnet test … -v q`, so the log shows only the test name with `[FAIL]`, not the
assertion or exception. That has to change first, or the next failure is equally blind.

## Acceptance criteria

- [ ] `live-tests.yml` prints a failing test's message and stack in every job (e.g. `--logger "console;verbosity=normal"`
      or a TRX upload), without flooding the log with passing tests.
- [ ] The failure is reproduced (CI rerun with the message, or locally against the same emulator image) and its
      cause named: emulator version, ordering / state, or a real defect in the index manager.
- [ ] Fixed at the cause: the test waits for what it asserts, or the index manager is fixed, or the emulator image is
      pinned to a known-good version with the reason recorded. No retry loop or skip that hides it.
- [ ] Three consecutive green `live-tests` runs on main.

## Out of scope

- Other Cosmos tests, unless the cause is shared.

## Human test plan

- N/A — fully covered by automated tests.
