---
id: TASK-648
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

# Async alias reindex reports Failed after a completed swap when the old-index delete is cancelled

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `schema-index-and-ddl`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

In `ReindexWithAliasAsync`, an `OperationCanceledException` from the old-index delete skips the cleanup handler and
reaches the outer catch-all (`Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:323,330`): the result is
`Failed` although the alias already points at the new index, and a retry dies at "already exists". `ReindexAsync`'s
catch-all also turns any cancellation into a Failed result (rule 9: a cancellation is the caller's, never rewrapped).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] A completed swap reports success (with CleanupError) whatever happens to the delete
- [ ] A cancellation propagates as cancellation
- [ ] Red on the current code; the spec `docs/specs/schema-index-and-ddl.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-648` — leave empty until then._
