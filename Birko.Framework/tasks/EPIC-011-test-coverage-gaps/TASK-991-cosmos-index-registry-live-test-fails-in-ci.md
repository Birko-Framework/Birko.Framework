---
id: TASK-991
parent: EPIC-011
feature: null
status: done
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

**Reproduced with the message (live-tests run 45612185103, Framework d68b1aec, image c274ac17):** the test's own raw
`SELECT * FROM c` on the `Rows` container, issued right after `CosmosDBIndexManager.CreateAsync` replaced its indexing
policy, fails with `CosmosException` **500 InternalServerError, "Internal error occurred (-2147418113)"** from the
emulator gateway (the POST to `.../colls/Rows/docs`). Birko's code is not in that call, and a server error is never a
valid answer to a query, so it is the emulator build, not the index manager and not the test's timing. 606 of 607
Cosmos tests pass on it; every other live job passes.

**Fixed by pinning** the emulator to the last good build by digest (`@sha256:09dcd04e…`), with this reason in
`live-tests.yml` beside the pin and how to unpin (run the current `vnext-preview` locally against this test). No
retry, no skip. The test is unchanged.

## Acceptance criteria

- [x] `live-tests.yml` prints a failing test's message and stack in every job (e.g. `--logger "console;verbosity=normal"`
      or a TRX upload), without flooding the log with passing tests.
- [x] The failure is reproduced (CI rerun with the message, or locally against the same emulator image) and its
      cause named: emulator version, ordering / state, or a real defect in the index manager.
- [x] Fixed at the cause: the test waits for what it asserts, or the index manager is fixed, or the emulator image is
      pinned to a known-good version with the reason recorded. No retry loop or skip that hides it.
- [x] Three consecutive green `live-tests` runs on main: 45614672152 (push, d84c34e2, 2026-11-25), 45799559503 (schedule, d84c34e2, 2026-11-26) and 45880985872 (push, 4e39d3a4, 2026-11-26, the TASK-990 push with its new live suites), all on the pinned emulator digest 09dcd04e.

## Out of scope

- Other Cosmos tests, unless the cause is shared.

## Human test plan

- N/A — fully covered by automated tests.

**Done 2026-11-26.** Three consecutive green live-tests runs on main after the pin (above). The pin stays until the vnext-preview emulator is fixed upstream; how to unpin is beside it in `live-tests.yml`.
