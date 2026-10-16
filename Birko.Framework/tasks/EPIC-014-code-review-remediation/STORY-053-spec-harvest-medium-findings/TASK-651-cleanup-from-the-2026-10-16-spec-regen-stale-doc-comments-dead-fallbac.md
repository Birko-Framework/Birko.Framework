---
id: TASK-651
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cleanup from the 2026-10-16 spec regen: stale doc comments, dead fallbacks, minor inconsistencies

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `store-crud-contract`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

Small items found by the regen, none a behaviour defect on its own:
- Doc comments: `StoreException.DuplicateCreate` promises one message on every provider (InMemory/JSON/XML keep their
  own); `IsDuplicateKeyException` still says "create" (update uses it too); `RuleSet` remark "assigning copies" (stores the
  reference); `MigrationFilter.cs:41-51`, `OrderBy.cs:62-70` (`ThenByName`), `StoreAggregationHelper.cs:327-369`,
  `AbstractAsyncBulkViewModelRepository.cs:23-28` carry stray or duplicate `<summary>` blocks; `DataBase.cs:1497-1505`
  shows a stale CASE form.
- Dead fallbacks (rule 53): the SQL bulk stores' `Connector == null` checks after `InitCore` refuses
  (`DataBaseBulkStore.cs:316,340`, `AsyncDataBaseBulkStore`).
- Minor: `RuleGroup.Rules` accepts null and evaluation then throws NullReferenceException (`Birko.Rules/Core/RuleGroup.cs:13`);
  `DictionaryRuleContext` fallback is ambiguous for case-only-distinct keys; ES `CopyData` refuses a `"{}"` transform
  while the shared `RefuseTransform` treats it as none (`ElasticSearchDataMigrator.cs:82`); `PagingOrder`'s Guid-key check
  is case-sensitive; `StoreAggregationHelper.ThrowIfTruncated` ignores a caller's smaller size; ES adapter `AsInt` sign
  handling; X-Forwarded-For non-IP hop returned verbatim behind a trusted proxy (`AuthenticationService.cs:272`); JWT
  Issuer/Audience required only at validation (`JwtTokenProvider.cs:92`); MongoDB text-index field order
  (unverified, `MongoDBIndexManager.cs:250-256`); a latent cross-assembly attribute filter on `[ViewField]`
  (`DataBase_Field.cs:44-58`).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Each item fixed or recorded as accepted, one line each
- [ ] Red on the current code; the spec `docs/specs/store-crud-contract.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-651` — leave empty until then._
