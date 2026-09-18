---
id: TASK-205
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: cancelled
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

> **Cancelled 2026-08-16 — the defect was real and is fixed, by [[TASK-211]] (read path) and [[TASK-216]]
> (write path).** Cancelled rather than closed `done` because *this* task did no work: it was filed
> 2026-08-12 from [[TASK-111]]'s review as an inferred, unmeasured claim, and the two tasks that actually
> measured and fixed it were filed and drained independently three days later. Kept, not deleted — it is
> the record of when the inference was first written down. See § Disposition.

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

_Never populated — the task was cancelled before `/tasks plan` ran. See § Disposition._

## Disposition

**The inference held, and it understated the blast radius.** This task reasoned from the emit sites that
PascalCase-named entities would be unreadable on PostgreSQL, and flagged that as "a strong claim inferred
from the emit sites, not yet measured against a live server". [[TASK-211]] measured it against PostgreSQL
16.4 and found it true and **wider**: not the multi-table joins this task's § Context named as the residue,
but *every* read of *every* such entity — `Read()` included — returning **zero rows silently**, because
`IsMissingTableException` classified the resulting `42P01` as "table missing, yield empty". The swallow hid
the bug that produced the error it swallowed, which is why nothing was ever red.

**Every acceptance criterion above is satisfied, by TASK-211 unless noted:**

| Criterion | Where it was met |
|---|---|
| Measured against a live PostgreSQL | PostgreSQL 16.4; **5 of 16** live tests fail on revert |
| Decision recorded, with why the rejected options lose | Alias over quoted-qualifier: the read path's qualifiers arrive function-wrapped (`LOWER(T.Col)`, `COALESCE`, the `.Date` rewrite), so per-producer quoting is a partial fix and a missed producer is the identical silent empty result |
| All four emit sites agree, pinned by a test | `FROM "Widgets" AS Widgets` makes them agree **by construction** — stronger than pinning four sites separately, and it covers producers not yet written. Shape pinned by `SelectTableAliasTests` |
| `CLAUDE.md § Conventions` states the rule | § *"A qualifier resolves against a bare ALIAS, not against a quoted table"* |
| MySQL / MSSql / SQLite checked for the same asymmetry | All three are case-insensitive for identifiers, so the asymmetry cannot bite — which is also why no offline suite can distinguish the fix from the defect. MSSql additionally rejects `DELETE FROM t AS a`, which is why [[TASK-216]] strips the write path's qualifier instead of aliasing it |
| `/specs regen` | `filter-expression-translation` regenerated. `schema-index-and-ddl` was **retargeted, not skipped**: no file the fix changed falls in its globs |

**Why this was not caught as a duplicate when TASK-211 was filed.** Both tasks were `_loose`, three days
apart, describing one defect in different words — this one from reading the emitters, TASK-211 from a live
failure. Nothing cross-checks a new `_loose` task against the existing pile, and neither task's `findings:`
id matched the other (`SH-H023-followup` here). Surfaced only by a `/roadmap` pass reading the two titles
side by side. The generalisable point is that **an inferred, unmeasured task is the shape most likely to be
silently re-filed later from evidence**, because the second filing does not recognise itself in the first
one's prose — worth a `duplicate-of` check at filing time rather than an audit three days on.
