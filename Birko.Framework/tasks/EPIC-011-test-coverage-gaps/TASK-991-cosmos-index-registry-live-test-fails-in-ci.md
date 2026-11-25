---
id: TASK-991
parent: EPIC-011
feature: null
status: in-progress
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

## Findings

**The trigger is the emulator image, not our code.** The CosmosDB job pulls `azure-cosmos-emulator:vnext-preview`, a
moving tag, on every run. Its digest, read from each run's CosmosDB job log:

| Runs | Image digest | Result |
|---|---|---|
| 2026-11-21, 22, 23 (schedule) | `sha256:09dcd04e7149975f0427e6cb09781681f407fc74d01e54fc9d6410c36795cda3` | success |
| 2026-11-24 (schedule, e731446d), 25 (schedule fb5394b0, push a6fd43a5) | `sha256:c274ac17c19a5f74dbadbc99f259bc49a8640a03f6789f54d7f76cbfc33ff6b6` | failure |

e731446d passed on the old image and failed on the new one. What the new emulator does differently is not known yet: the
log had no message (`-v q`). Every `dotnet test` in `live-tests.yml` now adds `--logger "console;verbosity=minimal"`, so
a failing test prints its assertion and stack while the build stays quiet; pushed and the workflow run once to read it.

## Acceptance criteria

- [x] `live-tests.yml` prints a failing test's message and stack in every job (e.g. `--logger "console;verbosity=normal"`
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
