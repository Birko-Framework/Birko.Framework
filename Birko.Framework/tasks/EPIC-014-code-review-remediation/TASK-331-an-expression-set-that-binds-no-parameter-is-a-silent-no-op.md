---
id: TASK-331
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P1
assignee: ai
created: 2026-09-09
depends-on: []
blocks: []
related: [TASK-308]
findings: [SH-H024]
pr: null
github-issue: null
affects: [Birko.Data.SQL]
---

# An expression-valued UPDATE that binds no parameter issues no statement at all, silently

## Context

Spawned at [[TASK-308]] while writing the opt-out test for `SH-H024`. Not that finding's root cause — a
different mechanism at a different layer — and out of TASK-308's scope, so it gets an id rather than an
`## Resolution (2026-10-10)

**The callers, measured.** `Update(tableName, fields, values, conditions, isExpressionValues, allowAllRows)` (sync
and async twins) has five:

| Caller | `isExpressionValues` | Empty `values` means |
|---|---|---|
| `Update(Table, object model, …)` | false | a model with no mapped fields — `fields` is the select list, `values` its written values |
| `Update<T,P>(…, IDictionary<Expression, object>, …)` | false | no assignments — `fields` empty too |
| `Update(Table, IDictionary<string, object>, …)` | false | nothing to set, but `fields` is the **whole select list** — running would emit `col = @SETcol` for every column with no parameter bound |
| `Update<T,P>(…, IDictionary<Expression, Expression>, …)` | **true** | only that the SET clauses bound no constant — `fields` holds them |
| `DataBaseBulkStore` / `AsyncDataBaseBulkStore` `Update(filter, PropertyUpdate<T>)` | **true** | never: `PropertyUpdateSqlTranslator` binds one parameter per assignment (`values.Add(parameter, value ?? DBNull.Value)`), so `values` is empty only when `fields` is |

**Fix.** The gate is `isExpressionValues ? fields.Any() : values.Any()` — the existing flag is the discriminator, no
new one. The value path is untouched; the store path is unchanged (both counts are equal there). The parameter
loop tolerates a null `values` on the expression path.

**Proof.** `PredicateMistranslationEndToEndTests` (SQLite, real file database, values read back): TASK-308's pin
inverted to `An_expression_SET_that_binds_no_parameter_issues_its_statement` (comment records where the line
moved), plus its async twin, column arithmetic (`Amount + Amount` → 20), and a parameter-free SET with no filter now
**refused** as a whole-table write (before: returned before the guard). **Red before the fix: 4 of 4.**
`An_empty_SET_payload_issues_nothing_on_every_door` (5 doors incl. async) passed before and after — a **contract
pin**, not evidence. SQLite 440/440, Birko.Data.SQL 700/700. Live MSSQL / PostgreSQL / MySQL suites not run (the
change is in the shared base connector, no provider override). Spec: scenario added under "An unrecognised call
in an UPDATE SET value is refused" in `docs/specs/filter-expression-translation.md` (hand edit, not a regen).

## Out of scope` sentence.

`AbstractConnector_Update.Update(string tableName, IDictionary<int, string> fields,
IDictionary<string, object> values, …)` opens with:

```csharp
if (values != null && values.Any())
{
    …everything…
}
```

On the **expression**-valued path (`Update<T, P>(…, IDictionary<Expression, Expression>, …)`, which routes
through `DataBase.ParseExpression`) `values` holds only the constants the SET expressions *bound*. A SET
expression that binds none therefore makes the method return having issued nothing:

- `r => r.Name.ToUpper()` → `UPPER(Name)`, no parameter
- `r => r.A + r.B` → column arithmetic, no parameter
- `r => r.Name` → a bare column copy, no parameter

**No statement, no exception, no log entry.** The caller has asked for a well-formed, translatable update
and been told nothing.

## Measured, 2026-09-09

Against on-disk SQLite through the connector's expression-valued overload, 3 rows seeded with names
`r1`/`r2`/`r3`:

```
SET Name = UPPER(Name)  WHERE Amount = 10   ->  no throw; names still r1, r2, r3
SET Name = REPLACE(Name,'r','R') WHERE Amount = 10 -> applied; the 10-row reads R1
```

The only difference between the two is that `Replace` binds `'r'` and `'R'` as parameters while `ToUpper`
binds none. Pinned meanwhile by
`Birko.Data.SQL.SqLite.Tests.PredicateMistranslationEndToEndTests.An_expression_SET_that_binds_no_parameter_is_a_SILENT_NO_OP`,
which **asserts the defect** so it cannot be believed fixed — when this task lands, that test fails and
the failure is the instruction to invert it.

## Why P1 rather than P0

A silent no-op is a lost operation, not a wrong one: nothing is corrupted and no row is destroyed, which
is what separates this from its parent finding. Against that, **reach is the same as SH-H024's and it was
measured there:** the two expression-valued `Update<T, P>` overloads have **no store-level caller** —
`PropertyUpdate<T>.Set` takes a value, not an expression — so only a consumer calling the connector
directly can reach it. ⚠ Latent is not a reason to downweight it (§ TASK-219/256: the window closes the
moment a consumer arrives); it is a reason not to overstate urgency.

## The question to settle first

The gate is not obviously wrong — it is there because on the **non**-expression path (`Update<T, P>(…,
IDictionary<Expression, object>, …)`) `values` *is* the payload, and an empty payload means "nothing to
set", where returning early is correct. So the two paths want different answers from the same line, which
is § TASK-274's *two doors onto one feature must give one answer* arriving as one door that needs two.

⚠ **Do not simply delete the `values.Any()` check.** Measure what an empty-payload call on the
non-expression path does today before changing it, and check the third caller —
`Update(Tables.Table table, IDictionary<string, object> values, …)`, which builds `fields` from
`table.GetSelectFields()` — since that one's `values` and `fields` are independent.

## Acceptance criteria

- [x] The three callers of `Update(tableName, fields, values, conditions, isExpressionValues, allowAllRows)`
      are enumerated and each one's meaning of an empty `values` is **measured**, not inferred
- [x] An expression-valued SET that binds no parameter issues its statement, asserted as the **value read
      back** from a real database — never as the absence of an exception
- [x] An empty payload on the non-expression path still does whatever it does today, asserted, so the fix
      is distinguishable from removing the guard
- [x] `isExpressionValues` is the discriminator if a discriminator is needed — it already reaches this
      method and already says which path the caller took; do not add a second flag
- [x] Proven able to fail
- [x] TASK-308's pin
      (`An_expression_SET_that_binds_no_parameter_is_a_SILENT_NO_OP`) is **inverted, not deleted**, with a
      comment recording where the line moved
- [x] `/specs regen filter-expression-translation` if the SET-value contract changes

## Out of scope

- `SH-H024` itself — [[TASK-308]] closed it: an unrecognised parameter-bound call in a SET value is now
  refused rather than reflectively invoked with null arguments.
- The `PropertyUpdate<T>` path, which cannot reach this overload at all.

## Human test plan

- [x] N/A — fully covered by automated tests; the deliverable is a value read back from SQLite, which a
      human adds nothing to.
