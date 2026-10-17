---
id: TASK-651
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
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

- [x] Reproduced (or refuted, with the measurement)
- [x] Each item fixed or recorded as accepted, one line each
- [x] Red on the current code; the spec `docs/specs/store-crud-contract.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-651` — leave empty until then._

## Resolution (2026-10-17)

Reproduced by tests on HEAD in a clean worktree: **12 red**: Rules 3, Patterns 2, Security 3, Migrations.ES 1, ES 2,
Views 1. One line per item:

- **Doc comments, fixed:** `DuplicateCreate` (same *type* on every provider, message per provider), `IsDuplicateKeyException`
  (create → DuplicateCreate, update → UniqueViolation), the stray `<summary>` blocks in `MigrationFilter`, `OrderBy.ThenBy`,
  `StoreAggregationHelper.ParseToTime` and `AbstractAsyncBulkViewModelRepository.BulkStore`, and the `DataBase.cs` CASE example.
- **`RuleSet.Rules` remark, fixed in code:** the setter now copies (`new RuleList(value)`), so the remark is true.
- **`Connector == null` after `InitCore`, accepted:** these checks are not rule-53 fallbacks. They narrow the nullable flow
  for the compiler and guard a subclass that skips `InitCore`. Removing them gains nothing.
- **`RuleGroup.Rules = null`, fixed:** throws `ArgumentNullException` at assignment, not NullReferenceException at evaluation.
- **`DictionaryRuleContext`, fixed:** two keys equal only ignoring case make the lookup throw `InvalidOperationException`
  naming both. Before, the answer depended on enumeration order.
- **ES `CopyData` with `"{}"`, fixed:** it uses `MigrationFilter.IsExplicitMatchAll`, like every other backend.
- **`PagingOrder` Guid key, fixed:** OrdinalIgnoreCase. `ByName("guid")` appended Guid twice. A Count==0 guard was tried
  and dropped: `OrderBy`'s constructor is private, so it was unreachable (rule 53).
- **`ThrowIfTruncated` and a smaller caller size, accepted:** remarked on `BuildGroupByAggregation`. A caller's smaller
  size is a requested cap, not a truncation.
- **ES adapter `AsInt`, fixed:** non-negative int, long, short and byte, or a digit string. A negative int was accepted.
- **X-Forwarded-For with a non-address hop, fixed:** answers `fallbackIp`. X-Real-IP and CF-Connecting-IP count only
  when they parse. The returned value is the normalised address.
- **JWT Issuer/Audience at issue, accepted:** TASK-165 pinned `A_provider_without_an_issuer_can_still_generate`, and
  the regression run caught the conflict. Issuing without them is a kept contract, because a token can be minted for a
  validator other than this provider (NFC and the OAuth server call `GenerateToken`). Changing it is a decision for the
  owner, not a cleanup.
- **MongoDB text-index field order, measured:** a `{title, body}` declaration reads back as weights `{body, title}`.
  The drift detector now compares text fields as a set. Before, this was false `ColumnsDiffer`.
- **Cross-assembly `[ViewField]`, fixed:** `IsForeignFieldAttribute` walks base types, so `[ViewField]` and
  `[AggregateField]` from another assembly copy of the attribute types are no longer read as columns.

Specs updated: validation-and-rules, migrations, specifications-and-paging, schema-index-and-ddl and
security-and-authorization (client address only). store-crud-contract only had doc-comment changes. Regression: 131 importer suites live, 7289 passed; the one failure was the TASK-165 pin above, and with the issue-side change reverted the JWT suite is 48/48.
