---
id: TASK-197
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
created: 2026-08-09
completed: 2026-08-09
depends-on: []
blocks: []
findings: [SH-H037]
pr: b0dec59 + b5d8eef (Birko.Data.SQL), 1b554a8 + 2bd177a (Birko.Data.SQL.Tests)
github-issue: null
jira-key: null
---

# `TimeOnly` had no column mapping — and after [[TASK-112]] it took the whole entity down

## Context

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs` — `CreateAbstractField`. Reported from the consumer
side as **Symbio TASK-361**.

`TimeOnly` was one more BCL value type the dispatch had no arm for. That was survivable while an
unmapped type was silently skipped: it cost the column and nothing else — the SH-H037 shape. TASK-112
turned the fallthrough into a `FieldAttributeException`, and because the throw is raised at **table
load** rather than on the query that touches the column, a single `TimeOnly` property started
returning 500 on **every** route of the owning entity: read, write, list and delete alike. The
consumer's model's only offence was an "opens at" schedule boundary.

**This is TASK-112's predicted blast radius arriving, and its prediction was incomplete.** Both the
CLAUDE.md entry and [[TASK-150]] named `char?`, `TimeSpan`, `DateTimeOffset` and collections as the
types a consumer might now trip over. `TimeOnly` was on neither list, and it is the one that fired.
The fail-fast was still the right call — it converted a silent drop into a loud, locatable failure —
but "we enumerated what this will break" was not true, and a survey of what consumers actually
declare would have been worth more than a survey of what the mapper omits.

## Approach

Stored as **fixed-width `HH:mm:ss` TEXT**, not `DbType.Time`:

- `AbstractConnectorBase` maps `DbType.Time` to `typeof(DateTime)`, so the value would round-trip
  through a type carrying a date component `TimeOnly` does not have, and the dialects disagree about
  what a bare TIME column even is — SQLite has no time type at all. `DbType.String` renders as
  TEXT/VARCHAR everywhere with no per-dialect special case.
- **The width is fixed because text compares lexically.** Unpadded, `9:05` sorts after `10:00`, so
  `<`, `>` and `BETWEEN` on a time column would quietly return the wrong rows — the same class of
  defect as [[TASK-196]], where a 10-character date string was compared against a full timestamp and
  the shorter prefix won.
- The colons are escaped and the format applied under `InvariantCulture`: `:` in a custom date/time
  format means "the culture's time separator", so a server-locale-dependent column would be
  unreadable by a differently configured replica.
- Sub-second precision is dropped deliberately — `TimeOnly` carries wall-clock schedule boundaries,
  and storing ticks would make equality against a caller-supplied `HH:mm` fail for reasons no caller
  could see.

`Read` is lenient in both arms so a column written before this mapping existed, or by a driver with a
native TIME type handing back `TimeSpan`, still loads. The non-nullable arm falls back to midnight
rather than throwing — a row that cannot be materialised takes down every read of the table, which is
the exact failure this field exists to remove. The nullable arm reports `null` instead, because
midnight is a real time that a range query would match.

## Acceptance criteria

- [x] A `[Table]` model carrying `TimeOnly` / `TimeOnly?` loads, and both map to a column
- [x] Stored zero-padded, so ordinal order over the stored text IS chronological order
- [x] Independent of the ambient culture's time separator
- [x] Both read arms distinguish "no boundary set" from midnight
- [x] Regression tests — `TimeOnlyMappingTests`, 19 tests, **all 19 red on revert of the dispatch
      arm**, reached through `DataBase.LoadTable` rather than by constructing the field
- [x] Finding id corrected: the working-tree code cited **SH-H038**, which is an unrelated
      ElasticSearch reindex finding (`b5d8eef` / `2bd177a`)
- [ ] `/specs regen` for `schema-index-and-ddl` — **still outstanding**

## Outcome

Landed 2026-08-09. Suite 417 pass (was 398); `Birko.Data.SQL.SqLite.Tests` 126 pass, unchanged.

Three things worth carrying:

- **The revert gives one cause and 19 symptoms.** Every test fails on `LoadTable`, not on its own
  assertion, because the fail-fast takes the whole table down. So the `Write`/`Read` contract checks
  are *not* independently witnessed by the revert — they pin the stored shape, which no revert of
  this change can exercise. Recorded rather than counted as 19 pieces of evidence.
- **Every test goes through `LoadTable` on purpose.** TASK-112's first run had all 15 provider DDL
  tests green with its fix reverted, because they built `new LongField(...)` by hand and the field
  classes survive a revert that only touches the dispatch. A test that constructs the object under
  test cannot witness a mapping.
- **A finding id carried in from working-tree comments was wrong and was propagated into two commit
  messages before anyone checked it against the register.** A wrong id is not cosmetic — it points
  every "which findings are closed" sweep at the wrong defect.

## Out of scope

- `char?`, `TimeSpan`, `DateTimeOffset` — still unmapped, still [[TASK-150]].
- Migrating consumer tables, exactly as in [[TASK-112]].

## Human test plan

N/A — covered by automated tests; there is no visual surface.
