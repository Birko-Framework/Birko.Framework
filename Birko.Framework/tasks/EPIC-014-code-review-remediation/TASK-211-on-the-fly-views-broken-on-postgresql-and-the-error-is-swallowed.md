---
id: TASK-211
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-209, TASK-110, TASK-216]
pr: f8d47b7
github-issue: null
jira-key: null
findings: []
---

# On-the-fly views are broken on PostgreSQL — and the error is swallowed, so they return an empty result

## Context

Measured by [[TASK-209]] against real PostgreSQL 16.4 (2026-08-14) while fixing the *persistent* view path.
Two defects, and **the second is worse than the first**.

**1. The same qualifier mechanism, through a different builder.** TASK-209 fixed the view **DDL** builder
(`ViewSelectSqlBuilder`) and the persistent read. The **on-the-fly** path composes its SQL in
`AbstractConnector_CreateSelectCommand` → `view.GetSelectFields()` and emits, verbatim from the server log:

```sql
SELECT PgPersons.Name, COUNT(PgOrders.PersonId) as OrderCount, SUM(PgOrders.Amount) as TotalAmount
  FROM "PgOrders" INNER JOIN "PgPersons" ON (PgOrders.PersonId = PgPersons.Guid)
  GROUP BY PgPersons.Name ORDER BY PgPersons.Name ASC
-- ERROR: missing FROM-clause entry for table "pgorders" at character 143
```

The `FROM` quotes the table; the projection, the `ON` clause, the `GROUP BY` and the `ORDER BY` do not. On
PostgreSQL the bare qualifier folds to `pgorders` and does not match the quoted relation. The fix is the one
TASK-209 established — **quote tables, never quote columns** — applied to this builder;
`Table.GetSelectFields` already accepts a `quoteTable` delegate for exactly this (added by TASK-209), so the
projection is a matter of passing `QuoteIdentifier`. The join / group / order emitters need the same.

**2. The exception is swallowed and the caller gets an empty collection.** This is the part to fix first.
The `PostgresException` above never reaches the caller: `SqlViewStore.QueryAsync` returned **zero rows** with
no error. A view that cannot execute reports "no results" — indistinguishable from a view that legitimately
matches nothing, which is a plausible wrong answer rather than a failure, and top of this project's severity
ladder. It is also why the breakage was invisible: nothing anywhere went red.

The same swallowing hides `CreateView` failures. TASK-209's first draft of
`A_persistent_view_is_created_on_postgresql` asserted `create.Should().NotThrow()` and **passed against the
unfixed code**, because `CreateView` swallowed `42P01` and reported success. That test now asks
`information_schema.views` instead. Any assertion of the form "the view operation did not throw" is
worthless until this is fixed.

**Scope note.** `AbstractConnector_Select.cs:95` / `AbstractAsyncConnector_Select.cs:95` call the same
`Table.GetSelectFields(true)` for ordinary **multi-table joined SELECTs**, so those are broken on PostgreSQL
by the identical mechanism. Not yet measured end-to-end, but it follows from the same emitter — check it as
part of this task rather than filing a third time. This is the framework-wide residue TASK-110's Outcome
first noted and TASK-209 deliberately did not cross.

## Measured on real PostgreSQL 16.4 (2026-08-15)

Same no-Docker recipe as TASK-209 (EDB portable binaries in the session scratchpad, `initdb` + `pg_ctl`,
port 55432, database `birkotest`, deleted afterwards). Docker is still unavailable on this machine, so the
recipe is now twice-proven. Probes in `PostgreSqlOnTheFlyViewTests`; **4 of 5 first-pass probes failed.**

**The filed defect reproduces exactly as described**, verbatim from the server log:

```sql
SELECT OfPersons.Name, COUNT(OfOrders.PersonId) as OrderCount, SUM(OfOrders.Amount) as TotalAmount
  FROM "OfOrders" INNER JOIN "OfPersons" ON (OfOrders.PersonId = OfPersons.Guid)
  GROUP BY OfPersons.Name ORDER BY OfPersons.Name ASC
-- ERROR: missing FROM-clause entry for table "oforders" at character 143
```
…and the store returned an **empty collection with no exception**.

**⚠ The premise is understated, and this is the finding that changes the task.** § Context says the residue
is "ordinary **multi-table** joined SELECTs". It is not multi-table — it is **every** SELECT:

