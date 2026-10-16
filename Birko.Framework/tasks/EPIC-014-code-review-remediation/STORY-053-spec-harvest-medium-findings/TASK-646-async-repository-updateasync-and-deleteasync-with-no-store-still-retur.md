---
id: TASK-646
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

# Async repository UpdateAsync and DeleteAsync with no store still return silently

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `repository-contract`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A gap in [[TASK-162]]'s own fix (SH-M219): `AbstractAsyncRepository.UpdateAsync` / `DeleteAsync` still
`if (Store == null) return;` (`Birko.Data.Repositories/AbstractAsyncRepository.cs:70,81`), contradicting its doc ("every
operation now refuses") and the sync twin, which throws. Also in this area: `AddRepository<TRepository>(store, lifetime =
Scoped)` keeps the default Scoped but accepts only Singleton since TASK-162, so calling it without a lifetime always
throws (`ServiceCollectionExtensions.cs:52,58`); and sync bulk ViewModel `Delete(IEnumerable)` calls no `RemoveHash`,
unlike the async twin (`AbstractBulkViewModelRepository.cs:146-155`).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Both async methods throw with no store
- [ ] AddRepository's default lifetime is one it accepts
- [ ] The sync bulk delete prunes hashes
- [ ] Red on the current code; the spec `docs/specs/repository-contract.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-646` — leave empty until then._
