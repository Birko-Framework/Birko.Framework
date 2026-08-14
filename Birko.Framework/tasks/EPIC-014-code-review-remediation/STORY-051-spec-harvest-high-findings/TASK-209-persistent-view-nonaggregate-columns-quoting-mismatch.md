---
id: TASK-209
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-110, TASK-128, TASK-129]
pr: null
github-issue: null
jira-key: null
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: []
---

# A persistent view's non-aggregate columns are created unquoted and read back quoted — every such view is unqueryable on PostgreSQL

## Context

Found by [[TASK-129]]'s own regression test (2026-08-14), which initially asserted that *every* column the
persistent read asks for appears in the generated DDL under exactly that spelling. It failed on the
non-aggregate columns, and the failure is real.

Two producers of the same column name disagree on quoting:

| Step | Code | Emits |
|---|---|---|
| View DDL | `ViewSelectSqlBuilder` → `Table.GetSelectFields(withName: true)` | `SELECT AvPersons.Name, …` — **unquoted**, no alias |
| Persistent read | `AbstractConnectorBase_View.CreatePersistentViewSelectCommand:84` | `SELECT "Name" FROM "AvTotals"` — **quoted** |

Measured DDL from TASK-129's fixture:

```sql
SELECT AvPersons.Name, COUNT(AvOrders.PersonId) AS "OrderCount", … FROM "AvOrders" INNER JOIN "AvPersons" …
```

On PostgreSQL an unquoted identifier folds to lower case, so the view's column is named `name`, while the read
asks for `"Name"` — case-sensitive — and gets `column "Name" does not exist`. **Every persistent view with a
non-aggregate column whose source column name is not already all-lower-case is affected**, which is the normal
case for this codebase's PascalCase models. Aggregates are *not* affected: TASK-129 made their DDL alias
quoted precisely so it round-trips against this reader.

**Why it has not been reported.** No provider suite runs against real PostgreSQL, and SQLite — which every
end-to-end view test uses — is case-insensitive for identifiers, so the mismatch is invisible there. MSSql and
MySQL are case-insensitive for column names under their default collations too. PostgreSQL is the only
supported provider that folds, and it is the one with no end-to-end coverage. TASK-129 deliberately did **not**
assert this either way, because encoding the current behaviour would bless it.

**Related but distinct.** `docs/specs/views-and-aggregation.md` already records a *third* disagreement in this
family: the persistent ORDER BY interpolates its key bare while the persistent SELECT list quotes its columns
("note the persistent SELECT list *does* quote its columns, so on that path alone the two disagree"). That
parenthetical describes the same root cause from the sort side. So there are three producers of persistent
column identifiers and they take **three different** positions on quoting — one quoted (SELECT), one bare
(ORDER BY), one bare-and-table-qualified (DDL).

**Update (2026-08-14, from [[TASK-207]]'s close-gate review) — this is now two defects, not one, and the
second one raises the priority.** The same unaliased non-aggregate projection also produces a **duplicate
output column name** whenever two view fields resolve to the same source column name. TASK-207 made both
collision shapes reach the DDL (previously one field was silently dropped before it got there), so measured
on SQLite with its `VkCollidingView` under `Persistent`:

```
DDL: SELECT VkPersons.Name, VkOrders.Total, SUM(VkOrders.Amount) AS "Total" FROM …
GetPersistentViewSelectFields() → 0=Name  1=Total  2=Total
```

The persistent read selects **by name**, so the aggregate binds to the non-aggregate's column and reads `70`
where `5` is correct; on MSSql and PostgreSQL `CREATE VIEW` rejects the duplicate outright. Both defects have
one cause — non-aggregates are projected unaliased and read back by source column — and **one fix closes
both**: alias non-aggregates by their view property, quoted, exactly as TASK-129 did for aggregates, and
return `Property.Name` uniformly from `GetPersistentViewSelectFields`. About four lines.

It was left to this task rather than taken inside TASK-207 because it changes the DDL of **every** persistent
view: a view created by an older build has columns named by source column, and after the change the read asks
for the view-property name, so **already-deployed persistent views must be recreated**. That migration call
is this task's to make, alongside the quoting decision it already owns.

## Approach

The decision is *which* convention wins, and it cannot be taken per sink — that is what produced three answers.
§ Conventions says column identifiers are emitted bare everywhere and that quoting one sink is what breaks
PostgreSQL; TASK-129 then had to quote the DDL aggregate alias because its reader quotes. Both are locally
correct, which means the *reader* is the odd one out.

1. **Unquote the persistent read** (`CreatePersistentViewSelectCommand`) so it matches § Conventions and the
   ORDER BY sink, then unquote TASK-129's aggregate alias to match. Makes all three bare and consistent, and
   is the option § Conventions points at. Requires re-checking TASK-129's aggregate round-trip.
2. **Quote all three**, including the DDL's non-aggregate projection (`AvPersons.Name` → the column needs an
   explicit `AS "Name"`, since a table-qualified projection has no quoted name of its own). Contradicts
   § Conventions' bare-identifier rule for the ORDER BY sink.

