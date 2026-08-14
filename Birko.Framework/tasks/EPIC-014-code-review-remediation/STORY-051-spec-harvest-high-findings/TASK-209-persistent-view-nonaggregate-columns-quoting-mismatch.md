---
id: TASK-209
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-110, TASK-128, TASK-129]
pr: 6fa64c5
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

**Blocked on infrastructure, twice measured (2026-08-14).** Two `/fix-next` runs ranked this task **top of
the pool** on severity and reachability and could not take it: acceptance criterion 1 requires a
reproduction against real PostgreSQL, and this machine has **no Docker daemon** (`failed to connect to the
docker API at npipe:////./pipe/dockerDesktopLinuxEngine`), no `psql` on `PATH`, and nothing listening on
5432. Status deliberately left `todo` rather than `blocked` — the blocker clears the moment Docker Desktop
is started, and marking it blocked would hide the highest-value item in the pool. **Start Docker before
picking this**, or the run stalls at step 3.

Note the duplicate-column half added above **is** reproducible without PostgreSQL (measured on SQLite), so
if PG stays unavailable this task could legitimately be split: the duplicate closed on SQLite evidence, the
quoting mismatch left waiting. That split has not been made — one fix closes both, and doing half would
change the persistent DDL twice.

## Measured on real PostgreSQL (2026-08-14)

**How it was run** (criterion 1 asks this to be recorded). No Docker: EDB's portable binaries zip
(`get.enterprisedb.com/postgresql/postgresql-16.4-1-windows-x64-binaries.zip`, 339 MB) extracted into the
session scratchpad, then

```
initdb  -D data -U birko -A md5 --pwfile=pw.txt -E UTF8
pg_ctl  -D data -l pg.log -o "-p 55432 -c listen_addresses=127.0.0.1" start
```

No admin rights, no installer, no Windows service, port 55432, deleted afterwards. `Npgsql 9.0.3` was
already referenced by `Birko.Data.SQL.PostgreSQL.View.Tests`.

**The base tables are the key fact, and it inverts the analysis.** `CreateTable` quotes the *table* and
emits *column* definitions bare, so on PostgreSQL every base column folds to lower case — measured:

```
AvPersons.guid, AvPersons.name, AvOrders.guid, AvOrders.personid, AvOrders.amount
```

So on PostgreSQL a column reference must be **bare** to resolve, and a table reference must be **quoted**.
Every sink that deviates is broken. There are four, and **the filed one is third in line** — two others
fail before it, so fixing only the persistent read changes nothing observable:

| # | Sink | Emits | Result on PG |
|---|---|---|---|
| 1 | View DDL SELECT list (`Table.GetSelectFields(true)`) | `AvPersons.Name` — table **unquoted** | `ERROR: missing FROM-clause entry for table "avpersons"` |
| 2 | View DDL JOIN `ON` | `"AvOrders"."PersonId"` — column **quoted** | `ERROR: column AvOrders.PersonId does not exist` |
| 3 | Persistent read (`CreatePersistentViewSelectCommand`) | `SELECT "Name"` — column **quoted** | `ERROR: column "Name" does not exist` ← **the filed defect** |
| 4 | Aggregate DDL alias (TASK-129) | `AS "OrderCount"` — creates case-sensitive | agrees with #3 only; must move with it |

Verbatim generator DDL fails at #1. With #1 hand-fixed it fails at #2. Only with #1 and #2 fixed does the
view get created, and only then does #3 — the defect this task was filed for — become reachable.

**The correct end state is proven, not theorised.** Applying § Conventions' own rule — *quote table
identifiers, never quote column identifiers* — end to end:

```sql
CREATE VIEW "AvTotals" AS SELECT "AvPersons".Name, COUNT("AvOrders".PersonId) AS OrderCount
  FROM "AvOrders" INNER JOIN "AvPersons" ON ("AvOrders".PersonId = "AvPersons".Guid)
  GROUP BY "AvPersons".Name;
-- CREATE VIEW; columns: name, ordercount
SELECT Name, OrderCount FROM "AvTotals";   -- works
SELECT "Name", "OrderCount" FROM "AvTotals";  -- ERROR: column "Name" does not exist  (what ships today)
```