```sql
SELECT OfPersons.Name, OfPersons.Guid FROM "OfPersons"
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 8
SELECT OfPersons.Name, OfPersons.Guid FROM "OfPersons" WHERE OfPersons.Name = $1
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 8
SELECT OfPersons.Name, OfPersons.Guid, OfOrders.PersonId, OfOrders.Amount, OfOrders.Guid
  FROM "OfPersons", "OfOrders"
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 8
```

`AbstractConnector_Select.cs:95` passes `GetSelectFields(withName: true)` **unconditionally**, so a
single-table read is qualified too, and `ResolveColumnName(…, withTableName: true)` qualifies the `WHERE`
as well. Every SQL store read funnels through `Connector.Select(typeof(T), …)`
(`DataBaseStore:121`, `DataBaseBulkStore:44`, and both async twins), so on PostgreSQL **every read of
every entity whose table name is not already all-lower-case returns zero rows, silently**. Reads, not just
views. `TimescaleDBConnector : PostgreSQLConnector`, and consumer Symbio uses TimescaleDB, so this reaches
a real deployment.

**The swallow is also wider than its name.** `A_select_the_server_rejects_throws_…` failed: a
`column "NoSuchColumn" does not exist` (SQLSTATE **42703**, an undefined *column* on a table that exists)
returned an empty result with no exception. `PostgreSQLConnector.IsMissingTableException` matches
`42P01` — which PostgreSQL also raises for *missing FROM-clause entry* — **and** a bare
`Message.Contains("does not exist")` catch-all, which additionally covers undefined column / function /
type. MySQL's `Contains("doesn't exist")` has the same shape. The one probe that **passed** is the
legitimate opt-out: a genuinely absent relation still reads as empty, which the lazy create-on-first-use
path depends on.

**Consequence for sequencing (decided by measurement, not preference).** The swallow cannot be narrowed
on its own: with the qualifier still bare, narrowing it turns every PostgreSQL read from *silently empty*
into a *thrown exception*. The two halves have to ship together.

## Approach

1. **Find and fix the swallowing first**, and give it its own test. Until an error surfaces, every other fix
   here is unverifiable by the usual means — that is how both defects survived. Decide deliberately whether
   the swallow is ever legitimate (`ViewExists` probing is the one place it plausibly is, per CR-M149) and
   narrow it to that.
2. Pass `QuoteIdentifier` into the on-the-fly projection via the `quoteTable` parameter TASK-209 added.
3. Bring the on-the-fly `ON` / `GROUP BY` / `ORDER BY` emitters onto the same rule.
4. Then check the plain joined SELECT path and fix it the same way.

## Acceptance criteria

