---
id: TASK-647
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Data sync: the Custom policy is skipped silently without a known pair; check timestamp round-trip

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `data-sync`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

Two read-only findings in the shared engine. (1) When both sides hold an entity with no known pair (lost or legacy
knowledge), the Custom policy falls to `GetWinner`'s `_ => "local"`, and `CustomConflictResolver` / `OnConflict` are
never asked (`Birko.Data.Sync/Internal/SyncProviderBase.cs:229,284`). (2) UNVERIFIED: the version is
`UpdatedAt.ToString("O")` recorded from the in-memory source and compared with the destination's read-back
(`:536,179-180`). A backend that changes DateTime Kind or precision on round-trip would read as changed every run, a
perpetual update or conflict. Also: a failed conflict resolution is reported twice (`OnError` and a `SyncError`,
`AsyncSyncProvider.cs:520-532`).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] (1) decided and tested
- [ ] (2) measured live against the SQL providers, MongoDB, RavenDB and ElasticSearch; fixed if it drifts
- [ ] A failure is reported once
- [ ] Red on the current code; the spec `docs/specs/data-sync.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-647` — leave empty until then._
