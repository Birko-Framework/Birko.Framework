---
id: TASK-205
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-12
depends-on: []
blocks: []
findings: [SH-H023-followup]
pr: null
github-issue: null
jira-key: null
---

# A qualified `Table.Column` is emitted unquoted while `FROM "Table"` is quoted — PostgreSQL folds them apart

## Context

Filed from [[TASK-111]]'s review, which found the task's own stated rationale did not hold for what its
code emits. The defect is **pre-existing and framework-wide**, not introduced by TASK-111.

`AbstractConnectorBase.CreateSelectCommand` emits the FROM clause through `QuoteIdentifier`
(`AbstractConnectorBase.cs:594`), so a table reaches SQL as `FROM "RRows"` — case-preserved. Every
*qualified column* reference, however, is built as bare `Table.Column`:

- the SELECT list, via `Table.GetSelectFields(withName: true)` (`AbstractConnector_Select.cs:95`)
- WHERE conditions from the expression path, via `ResolveColumnName(exprType, name, withTableName: true)`
  (`DataBase.cs:708`, `:835`)
- WHERE conditions from the rule path, via `DataBase.ResolveRuleField` ([[TASK-111]])
- ORDER BY keys in a multi-table select, via `ResolveOrderFields` ([[TASK-110]])

On PostgreSQL an unquoted identifier folds to lower case, so `RRows.label_col` looks for table `rrows`
while the statement declared `"RRows"`. The result is `42P01 missing FROM-clause entry for table "rrows"`.

**Any Birko model whose table name is not already lower case is therefore unusable on PostgreSQL for any
multi-column read** — which is every read. That is a strong claim and it is *inferred from the emit sites,
not yet measured against a live server*; verifying it is step 1 of this task, and the finding should be
downgraded or closed if the DDL path turns out to lower-case table names somewhere first.

Note the asymmetry that makes this survivable today and confusing tomorrow: **column** identifiers are
deliberately bare (CR-H087, [[TASK-110]], [[TASK-111]] — quoting them would break the same way, because the
DDL creates them unquoted), and **table** identifiers are deliberately quoted. The two conventions are each
individually defensible and they contradict each other the moment a name is qualified.

## Approach

Decide once, for all four emit sites, and record it in `CLAUDE.md § Conventions` next to the existing
identifier rule — which currently notes this gap but does not resolve it.

The options, in rough preference order:

1. **Quote the table part of a qualified reference** (`"RRows".label_col`). Consistent with the FROM
   clause, and leaves the column bare as the existing rule requires. Needs `QuoteIdentifier` to be
   reachable from `GetSelectName`, which today has no connector.
2. **Stop quoting table names in FROM.** Symmetric the other way and a much larger blast radius — it
   breaks any table whose name is a reserved word, which is presumably why the quoting is there.
3. **Only qualify when a statement actually joins** (what `ResolveOrderFields` already does via
   `tableList.Length > 1`). Shrinks the problem to joins rather than removing it, but it is cheap and it
   would immediately fix the single-table case that is the overwhelming majority.

Whichever is chosen, the four sites above must end up agreeing — the whole lesson of TASK-110/111 is that
one identifier rule split across sinks gets rediscovered rather than reused.

## Acceptance criteria

- [ ] **Measured against a live PostgreSQL** (the STORY-042 Docker tier, if it exists by then): a model
      with a mixed-case table name, a multi-column read, and the actual error — or evidence that there is
      no defect, in which case cancel this task and correct the three places that now describe it
- [ ] A decision recorded among the options above, with the reason, including why the rejected ones lose
- [ ] All four emit sites agree, and a test pins the agreement rather than each site separately
- [ ] `CLAUDE.md § Conventions` — the identifier rule is updated from "this gap exists" to what the rule now
      is
- [ ] MySQL / MSSql / SQLite are checked for the same asymmetry; each dialect folds differently and a fix
      correct for PostgreSQL could break another
- [ ] `/specs regen` for `filter-expression-translation` + `schema-index-and-ddl`, spec diffs reviewed

## Out of scope

- The bare-**column** convention itself. It is settled and correct (CR-H087, [[TASK-110]], [[TASK-111]]);
  this task is only about the table qualifier in front of it.
- [[TASK-111]]'s rule-field resolution, which matches the surrounding convention deliberately rather than
  diverging in one sink.

## Human test plan

- [ ] Against a real PostgreSQL, create a Birko model with a mixed-case table name (`[Table("MixedCase")]`),
      insert a row and read it back. This is the check that decides whether the task is real, and it cannot
      be done against SQLite — SQLite is case-insensitive for identifiers and will pass either way, which is
      precisely why this has survived unnoticed.

## Implementation plan

_Populated by `/tasks plan TASK-205` — leave empty until then._