Option 1 looks right and is the one to cost first. **Whichever is chosen, verify it against real PostgreSQL** —
this defect exists precisely because no suite does, so a fix validated only on SQLite would be unverifiable in
exactly the same way.

## Acceptance criteria

- [ ] The defect is **reproduced on real PostgreSQL** first (a container is acceptable; record how it was run)
      — create a persistent view over a PascalCase model and read it back. Without a reproduction this task
      cannot distinguish a fix from a no-op, which is the trap that hid it
- [ ] All three producers of a persistent view's column identifiers agree: the DDL projection, the persistent
      SELECT list and the persistent ORDER BY. The chosen convention is recorded in `CLAUDE.md` § Conventions
      alongside the existing bare-identifier rule, since that rule is currently true of two sinks out of three
- [ ] A persistent view with non-aggregate PascalCase columns round-trips on PostgreSQL
- [ ] **The duplicate-output-column defect above is closed with the same change**: TASK-207's
      `VkCollidingView` under `Persistent` reads `OrderTotal = 70, Total = 5` (it currently reads
      `Total = 70`), and its shape-1 twin creates on MSSql/PostgreSQL rather than being rejected for a
      duplicated output name. TASK-207 left a corrected comment in
      `ViewFieldKeyCollisionTests.The_colliding_aggregate_and_non_aggregate_read_back_their_own_values`
      pointing here — turn it into an executing Persistent assertion as part of this task
- [ ] The migration consequence is recorded and decided, not discovered: aliasing non-aggregates renames the
      columns of **every** existing persistent view, so already-deployed views must be recreated. Say so in
      `CLAUDE.md` § Recent Updates as a consumer-facing ⚠, the way the TASK-112 mapper change did
- [ ] TASK-129's aggregate behaviour still round-trips — its
      `The_ddl_alias_is_quoted_exactly_as_the_persistent_read_quotes_it` is updated together with the reader if
      option 1 is taken, never left asserting a convention the code no longer follows
- [ ] Red-verified. Report the split as numbers and name any test that passes either way as a contract pin
      rather than as evidence. **A SQLite-only assertion is a contract pin here by construction** — say so
- [ ] `/specs regen views-and-aggregation`, and the spec's existing ORDER BY-vs-SELECT parenthetical is
      updated rather than left describing the old three-way split

## Out of scope

- The aggregate alias itself — [[TASK-129]] closed it and this task must not regress it.
- Adding a PostgreSQL integration tier for the whole framework (that is STORY-042's Docker tier). This task
  needs one reproduction, not a suite.
- The framework-wide `Table.Column` qualifier-quoting inconsistency noted in TASK-110's Outcome — same family,
  affects the on-the-fly path, and larger.

## Human test plan

N/A once the PostgreSQL reproduction is automated — the whole point of the first acceptance criterion is that
this must be machine-verified against a folding provider, since human inspection of SQLite output is what would
miss it again.

## Implementation plan

_Populated by `/tasks plan TASK-209` — leave empty until then._
