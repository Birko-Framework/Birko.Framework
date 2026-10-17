---
id: TASK-640
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB migrations put update keys, the collection name and RenameField's fields into RQL unescaped

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `migrations`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`RavenDBDataMigrator` interpolates update keys (`this.{key}`) and the collection (`FROM '{collection}'`) into the RQL
text raw (`Birko.Data.Migrations.RavenDB/Context/RavenDBDataMigrator.cs:32,37,61`); filter fields are validated by
`RqlField` since TASK-152, these are not. `RavenDBSchemaBuilder.RenameField` does the same with the collection and both
field names (`Context/RavenDBSchemaBuilder.cs:80`). The shape of an injection (rules 33, 45).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Every caller name reaching RQL is validated (or passed as a parameter where RQL allows), with a refusal test
- [x] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced live on RavenDB (local Docker), against four sinks** (the harvest named three; `DropCollection`'s
`FROM '{name}'` is the fourth):
- `DropCollection("Accounts' WHERE Name = 'b")` deleted only `b` from `Accounts`: the name rewrote the query.
- An update key of `Role = 'admin'; this.Note`, and a `RenameField` new name of `Role = 'admin'; this.Label`, were
  accepted and wrote `Role`.
- `UpdateDocuments` and `DeleteDocuments` with the crafted collection failed loudly instead (`RavenException`, two
  `WHERE` clauses), since a filter follows the collection there.

**Decision: escape the collection, refuse the field.** A collection name cannot be a query parameter, and an apostrophe
in one is legitimate. A probe measured that RQL accepts `'O\'Brien'` (also `"O'Brien"` and `'O''Brien'`), each
matching the one document. So `RqlCollection` makes it one string literal, with `\` and `'` escaped (rule 45:
contain, don't refuse, where the name is data). A field reaches the patch script as `this.<name>`, which is grammar,
not a literal. So update keys and both `RenameField` names go through TASK-152's `RqlField`, as filter fields already
did: a plain dotted identifier path, anything else `NotSupportedException` (rule 33). Passing names as `this[$k]`
parameters was rejected because it changes a dotted path's meaning: `this["Address.City"]` is a flat key, not the
nested field.

**Red check, one neutralisation at a time.** With escaping off, 6 tests failed: the drop/delete and rename narrowing,
the quote-and-backslash collection, and 3 of the literal cases. With validation off, 5 failed: the update key,
`RenameField`, and 3 refusal cases. Tests: `Task640RqlNameLiveTests` (15: 7 live, 8 offline).

**Regression:** `Birko.Data.Migrations.RavenDB.Tests` is the only suite importing the project: 38 of 38, live.

**Docs:** the spec `migrations` (the RavenDB rename, the update requirement, a new scenario),
`Birko.Data.Migrations.RavenDB/CLAUDE.md`, and Recent Updates (the oldest entry rolled into CHANGELOG).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-640` — leave empty until then._
