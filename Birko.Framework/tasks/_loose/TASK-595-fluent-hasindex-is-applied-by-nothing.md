---
id: TASK-595
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-033]
pr: null
github-issue: null
jira-key: null
---

# Fluent `HasIndex` is applied by nothing, so `Birko.Models.Users.SQL`'s lookup indexes likely exist on no database

## Context

Found 2026-10-14 while building `Birko.Security.Grants.SQL` (TASK-594), which measured it on SQLite: a model mapped
through `Birko.Models.SQL` gets its table, columns, primary key and `IsUnique()` constraints, but **no index from
`FieldBuilder.HasIndex(...)`**. Only `sqlite_autoindex_*` (the key and the unique constraint) appeared in
`sqlite_master`.

Why, from the code:
- `Birko.Models.SQL/Mapping/ModelMapRegistry.ApplyToDatabase` applies the table name, column name and the primary /
  unique / required / auto-increment flags. Its doc comment says `HasIndex` is "mapping metadata only".
- `Birko.Models.SQL/CLAUDE.md` says to declare indexes "via the model's SQL field attributes / migrations".
- The CR-L324 remediation (EPIC-014 STORY-027, batch BZ) **added `HasIndex` to `Birko.Models.Users.SQL`** on
  `UserRole.UserGuid` / `RoleGuid`, `UserTenant.UserGuid`, `RolePermission.RoleGuid`, `UserLogin.UserGuid` and the
  `UX_UserLogin_Provider_ProviderKey` composite, to fix "membership lookups would table-scan". It recorded them as
  "consumed by migrations". But `Birko.Data.Migrations.SQL` never reads `FieldDescriptor.IndexName`; a grep finds no
  reader. So the fix most likely does nothing, and the table scans it was meant to remove are still there (rule 51:
  a declaration nothing honours).

Not yet measured on the Users mappings themselves. Measure before fixing (rule 54).

`UX_UserLogin_Provider_ProviderKey` is named as a **unique** index. If it doesn't exist, two `UserLogin` rows can share
(Provider, ProviderKey), which is the lookup key of an external sign-in. That makes this more than a performance
issue: the external-login lookup pattern this framework's README teaches (find by provider + subject) assumes it is
unique.

`Birko.Security.Grants.SQL` sidestepped the gap: it creates its indexes through the provider's `IIndexManager`
(`GrantSqlIndexes.EnsureAsync`) instead of declaring them.

## Acceptance criteria

- [ ] Measured on SQLite: the Users mappings' declared `HasIndex` indexes are present or absent in `sqlite_master`
      after `ApplyToDatabase` + store init. The finding is confirmed, or falsified and this task closed with the
      measurement (rule 54).
- [ ] If confirmed: a fluent `HasIndex` reaches the DDL. Either `ApplyToDatabase` carries it into the SQL layer's
      index declarations, as the attribute path does, or the API stops offering it (rule 55: a builder honours a
      declaration or refuses it). The composite `order:` is preserved, and a unique index is created as unique.
- [ ] `UX_UserLogin_Provider_ProviderKey` is shown to reject a duplicate (Provider, ProviderKey) on SQLite.
- [ ] Docs corrected: `Birko.Models.SQL/CLAUDE.md`, the `ModelMapRegistry` doc comment, and the STORY-027 batch BZ
      note's "consumed by migrations".
- [ ] Regression tests fail with the fix removed.

## Out of scope

- `Birko.Security.Grants.SQL`'s indexes. They are created through `IIndexManager` and stay that way unless this fix
  makes `HasIndex` the simpler producer, which would be a follow-up there.
- `HasMaxLength` / `HasPrecision` being metadata only too. That is the same mechanism, so a fix here should say
  whether it covers them. If it doesn't, they get their own task.

## Human test plan

N/A — fully covered by automated tests. Whether an index exists is read from the database catalogue.

## Implementation plan

_Populated by `/tasks plan TASK-595` — leave empty until then._

## Progress log

- step 2 — picked; the last of the two defects the user named (TASK-596 done first: silent wrong data outranks a missing index)