So § Approach **option 1 is right** (unquote the read, unquote TASK-129's alias) and **incomplete**: it
names three producers and there are four, two of which this task currently lists as out of scope. The
persistent `ORDER BY` already interpolates bare and is correct under this rule — it becomes the model, not
the outlier.

**Consequence for scope.** Sinks #1 and #2 are the "framework-wide `Table.Column` qualifier inconsistency"
that § Out of scope defers. The measurement shows they are not merely *related* — they are **in front of**
the filed defect, so this task cannot deliver a working persistent view on PostgreSQL without them.
Widening is therefore not scope creep; it is the minimum that makes the task's own acceptance testable.
**That decision is deferred to the user** rather than taken here.

**The reserved-word risk was raised, then measured, and it is void.** The concern was that an entity
property named after a reserved word survives sinks #2/#3 *because* they quote, and would break going bare.
Measured on the same server: the framework's own base-table DDL emits column definitions bare, so

```sql
CREATE TABLE "RwTest" (Guid uuid, Order text, Name text);
-- ERROR: syntax error at or near "Order"
```

A reserved-word column **cannot have its table created at all** on PostgreSQL today. There is therefore no
working case for unquoting to break — the breakage is pre-existing, framework-wide and upstream of every
view. (`Rank`, a non-reserved PascalCase name, creates fine.) A grep of `Birko.Models.*` / `Birko.Data.*`
for properties named after PostgreSQL reserved words returns only infrastructure types — `IndexedField.Order`,
`IndexColumn.Order`, `AggregateQuery.Limit/Offset`, `AbstractField.Table` — none of them `[Table]`-mapped
entities. **Scope decision (user, 2026-08-14): widen and fix all four sinks.**

**Scope boundary held.** All four sinks above are in the **view** path. Sizing them turned up a *fifth*,
outside it: `AbstractConnector_Select.cs:95` / `AbstractAsyncConnector_Select.cs:95` call the same
`Table.GetSelectFields(true)` for ordinary **multi-table joined SELECTs**, so those are broken on PostgreSQL
by the identical qualifier mechanism. Not fixed here — it is a different entry point with its own blast
radius, and fixing the view path does not depend on it. Filed separately.

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

- [x] The defect is **reproduced on real PostgreSQL** first (a container is acceptable; record how it was run)
      — create a persistent view over a PascalCase model and read it back. Without a reproduction this task
      cannot distinguish a fix from a no-op, which is the trap that hid it
- [x] All three producers of a persistent view's column identifiers agree: the DDL projection, the persistent
      SELECT list and the persistent ORDER BY. The chosen convention is recorded in `CLAUDE.md` § Conventions
      alongside the existing bare-identifier rule, since that rule is currently true of two sinks out of three
- [x] A persistent view with non-aggregate PascalCase columns round-trips on PostgreSQL
- [x] **The duplicate-output-column defect above is closed with the same change**: TASK-207's
      `VkCollidingView` under `Persistent` reads `OrderTotal = 70, Total = 5` (it currently reads
      `Total = 70`), and its shape-1 twin creates on MSSql/PostgreSQL rather than being rejected for a
      duplicated output name. TASK-207 left a corrected comment in
      `ViewFieldKeyCollisionTests.The_colliding_aggregate_and_non_aggregate_read_back_their_own_values`
      pointing here — turn it into an executing Persistent assertion as part of this task
- [x] The migration consequence is recorded and decided, not discovered: aliasing non-aggregates renames the
      columns of **every** existing persistent view, so already-deployed views must be recreated. Say so in
      `CLAUDE.md` § Recent Updates as a consumer-facing ⚠, the way the TASK-112 mapper change did
- [x] TASK-129's aggregate behaviour still round-trips — its
      `The_ddl_alias_is_quoted_exactly_as_the_persistent_read_quotes_it` is updated together with the reader if
      option 1 is taken, never left asserting a convention the code no longer follows
- [x] Red-verified. Report the split as numbers and name any test that passes either way as a contract pin
      rather than as evidence. **A SQLite-only assertion is a contract pin here by construction** — say so
- [x] `/specs regen views-and-aggregation`, and the spec's existing ORDER BY-vs-SELECT parenthetical is
      updated rather than left describing the old three-way split

## Out of scope

- The aggregate alias itself — [[TASK-129]] closed it and this task must not regress it.
- Adding a PostgreSQL integration tier for the whole framework (that is STORY-042's Docker tier). This task
  needs one reproduction, not a suite.
- The framework-wide `Table.Column` qualifier-quoting inconsistency noted in TASK-110's Outcome — same family,
  affects the on-the-fly path, and larger.

## Outcome

**What was wrong.** SQL views did not work on PostgreSQL at all — the one supported provider that case-folds
an unquoted identifier. Not degraded: a persistent view could not be created. `CreateTable` quotes the table
and emits column definitions **bare**, so every base column is stored folded while every table keeps its
PascalCase; a column reference must therefore be bare and a table reference quoted. Four view sinks
disagreed. All four now follow one rule — **quote tables, never quote columns** — which is what
§ Conventions already said and what the persistent `ORDER BY` was already doing.

**The filed fix would have been a no-op, and criterion 1 is the only reason we know.** The three failures
queue: unquoted qualifier, then quoted join column, then the quoted read that was actually filed. Fixing
only the read changes nothing observable. Reproduced on real PostgreSQL 16.4 run from the scratchpad via
EDB portable binaries — no Docker, no admin, no service (recipe in § Measured above).

**Closes two of TASK-207's residues as a side effect**: `GetPersistentViewSelectFields` now returns the view
property for every column, and every column is aliased, so two view properties over one source column can no
longer produce one duplicated output name.

**Split.** Live PostgreSQL: **3 of 3** new tests fail on revert. Offline: **9 of 567** across the five view
suites. Green: 10/10 live PG + 808/808 across 12 offline SQL suites.

**Judgement calls.**

- **`A_persistent_view_is_created_on_postgresql` was written as `NotThrow()` and passed against the unfixed
  code.** `CreateView` swallows the `PostgresException`. Rewritten to ask `information_schema.views`; that
  one change took the live split from 2/3 to 3/3. A "did not throw" assertion over a swallowing call stack
  asserts nothing — recorded in § Conventions, because it is the general reason this defect was invisible.
- **The reserved-word risk was raised and then measured away** rather than mitigated: such a column already
  cannot have its table created, so no working case existed to break.
- **Nine existing tests asserted the replaced convention and were updated, not weakened.** Criterion 4
  required exactly that. The rewritten quoting test now asserts the *property* (both halves agree) over the
  whole column set instead of a literal — the wider assertion that failed under TASK-129 and produced this
  task.
- **The scope boundary was widened deliberately and with the user's decision**, then held: all four sinks
  are in the view path. The fifth (on-the-fly, and plain joined SELECTs) is a different builder and is
  filed, not silently absorbed.

**Flagged, not fixed.**

- **[[TASK-211]]** — the on-the-fly view path is broken on PostgreSQL by the same mechanism through
  `AbstractConnector_CreateSelectCommand`, **and the error is swallowed so the caller gets an empty
  collection**. The swallow is the more serious half and is why nothing was ever red. `AbstractConnector_Select.cs:95`
  is very likely the same defect for plain multi-table SELECTs; that task owns checking it.
- The PostgreSQL tests are env-gated (`BIRKO_PG_HOST`) and so do not run in CI. A permanent tier is
  STORY-042's Docker work, not this task's.

## Human test plan

N/A once the PostgreSQL reproduction is automated — the whole point of the first acceptance criterion is that
this must be machine-verified against a folding provider, since human inspection of SQLite output is what would
miss it again.

## Implementation plan

_Populated by `/tasks plan TASK-209` — leave empty until then._

## Progress log

- step 2 — picked; top of the pool on severity and reachability across three consecutive runs. The
  Docker blocker recorded above is **resolved without Docker**: the user chose the portable-binaries
  route, so a real PostgreSQL 16.4 runs from the scratchpad (`initdb` + `pg_ctl` on a high port, no
  admin, no service, deleted afterwards) and criterion 1 is satisfiable as written. `Npgsql 9.0.3` is
  already referenced by `Birko.Data.SQL.PostgreSQL.View.Tests`, so no package change is needed.
  Two halves in scope, and only one of them needs the server: the **duplicate output column** (already
  reproduced on SQLite under TASK-207) and the **case-folding mismatch** (needs PG, or a folding model).
- step 3 — **verified against real PostgreSQL 16.4, and the finding is REAL but MIS-SCOPED. The fix as
  filed would be a no-op on PostgreSQL** — precisely the trap criterion 1 was written to catch. See
  § Measured on real PostgreSQL below. Not yet rescoped: the widening crosses this task's own
  § Out of scope, so it is a decision, not a correction. **No production code changed yet.**
- step 4 — layer: local (Birko.Data.SQL + Birko.Data.SQL.View)
- step 5 — fix in `AbstractField.GetSelectName`, `Table.GetSelectFields` (new optional `quoteTable`),
  `View.GetPersistentViewSelectFields`, `ViewSelectSqlBuilder` (projection + alias + `QuoteFieldReference`),
  `AbstractConnectorBase_View.CreatePersistentViewSelectCommand`, `DataBase_ViewOrderBy.ViewOrderFieldName`.
  Tests: new `PostgreSqlViewRoundTripTests` (live PG) + 9 updated across 5 suites. 10/10 live, 808/808 offline.
- step 6 — reverted production: **3 of 3** live PG tests failed; **9 of 567** offline. Fix-dependent (live) =
  `A_persistent_view_is_created_on_postgresql`, `The_created_columns_are_exactly_what_the_persistent_read_asks_for`,
  `A_persistent_view_round_trips_end_to_end_on_postgresql`. No contract pins among the new tests — the
  9 offline failures are the updated convention assertions, which are fix-dependent by construction.
- step 7 — respecced `views-and-aggregation`: replaced the duplicate-column scenario (now fixed) with three —
  the one identifier rule, the PostgreSQL round-trip, and distinct output columns for two view properties
  over one source column.
- step 8 — closed `done`; production `7bc56bc` (Birko.Data.SQL) + `6fa64c5` (Birko.Data.SQL.View); tests
  `7662ca1`, `fd8612c`, `6f7f1b7`, `c6d2dbd`, `9b335b7`. § Conventions gained the rule and TASK-129's
  superseded bullet is marked. `tasks/README.md` not regenerated (unchanged reason: uncommitted intake).
