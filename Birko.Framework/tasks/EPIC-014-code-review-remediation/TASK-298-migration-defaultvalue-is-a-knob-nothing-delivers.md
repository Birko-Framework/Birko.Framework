---
id: TASK-298
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-07
depends-on: []
blocks: []
related: [TASK-264, TASK-296]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A migration can declare a column default, and no connector emits one

## Context — measured at TASK-264's Step 0

`ICollectionBuilder.WithField(..., object? defaultValue = null)` accepts a default, and
`FieldDescriptor.DefaultValue` carries it. Measured 2026-09-07: **no connector emits a `DEFAULT` clause
anywhere** — grepping `DEFAULT` across `Birko.Data.SQL/SQL/Connectors/` returns nothing, and
`FieldDefinition` composes only the type, `UNIQUE`, `NOT NULL` and `PRIMARY KEY`.

So the value is accepted and silently discarded. TASK-264 fixed the four descriptor properties whose
consequence was a *wrong* column (`MaxLength`, `Precision`/`Scale`, `ColumnName`, `IsIgnored`) and
deliberately left this one, because it is not a dropped assignment — the DDL support does not exist.

## Why it matters

§ Conventions' rule from TASK-296: **a knob may only offer what the mechanism can actually deliver.**
That entry is about `SqLiteSettings.JournalMode` refusing the four non-persistent journal modes rather
than accepting them and doing nothing; this is the same shape in the migrations API. A migration author
writing `defaultValue: 0` on a new `NOT NULL` column reasonably expects existing rows to be backfilled,
and on every provider they get either a failed `ALTER` or a column of nulls.

Note the asymmetry with `[DefaultValue]` on the attribute path — worth checking whether that is honoured
before deciding, because if it is, the two doors give different answers about the same declaration
(§ TASK-274).

## What to decide

1. **Emit the DDL.** `DEFAULT <literal>` is per-provider but not hard, and the escaping producer already
   exists — `Birko.Data.SQL.SqlLiteral.EscapeLiteral` (TASK-253), which is exactly the sink type it was
   built for: a constant in a statement that takes no parameters. Needs a decision on typed literals
   (booleans, dates, `CURRENT_TIMESTAMP`-style expressions vs values) — an expression default is the
   uncontainable-parameter shape TASK-260 dealt with, so it must not be a raw passthrough.
2. **Refuse it.** Throw when `DefaultValue` is set, naming the limitation. Affordable — TASK-247/259
   measured 0 production consumers of `ISchemaBuilder` — and honest, but it removes an API surface
   somebody may be about to use.
3. **Document it as inert.** Cheapest, and the weakest: this is the state that already exists, minus the
   silence.

Option 1 if the attribute path honours defaults (the two doors must agree); otherwise 2.

## Acceptance criteria

- [ ] Established whether the attribute path (`[DefaultValue]` or equivalent) honours a default, so the
      two doors are made to agree rather than diverge.
- [ ] `DefaultValue` either reaches the emitted column on all four providers, or is refused with a message
      naming what to do instead — not accepted and discarded.
- [ ] If emitting: the literal goes through `SqlLiteral.EscapeLiteral`, and an *expression* default is
      either refused or given a structured form rather than interpolated raw (§ TASK-260).
- [ ] Proven able to fail, with a named revert per provider.

## Out of scope

- `MaxLength` / `Precision` / `Scale` / `ColumnName` / `IsIgnored` — TASK-264 owns those and is done.
- Backfilling existing rows on `ALTER TABLE ADD` with a default. That is a data migration decision, not
  a DDL one, and it needs its own answer.

## Human test plan

- [ ] N/A — mechanical; the proof is the emitted DDL and a live column default per provider.

## Implementation plan

_Populated by `/tasks plan TASK-298` — leave empty until then._
