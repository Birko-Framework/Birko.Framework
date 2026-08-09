---
id: TASK-150
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: [SH-H037]
pr: null
github-issue: null
jira-key: null
---

# `char?`, `TimeSpan` and `DateTimeOffset` have no column mapping — they now fail loudly instead of quietly

## Context

Filed from [[TASK-112]]'s Outcome, where the fail-fast turned each of these from a silent drop into a
table-load failure. That change is the improvement; this task is the resolution.

TASK-112 mapped `long` / `short` / `double` / `float` / `byte[]` and made an unmappable property throw
`FieldAttributeException` rather than producing no column.

> **This list was incomplete, and the omission reached a consumer before the list did.** `TimeOnly`
> was on neither this task nor TASK-112's CLAUDE.md note, and it is the type that actually fired —
> Symbio hit it on a schedule model and lost every route on the entity, since the throw happens at
> table load. Mapped and closed separately as [[TASK-197]]. The lesson for whoever picks this up: an
> inventory of what the *mapper* omits is worth less than an inventory of what *consumer models
> actually declare*. Grep the consumer trees before assuming the three below are the remainder.

Three CLR types are known to be on the wrong side of that line:

- **`char?`** — the sharpest case, and the one that proves the fail-fast was worth having. `char` maps to
  `CharField(length: 1)`, but the dispatch tests `PropertyType == typeof(char)`, which `Nullable<char>`
  fails; it is not an enum either, so it falls through. It was **silently dropped before TASK-112** — the
  exact SH-H037 data-loss shape, found only because the throw surfaced it. The fix looks like one arm
  next to the existing `char` one, but see the open question below.
- **`TimeSpan`** — no `ConvertType` arm on any provider either, so it needs a storage decision (ticks as
  `BIGINT`? an interval type where one exists?) before a field class means anything.
- **`DateTimeOffset`** — the closest to free: **all four** `ConvertType` implementations already have a
  `DbType.DateTimeOffset` arm (`TIMESTAMPTZ` / `DATETIME` / `DateTimeOffset` / SQLite `INTEGER`). It was
  left out of TASK-112 because **SQLite has no native type for it** and its current arm is plainly wrong,
  so the round-trip needs designing rather than wiring.

Collections and complex types are **not** in scope and should keep throwing — `[NotMapped]` is the
answer there, and that is what the message says.

## Approach

Take `char?` first; it is a genuine defect with a live data-loss history, while the other two are
capability gaps that were never claimed to work.

**Check `CharField` before adding the arm.** `schema-index-and-ddl` records a scenario stating that
`CharField.Read` assigns `reader.GetString(index)` via `Property.SetValue`, so a `char` property raises a
type mismatch at runtime — i.e. `CharField` round-trips `string` properties, not `char` ones. If that
holds, `char` is *already* broken on read and mapping `char?` onto the same class would ship a second
broken mapping. Verify it by hand (TASK-112's step 3 found the finding's own cost model wrong; do not
trust this paragraph either) and fix `char` in the same pass if it does.

## Acceptance criteria

- [ ] `char`'s existing read path is verified by hand and fixed if the `GetString` → `char` mismatch is
      real — a `char` property round-trips through `Write()` / `Read()` end-to-end
- [ ] `char?` maps to a column and round-trips, including a null
- [ ] **Decide** `TimeSpan` and `DateTimeOffset` explicitly — map (with the SQLite storage choice named
      and justified) or record them as permanently `[NotMapped]`-only, with the reasoning
- [ ] Whatever stays unmapped still throws, and the message still names the opt-out
- [ ] Regression tests, with the step-6 revert split recorded by name
- [ ] `/specs regen` for `schema-index-and-ddl` — the *Nullable char raises the unmapped-type failure*
      scenario and the `CharField` read scenario both change

## Out of scope

- Collections, complex types and object graphs (`FieldType.Json`) — these should keep failing loudly.
- Migrating consumer tables, exactly as in [[TASK-112]].

## Human test plan

N/A — covered by automated tests; there is no visual surface.

## Implementation plan

_Populated by `/tasks plan TASK-150` — leave empty until then._