- [x] An on-the-fly view over PascalCase models round-trips on **real PostgreSQL** — reproduce first, and
      record how the server was run (TASK-209's § Measured section has a no-Docker recipe that works)
- [x] A view query whose SQL is rejected by the server **throws** rather than returning an empty collection,
      with a test that fails if the swallow returns
- [x] The `PostgreSqlViewRoundTripTests` class doc's "on-the-fly is still broken" paragraph is removed and
      replaced by an executing assertion — it exists only because this was out of scope
- [x] Multi-table plain `SELECT`s are checked on PostgreSQL and either fixed here or filed with evidence
      — **fixed here**, along with single-table and filtered reads, which the same probe showed were equally
      broken (scope widened with the user's decision, 2026-08-15). Filtered **writes** are broken by the same
      mechanism and are **filed with evidence as [[TASK-216]]**: the alias does not port to `DELETE`/`UPDATE`
      on MSSql, and they fail loudly rather than silently
- [x] Red-verified; split as numbers, contract pins named as pins
- [x] Full SQL suite sweep (TASK-209 touched 13 suites; the same set applies) — **23 suites, all green**
- [x] `/specs regen views-and-aggregation` — **retargeted**: no file this task changed is in that area's
      globs. All three (`AbstractConnectorBase.cs`, `PostgreSQLConnector.cs`, `MySQLConnector.cs`) are in
      **`filter-expression-translation`**, which is what needs regenerating; `views-and-aggregation` is
      affected behaviourally (the on-the-fly read now works on a folding provider) with no source change,
      so its regen is optional and its diff should be empty

## Out of scope

- The persistent view path — [[TASK-209]] closed it and this task must not regress it.
- Adding a permanent PostgreSQL CI tier (STORY-042's Docker tier). This needs one reproduction.

## Outcome

**What was wrong.** Two defects with one story. Every SELECT this framework builds qualifies its columns
(`Table.Column`) while the `FROM` quoted its table, so on PostgreSQL — the one supported provider that
case-folds an unquoted identifier — the qualifier folded to `ofpersons` and did not match the quoted
relation. **Every read of every entity whose table name is not already all-lower-case returned zero rows**,
reads and views alike, reaching consumer Symbio via `TimescaleDBConnector : PostgreSQLConnector`. And the
reason nothing was ever red: `IsMissingTableException` classified the resulting error as "table missing,
yield empty". The swallow hid the bug that produced the error it swallowed.

**The filed scope was one twentieth of the real one.** § Context named *multi-table joined* SELECTs as the
residue; `AbstractConnector_Select.cs:95` passes `withName: true` unconditionally, so a single-table read is
qualified too, and `ResolveColumnName(…, withTableName: true)` qualifies the `WHERE`. Escalated as a
decision rather than taken; the user chose to widen (2026-08-15).

**The fix is an alias, not a quoted qualifier — decided by the failure mode.** `CreateSelectCommand` emits
`FROM "Widgets" AS Widgets`: quoted relation, bare alias, so every bare qualifier folds onto the alias.
Copying TASK-209's `quoteTable` delegate would have been the symmetric choice and a partial fix — the read
path's qualifiers include function-wrapped ones (`LOWER(T.Col)`, `COALESCE`, the `.Date` rewrite), each its
own producer, and a missed producer reproduces the identical silent empty result. It also keeps
`ParseConditionExpression` provider-independent. The swallow was narrowed on both axes: SQLSTATE as the
primary key, message only to separate the two shapes sharing `42P01`, untyped fallback kept but narrowed to
the *relation* / *table* wording.

**Split.** Live PostgreSQL 16.4: **5 of 16** fail on revert — `An_on_the_fly_view_round_trips_on_postgresql`,
`A_plain_single_table_select_returns_rows_on_postgresql`,
`A_filtered_single_table_select_returns_rows_on_postgresql`,
`A_plain_multi_table_select_returns_rows_on_postgresql`,
`A_select_the_server_rejects_throws_instead_of_returning_an_empty_result`.
Offline: **7 of 542** across three suites (3 alias-shape in `SelectTableAliasTests`, 3 PostgreSQL and 1
MySQL swallow-narrowing cases). Green after restore: 16/16 live, 23 SQL suites offline.
**Re-derived at the close gate**, after the security pass added four injection-payload cases and the
correctness pass changed the PostgreSQL predicate — the earlier "7 of 538" expired the moment the suite
changed, which is the standing rule.

**Contract pins, named as pins.** `A_genuinely_missing_table_still_reads_as_an_empty_result` passes either
way — that is its job: it guards the opt-out the narrowing must not close (§ SH-H037's rule that a guard's
escape hatch needs its own test). Four of the seven `SelectTableAliasTests` also pass either way (the three
unaliasable-name cases and `The_alias_is_never_quoted`). And **every offline assertion here is a pin about
the emitted shape, not evidence of the behaviour** — SQLite, MySQL and MSSql are all case-insensitive for
identifiers, so no offline suite can distinguish the fix from the defect. TASK-209 recorded this; it is
still true, and it is why the live gate matters.

**Judgement calls.**

- **The scope decision went to the user rather than being taken.** Widening crossed the task's own § Scope
  note. Same call TASK-209 escalated, and the same answer.
- **The write path was measured, not assumed, and then not fixed.** `DELETE FROM "T" WHERE T.Col = $1` fails
  identically, but the alias does not port (MSSql rejects `DELETE FROM t AS a`) and writes fail *loudly*.
  Filed as [[TASK-216]] with the measurement. The probe that found it was **removed** rather than left
  asserting the broken behaviour — the TASK-111 precedent.
- **Two suites failed on the narrowing, and restoring the catch-all would have been the wrong fix.** They
  assert an *untyped* exception carrying the provider's wording. The signal in `relation "x" does not exist`
  is the word *relation*; requiring it keeps the shipped fallback and still excludes column/function/type.
  Narrow on the signal, don't delete the seam.
- **The reserved-word risk was re-measured, not inherited.** TASK-209 measured it for columns; this fix
  needed it for *table* names, which is a different question. `SELECT Order.Guid FROM "Order"` is already a
  syntax error with or without an alias, so no working case exists to break — and unaliasable names are
  emitted unaliased anyway, which keeps the change off the one shape it is not about (`SELECT COUNT(*)`).
- **The acceptance criterion's spec target was wrong and was retargeted, not quietly skipped.** No file this
  task changed is in `views-and-aggregation`'s globs; all three are in `filter-expression-translation`.

## Human test plan

N/A — a query either returns the right rows against a live PostgreSQL or it does not, which an automated
test observes directly.

## Implementation plan

Drafted 2026-08-15 at `/tasks pick`. **Steps 2–5 are provisional until step 1 has run** — TASK-209's whole
lesson is that this family's filed defect is rarely the reachable one, so the shape below is a hypothesis
built by reading the emitters, not a costing.

1. **Reproduce on live PostgreSQL first, and reproduce more than the filed shape.** Extend
   `PostgreSqlViewRoundTripTests`' existing harness (same models, same `BIRKO_PG_HOST` gate). Three probes,
   because reading the emitters says the blast radius is wider than the task's Context claims:
   - the on-the-fly view query — expect an empty collection, no exception;
   - a **plain multi-table** SELECT (`Select(Type[])`) — the scope note's third-time-filed suspicion;
   - a **single-table** SELECT. `Table.GetSelectFields(withName: true)` qualifies *every* column with the
     bare table name, and `AbstractConnector_Select.cs:95` passes `true` unconditionally, so on paper the
     ordinary entity read is broken on PostgreSQL too. If that measures true the task's premise ("views")
     is understated and the finding has to be re-scoped before any fix — like TASK-209's, in the same way.
2. **Narrow the swallow, and give it its own test.** Two mechanisms, both wider than their names:
   - `RunReaderCommand` / its async + external-transaction twins `yield break` on
     `IsMissingTableException`, and `PostgreSQLConnector` answers **true** for SQLSTATE `42P01` — which PG
     also raises for *missing FROM-clause entry* — plus a bare `Message.Contains("does not exist")` catch-all
     that additionally swallows `42703 undefined_column`, undefined function, undefined type. MySQL's
     `Contains("doesn't exist")` is the same shape. So every one of the four sinks' errors reads as "no rows".
   - `PostgreSQLConnector_OnException` calls `DoInit()` and **returns** on any `does not exist` message,
     which is what let `CreateView` report success (TASK-209 measured this).
   The legitimate case is a genuinely absent relation (CR-M149's `ViewExists` probing, and the lazy
   create-on-first-use that `DoInit` implements). Narrow to *that* — match on the provider's own
   undefined-**table** signal and not on message substrings — and let everything else surface.
3. **Quote the table qualifier on the on-the-fly path**, applying TASK-209's settled rule (quote tables,
   never quote columns) to the second builder: `AbstractConnector_CreateSelectCommand` passes
   `quoteTable: QuoteIdentifier` into both `view.GetSelectFields()` calls (projection + GROUP BY), and the
   `ORDER BY` key follows via `DataBase.ViewOrderFieldName`, which today calls `GetSelectName(true)` with no
   delegate. The `ON` clause is the awkward one: on-the-fly joins render through the generic
   `ConditionDefinition` strategies, which emit `condition.Name` verbatim, so the qualifier has to be
   quoted at render time — `ViewSelectSqlBuilder.QuoteFieldReference` is the established shape and wants
   promoting to somewhere both builders can call rather than being copied.
4. **Then the plain SELECT path** (`AbstractConnector_Select.cs:95` + async twin), same rule, sized by what
   step 1 measured.
5. **Red-verify** by reverting production and re-running (live + offline), report the split as numbers, name
   contract pins as pins — and note that **any SQLite-only assertion here is a pin by construction**, as
   TASK-209 recorded. Then the 13-suite SQL sweep and `/specs regen views-and-aggregation`.

**Risks / notes**

- Narrowing the swallow is the change with the blast radius, not the quoting: it turns silently-empty reads
  into thrown exceptions across every provider. Measure it against the full SQL suite set before keeping it,
  and check the lazy-init path (`DoInit` on missing table) still works — that is a real feature, not a bug.
- The reserved-word question is already settled by TASK-209 (measured void: such a column cannot have its
  table created at all). Do not re-litigate it.
- If step 1 shows the ordinary entity read is broken on PostgreSQL, that is bigger than this task and the
  split decision goes to the user before any fix — the same call TASK-209 escalated rather than took.

## Progress log

- step 1 — picked via `/tasks pick TASK-211`; plan drafted above (no Plan subagent — drafted inline).
  Live PostgreSQL **16.4** is up from the session scratchpad via TASK-209's no-Docker recipe (EDB portable
  binaries, `initdb` + `pg_ctl` on port 55432, database `birkotest`, user `birko`). Docker is still not
  running on this machine, so the recipe is now twice-proven rather than once.
- step 2 — **verified against live PostgreSQL 16.4; the finding is REAL and UNDER-scoped** (TASK-209's trap
  arriving from the other side: there it was the fix that would have been a no-op, here it is the blast
  radius that was understated). See § Measured on real PostgreSQL 16.4 above. 4 of 5 probes failed; the
  passing one is the legitimate missing-relation opt-out. **No production code changed yet** — widening
  crosses the task's own § Scope note, so it is a decision, not a correction.
- step 3 — user decision: **widen** to every read emitter. Fix in `AbstractConnectorBase.SelectTableReference`
  (new) + its two emit sites, `PostgreSQLConnector.IsMissingTableException` / `_OnException`,
  `MySQLConnector.IsMissingTableException` / `_OnException`. Tests: new `PostgreSqlOnTheFlyViewTests` (6 live),
  new `SelectTableAliasTests` (7 offline), 5 new InlineData cases across the two provider suites.
- step 4 — sweep: 23 SQL-touching suites green (SQL, SqLite, SqLite.View, Providers, Views, ViewModel, MSSql,
  MSSql.View, MySQL, MySQL.View, PostgreSQL, PostgreSQL.View, View.Migrations, Migrations.SQL, Caching,
  Sync.Sql, BackgroundJobs.SQL, Workflow.SQL, Models.{SQL,Users,Customers,Inventory,Pricing,Product}.SQL).
- step 5 — red-verified by stashing the three production files: **5 of 16** live, **7 of 538** offline.
  Restored, re-run, all green. Pins named in § Outcome.
- step 6 — spawned [[TASK-216]] (filtered writes, same mechanism, different fix, loud not silent).
  CLAUDE.md § Conventions gained two rules and § Recent Updates an entry.
- step 7 — committed. Production: `f8d47b7` (Birko.Data.SQL, the alias), `c3fed41`
  (Birko.Data.SQL.PostgreSQL, the swallow), `72cac6d` (Birko.Data.SQL.MySQL, the swallow).
  Tests: `0e0fcc6` (PostgreSQL.View.Tests, 6 live), `24efd1b` (SQL.Tests, 7 pins), `cf1e01f`
  (PostgreSQL.Tests, 4 cases), `47cbb1d` (MySQL.Tests, 1 case).
- **Left open, deliberately, and this task is NOT `done` until it is closed:** the spec regen
  (`filter-expression-translation`, not the `views-and-aggregation` the criterion named — see the criterion
  for why) and the `/tasks close` gate itself (verify-conventions + code-review). Status stays
  `in-progress` rather than `review`: `## Human test plan` is `N/A`, so there is no human step to be waiting
  on, and `review` would misreport what is outstanding.
- step 8 — close gate. `verify-conventions` (project-local): clean; no CS8xxx (0 warnings), no store
  overriding public CRUD, `Recent Updates` + § Conventions both updated in the same change
  (register-on-introduce). Pre-existing NU1510/NU1504 packaging warnings surface in two test csprojs only
  under `TreatWarningsAsErrors` — untouched by this task, not spawned (build hygiene, not a defect).
  **`code-review` (inline) found a real regression in this task's own fix**: keying the missing-relation
  test on the English "does not exist" would break lazy create-on-first-use against a PostgreSQL server
  whose `lc_messages` is not English. Inverted so the SQLSTATE decides and the message only excludes
  (`6f7a12f`). **`security-review` (inline)**: the alias is the one identifier this builder interpolates
  BARE — gated on `\A[A-Za-z_][A-Za-z0-9_]*\z`, § Conventions' sanctioned fallback, and now asserted with
  four injection payloads rather than argued (`02ba5d3` + tests). Spec: `filter-expression-translation`
  regenerated surgically per the stable-wording rule — one new requirement for the alias, one for the
  missing-relation seam, and the rule-field section's "the table qualifier is pre-existing and broken on
  PostgreSQL" paragraph corrected, since it no longer is.
- step 9 — closed `done`.
