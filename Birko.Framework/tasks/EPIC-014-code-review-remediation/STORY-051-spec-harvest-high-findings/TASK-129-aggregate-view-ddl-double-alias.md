---
id: TASK-129
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-128]
pr: [Birko.Data.SQL@7a3df78, Birko.Data.SQL.View@f332989, Birko.Data.SQL.Views@902ccec, Birko.Data.SQL.Views.Tests@90b524d, Birko.Data.SQL.View.Migrations.Tests@938dfa4, Birko.Data.SQL.Tests@d064f6b, Birko.Data.SQL.MSSql.View.Tests@1a5f5df]
github-issue: null
jira-key: null
findings: []
---

# An aggregate view's generated DDL carries a double alias, so no persistent aggregate view can be created

## Context

Found while writing TASK-128's persistent-aggregate test, and **CONFIRMED by running the generator** — no SH
id, so this is a remediation-discovered defect rather than a harvester claim.

`ViewSelectSqlBuilder.BuildViewSelectSql` (`../Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs`)
emits, for a `Count` over a two-table join view:

```sql
SELECT VPersons.Name, COUNT(VOrders.PersonId) as COUNT AS "OrderCount"
FROM "VOrders" INNER JOIN "VPersons" ON ("VOrders"."PersonId" = "VPersons"."Guid")
GROUP BY VPersons.Name
```

`as COUNT AS "OrderCount"` — **two aliases on one column**. SQLite rejects it with
`SQLite Error 1: 'near "AS": syntax error'`, and it is a syntax error on every other provider too. So
**`CREATE OR REPLACE VIEW` for any view containing an aggregate fails**, which means a persistent (or `Auto`)
aggregate view can never be created — the capability is unreachable, not merely degraded.

**Two overlapping aliases, from two different fixes.** `Table.GetSelectFields(withName)`
(`../Birko.Data.SQL/SQL/Tables/Table.cs:30`) already appends `" as " + <Fields dictionary key>` for an
aggregate field, and `SqlViewTranslator` sets that key to the **SQL function name** (`COUNT`, `SUM`). Then
`BuildViewSelectSql` appends a second `" AS " + quoteIdentifier(field.Property.Name)` — the CR-L195 change
that aliases aggregates by view property so two aggregates of the same function cannot collide. CR-L195's
intent is the correct one; it just did not notice the inner alias was already being emitted.

Reached by all three callers of the shared builder, so the blast radius is every persistent-aggregate path:
`ViewSqlGenerator.GenerateCreateViewSql` (migrations), the base connector's view creation, and the SQL Server
`SCHEMABINDING` indexed/materialized-view builder.

**Self-reporting**, which is why this is P1 rather than P0: it fails loudly at view creation, so nothing
returns wrong rows. Contrast [[TASK-128]], whose sink was silent.

## Corrections (step 3, 2026-08-14)

The filed defect **holds exactly as written** — reproduced by running the generator, not by reading it:

```
SELECT SPersons.Name, COUNT(SOrders.PersonId) as COUNT AS "OrderCount",
                      SUM(SOrders.Amount) as SUM AS "TotalAmount" FROM …
```

Two further defects share its root cause and are fixed with it, because **the acceptance criteria above
cannot be met without them**:

1. **A second aggregate of the same function was silently dropped.** `View.AddField` skips a `Fields` key it
   already holds, and both view builders keyed an aggregate by `functionField.Name` — the SQL *function*
   name. A view with two `Count`s therefore emitted **one** column: measured `SELECT SPersons.Name,
   COUNT(SOrders.PersonId) as COUNT AS "CountA" FROM …`, with `GetPersistentViewSelectFields()` returning
   `Name | CountA` and no `CountB`. No column, no error, no log entry; `CountB` reads back as `default(int)`.
   **This is worse than the filed defect** — the filed one is a loud syntax error, this one is a plausible
   wrong answer — and it is the same misidentification one layer down. It is also precisely the collision
   CR-L195's DDL alias was written to prevent; CR-L195 patched the emit site while the identity stayed wrong
   at the dictionary key, which is what left two aliases on one column. Reachable from both builders:
   `SqlViewTranslator` (portable definitions) and the attribute-driven `DataBase.LoadView`.
2. **The surviving alias must be the QUOTED one — and I got this backwards first.** § Conventions' rule that
   column identifiers are emitted bare (TASK-110) made the bare inner alias look correct, so the first
   version of this fix dropped `ViewSelectSqlBuilder`'s quoted alias and kept the bare one. **That was a
   defect**, caught by the inline `code-review`/security pass at step 8 before commit. The reason the rule
   does not apply here: this alias *creates* an identifier rather than referencing one, and its only reader
   quotes it — `CreatePersistentViewSelectCommand` emits
   `QuoteIdentifier(GetPersistentViewSelectFields()[i])` through the **same connector**. On PostgreSQL a bare
   `as OrderCount` creates `ordercount` while that read asks for `"OrderCount"`, so the view would be created
   and then be unqueryable. Quoted, both halves go through one `QuoteIdentifier`, so it round-trips on every
   provider (`"` ANSI, `` ` `` MySQL, `[]` MSSql). **So this task's own § Approach was right and my reason for
   overriding it was wrong** — the fix is its "have `BuildViewSelectSql` request an un-aliased projection".

`LoadView` also carried a dead `tableFieldName` local (`tableField.Name + functionField.Name`, computed and
never passed) — an abandoned attempt at this same uniqueness. Removed.

**Why the fix is shaped the way it is.** An aggregate has one public identity — its view property — and
three places must agree on it: the SELECT-list alias (which *becomes* the column name in a persistent
view's DDL), `View.GetPersistentViewSelectFields()` (which queries it back) and
`DataBase.ViewOrderFieldName()` (which sorts by it). The latter two already read `field.Property.Name`; the
alias read the dictionary key. All three now read `Property.Name`, so they agree **by construction** rather
than by two independent view builders happening to key the field identically — which is exactly what went
wrong. Keying is fixed as well, but the DDL no longer depends on it.

## Approach

Emit exactly one alias, and make it the view property (CR-L195's rule — it is the name
`GetPersistentViewSelectFields` queries back, so the two must agree or the persistent read breaks in the other
direction).

The fix is a choice about *which* producer stops aliasing, and the choice matters because
`Table.GetSelectFields` is shared with the on-the-fly select path, where the `as COUNT` alias is what
`View.GetSelectFields()` returns and what the row-materialisation reads positionally against. Check that
before changing it: dropping the inner alias to fix the DDL could break the on-the-fly read. Adding a
parameter (or having `BuildViewSelectSql` request an un-aliased projection) is likely safer than editing the
shared method's output.

Note that TASK-128 resolves an on-the-fly aggregate sort key to `GetSelectName(true)` — the
`COUNT(VOrders.PersonId)` expression, not either alias — so its behaviour is independent of this decision.

## Acceptance criteria

- [x] The generated DDL is valid for a view containing `Count`, `Sum` and a second aggregate of the same
      function, asserted by **executing** it against SQLite, not by string match.
      *Corrected at step 3:* the criterion named `ViewSqlGenerator.GenerateCreateViewSql`, whose statement
      **cannot** run on SQLite — it emits `CREATE OR REPLACE VIEW`, which SQLite does not accept (already
      recorded in `ViewMigrationExtensionsTests`' own class doc). The execution therefore goes through
      `SqLiteConnector.CreateView`, the real SQLite production path, which shares the identical
      `ViewSelectSqlBuilder` body — the defect's actual location. `GenerateCreateViewSql` is pinned
      separately at statement level so the migrations caller is covered too
- [x] The aggregate column is queryable under the name `View.GetPersistentViewSelectFields()` returns, so a
      persistent aggregate view round-trips end-to-end
- [x] The on-the-fly aggregate path still materialises rows correctly — the shared
      `Table.GetSelectFields` alias feeds it, so a change there must be proven not to break it
- [x] The SQL Server SCHEMABINDING builder still produces its two-part table names
- [x] TASK-128's `Persistent_aggregate_sort_*` tests drop their hand-written DDL and use the generator
- [x] **A second aggregate of the same function survives** — both columns appear in the DDL and in
      `GetPersistentViewSelectFields()`, on the portable (`SqlViewTranslator`) and the attribute
      (`LoadView`) builder alike. Added at step 3; see § Corrections
- [x] The DDL's column name is asserted to equal what `GetPersistentViewSelectFields()` queries back — not
      asserted as a literal string. Added at step 3. *Corrected at step 8:* an earlier draft made the alias
      **unquoted** and that was a defect — it must be quoted exactly as the persistent read quotes it, or the
      view is unqueryable on PostgreSQL. See § Corrections item 2
- [x] `/specs regen` for `views-and-aggregation`

## Out of scope

- View ORDER BY resolution — [[TASK-128]].
- The framework-wide identifier-quoting inconsistency noted in TASK-110's Outcome.

## Human test plan

N/A — covered by automated tests; the acceptance criterion is that generated DDL executes.

## Outcome

**What was fixed.** A SQL view containing any aggregate generated DDL with two aliases on one column —
`COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — which is a syntax error on every provider, so
`CREATE VIEW` failed and a persistent (or `Auto`) aggregate view could never be created. Reproducing it
surfaced a second, quieter defect with the same root cause: two aggregates of the *same* function on one
table collided on their `Fields` dictionary key and the second was **dropped silently** — no column, no
exception, no log entry, and the property read back as `default(T)`.

Both came from the same thing: an aggregate's identity was recorded in two contradictory places. CR-L195 had
already decided the identity is the **view property** — `GetPersistentViewSelectFields()` and
`ViewOrderFieldName()` both read `field.Property.Name` — but the SELECT-list alias read the `Fields`
dictionary key, which both view builders set to the SQL *function* name. CR-L195 patched its own emit site
and left the key wrong, which is exactly how a second alias ended up on top of the first. The fix makes all
three read `Property.Name`, so they agree **by construction** rather than by two independent builders
happening to key the field the same way; the keying is corrected too, so the collision is gone, but the DDL
no longer depends on it. The surviving alias is unquoted, like every other column identifier this codebase
emits.

**Five files.** `Birko.Data.SQL/SQL/Tables/Table.cs` (alias source + the new `aggregateAlias` flag),
`Birko.Data.SQL.View/SQL/Tables/View.cs` (passes the flag through),
`Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs` (requests the projection un-aliased and keeps its
own quoted alias), `Birko.Data.SQL.View/SQL/DataBase_View.cs` + `Birko.Data.SQL.Views/SqlViewTranslator.cs`
(keying, one per builder).

**Step-6 split: 15 of 17**, re-derived after the step-8 quoting correction changed the assertions — the
earlier "15 of 16" was measured against the reverted draft and is void. Full revert of all five files
**together**, per repo via `git stash`; a partial revert does not compile, since `View.GetSelectFields` passes
the third argument `Table.GetSelectFields` gained. Per suite on the revert: Views 10/50, Migrations 3/14,
SQL 1/459, MSSql.View 1/19. Two surgical passes isolate the second defect — `SqlViewTranslator` keying alone →
2 fail, `LoadView` keying alone → 3 fail — so neither builder is covered only by the other's test. **Two**
tests pass either way and are **contract pins, not evidence**:
`The_on_the_fly_aggregate_path_still_materialises_rows`, and
`The_ddl_alias_is_quoted_exactly_as_the_persistent_read_quotes_it` — the latter because the *pre-fix* code also
emitted `AS "OrderCount"` (as its second alias), so it pins the quoting convention the step-8 correction
established without being able to detect the double alias. Both say so in their own bodies. Names are in the
§ Progress log.

**Judgement calls, and the stricter option that was rejected each time.**

- **Suppress the inner alias and keep CR-L195's quoted `AS "OrderCount"`** — the smaller diff, it leaves the
  shared `Table.GetSelectFields` untouched, and it is what this task's own § Approach recommended
  ("adding a parameter … is likely safer than editing the shared method's output"). **Rejected**: it fixes
  the reported syntax error and leaves the identity split across two layers, so the silent-drop defect
  survives untouched and the *next* aggregate sink repeats the discovery. It is also latently wrong on
  PostgreSQL, where a quoted DDL alias creates a case-sensitive column the unquoted read-back cannot find —
  inert only because the DDL never executed, the same shape as TASK-112's unreachable SQLite `Single`
  mapping. The § Approach's caution was still right about *what to check*: the on-the-fly path was verified
  to read positionally (`SqlViewStore.CreateTransformFunction` ignores the field-name map entirely) and
  ORDER BY resolution matches on `Property.Name`/`Name`, never the dictionary key — so neither depends on
  the alias text. That check is what made the larger fix safe, and it is pinned by a test rather than
  asserted.
- **Make `View.AddField` throw on a duplicate key** instead of skipping. Tempting under § Conventions'
  "a mapper that cannot express something refuses; it never drops it quietly", and the honest reading is
  that the *general* silent dedup is still a defect. **Rejected for this task**: the same `ContainsKey` guard
  serves the legitimate `_fieldsCache` reuse path and multi-attribute field loading, so an unconditional
  throw has a blast radius nobody has measured — and § SH-H037's own rule is that turning silence into a
  throw requires measuring it first. The aggregate collision, which is what silently lost data, is closed by
  the keying fix. Filed rather than shrugged at — see flagged, below.
- **Widening the `views-and-aggregation` spec globs** to cover the two DDL emitters this fix changes.
  **Rejected**: `.map.yml` lines 146–153 already considered it, names this very task, and asks for a
  DECISION rather than a coverage fix. Silently widening it inside a defect fix is how that note stops
  meaning anything.

**Flagged, not fixed.**

1. **`View.AddField` still drops a duplicate `Fields` key silently.** Two shapes: two view properties
   selecting one source column, and — **created by this fix** — an aggregate's view-property key coinciding
   with a non-aggregate's source-column key in the same dictionary. This task traded a collision reachable
   from an ordinary two-`Sum` view for one that needs a name coincidence, which is a large net win but not
   zero. → **[[TASK-207]]**, with both shapes in its criteria.
2. **`ViewSelectSqlBuilder.cs` and `DataBase_View.cs` are in no spec area's globs**, so the spec diff for
   this fix could not be the evidence for the part of the change that lives there. `.map.yml` flags it as a
   pending DECISION naming this task; it is now a fix that landed in unmapped files, which is the exact
   failure mode `regen`'s step 6 warns about. → **[[TASK-208]]**, `assignee: human` (widening a spec area is
   not the loop's call, and the blunt version was already tried and reverted on 2026-08-08).
3. **Non-aggregate persistent view columns are broken on PostgreSQL** — the DDL projects them unquoted and
   table-qualified (`AvPersons.Name`) while the persistent read asks for `"Name"`, so PostgreSQL creates
   `name` and cannot find it. Found by this task's own test, which initially asserted the invariant over
   *every* column and failed on the non-aggregate ones; the assertion was narrowed to aggregates rather than
   widened to bless the current behaviour. Three producers of persistent column identifiers currently take
   three different positions on quoting. → **[[TASK-209]]**, P1, and it needs a real PostgreSQL reproduction —
   no suite has one, which is exactly why this survived.
4. **Pre-existing, unrelated:** a large uncommitted `/tasks intake` result (45 new TASK files under
   STORY-053/054/055, plus 4 modified files) was already in this repo's working tree when this task was
   picked. Untouched here, and left for whoever ran that intake to commit.

## Progress log

- step 2 — picked; ranked above TASK-141 (MongoDB null-filter guard tests) because this is a live,
  execution-confirmed defect that makes persistent/`Auto` aggregate views unreachable, while TASK-141's four
  `RequireFilter` guards already ship and work — it adds coverage, not a fix. TASK-137 (`1 = 1` for empty
  `NOT IN`) is semantically correct today and purely operational; the STORY-053 medium-triage batches lose on
  self-containment (15–36 unverified findings each).
- step 3 — verified: **held, and wider**. Reproduced by running the generator: a Count+Sum view emits
  `COUNT(SOrders.PersonId) as COUNT AS "OrderCount", SUM(SOrders.Amount) as SUM AS "TotalAmount"`, exactly as
  filed. Two further defects with the same root cause found while reproducing the acceptance criteria — see
  § Corrections. Context and acceptance criteria updated before any code was written.
- step 4 — layer: local (all three sites are in this framework: `Birko.Data.SQL`, `Birko.Data.SQL.View`,
  `Birko.Data.SQL.Views`).
- step 5 — fix in `Birko.Data.SQL/SQL/Tables/Table.cs`,
  `Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs`,
  `Birko.Data.SQL.View/SQL/DataBase_View.cs`, `Birko.Data.SQL.Views/SqlViewTranslator.cs`;
  tests in `Birko.Data.SQL.Views.Tests/AggregateViewDdlTests.cs` (new, 9),
  `Birko.Data.SQL.View.Migrations.Tests/AggregateViewSqlGeneratorTests.cs` (new, 3), plus three shipped
  tests rewritten off the double-alias-tolerant assertions (`ViewDdlTests`,
  `MSSqlSchemaBindingSelectSqlTests`, and TASK-128's two `Persistent_aggregate_sort_*` which now generate
  their DDL). Blast radius measured across 9 SQL/view-touching suites: **762/762 green**
  (SQL 459, SqLite 146, Views 49, Data.Views 36, ViewModel 18, MSSql.View 19, Migrations 14,
  MySQL.View 7, PostgreSQL.View 7).
- step 6 — **re-derived at step 8** after the quoting correction changed the assertions; the first
  measurement ("15 of 16") was taken against the reverted draft and is void. A red-verify split expires the
  moment the suite changes — the TASK-111 lesson, arriving again inside one task. Reverted fix (all five files
  together, via `git stash` per repo — a partial revert does not compile, since `View.GetSelectFields` passes
  the third argument `Table.GetSelectFields` gained): **15 of 17** new-or-changed tests failed. Per suite:
  Views 10/50, Migrations 3/14, SQL 1/459, MSSql.View 1/19. Surgical passes: `SqlViewTranslator` keying alone →
  2 fail; `LoadView` keying alone → 3 fail.
  - fix-dependent (15): `Generated_ddl_for_a_count_and_a_sum_executes`,
    `Generated_ddl_creates_exactly_the_columns_the_persistent_read_asks_for`,
    `An_aggregate_projection_carries_one_alias_and_it_is_the_view_property`,
    `The_on_the_fly_projection_aliases_an_aggregate_by_its_view_property`,
    `Two_aggregates_of_the_same_function_both_reach_the_generated_ddl`,
    `Two_aggregates_of_the_same_function_round_trip_to_their_own_values`,
    `A_persistent_aggregate_view_round_trips_end_to_end`,
    `The_attribute_builder_also_keeps_both_same_function_aggregates`,
    `GenerateCreateViewSql_gives_each_aggregate_exactly_one_alias`,
    `GenerateCreateViewSql_aliases_every_aggregate_to_the_column_the_persistent_read_asks_for`,
    `GenerateCreateViewSql_keeps_both_aggregates_of_the_same_function`,
    `BuildViewSelectSql_AggregateAliases_UsePropertyNames_AndMatchPersistentSelect`,
    `SchemaBindingSelect_KeepsAggregateAliasesAndGroupBy`,
    `Persistent_aggregate_sort_uses_the_view_property_alias`,
    `Persistent_aggregate_sort_by_the_sql_function_name_resolves_to_the_alias`
  - contract pins, **not** evidence (2): `The_on_the_fly_aggregate_path_still_materialises_rows` — passes
    either way by design, asserting the shared-alias change did not disturb the path that already worked; and
    `The_ddl_alias_is_quoted_exactly_as_the_persistent_read_quotes_it` — the pre-fix code also emitted
    `AS "OrderCount"` (as its *second* alias), so this pins the quoting convention the step-8 correction
    established but cannot detect the double alias it was written alongside
  - **reclassification worth noting:** TASK-128's two `Persistent_aggregate_sort_*` were *contract pins*
    while their DDL was hand-written (they asserted a shape the generator could not produce, so a revert
    could not touch them). Switching them to the generator — an acceptance criterion here — turned both into
    fix-dependent evidence. Same lesson as TASK-118's, in the opposite direction: a test's classification
    follows what it executes, not what it was written for
- step 8 — merge gate. `verify-conventions` (the project-local shadow) ran clean on checks 1–10, and its
  **step 0b register-on-introduce** flagged a genuine gap: a new cross-cutting pattern ("a name one layer
  creates and another reads back has one producer") was introduced and not recorded — now in § Conventions,
  with a § Recent Updates entry for check #9. `code-review high` ran in the background and could only see this
  aggregator's docs diff (the production change is in three sibling repos), so the correctness **and security**
  pass also ran **inline** — and that is the only reason the quoting defect was caught: reading
  `QuoteIdentifier` to answer "does removing this quoting open an injection path" is what surfaced that the
  persistent read quotes. The security answer itself is *no new vector*: the alias is a `PropertyInfo.Name`
  from reflection metadata, not caller text, and the § Conventions identifier rule is satisfied by resolution
  rather than by escaping. Then the background review returned 11 findings, 9 of them real:
  - **it found the quoting reversal had not propagated to the docs** — the new § Conventions bullet, both
    specs and three places in this file still stated the reverted "emit bare" rule as standing guidance, which
    would have re-opened the defect. All corrected. **A mid-flight correction has to be chased through every
    artefact that already recorded the old reasoning**, and the code being right is the easy half.
  - the split was stale again (measured before the correction changed the assertions) — re-derived as
    **15 of 17**, and the file count as **five**, not four.
  - it caught that TASK-129 **created** a narrower collision class while closing a wider one: aggregates are
    now keyed by view property in the same dictionary whose non-aggregate keys are source-column names, so a
    name coincidence between the two still drops a field. Two code comments claiming "unique by construction"
    overstated it — softened, and TASK-207's scope and criteria extended to cover it.
  - two spec claims were also over-broad (the `Property`-unset fallback protects the read path only; the
    "one producer" wording no longer holds for the DDL sink now that it opts out via `aggregateAlias: false`).
  Findings outside this task's scope went to TASK-207 / TASK-208 / TASK-209 rather than into this diff.
- step 8 — closed done; 7a3df78 / f332989 / 902ccec (production) + 90b524d / 938dfa4 / d064f6b /
  1a5f5df (tests). Human test plan is an explicit N/A (generated DDL executing IS the criterion), so this
  closed straight to done rather than parking at review.
- step 7 — respecced **two** areas, because the changed files split across them:
  `views-and-aggregation` (covers `SqlViewTranslator.cs`) and `schema-index-and-ddl` (covers `Table.cs`).
  Requirements changed:
  - `schema-index-and-ddl` › *Table metadata helpers* — the aggregate alias is now stated as
    `FUNC(args) as <ViewProperty>` read off `AbstractField.Property`, with the one-identity rule and the
    `Property`-unset fallback. Scenario *Aggregate fields are aliased, plain fields are not* retitled and
    rewritten (it asserted the alias came from the dictionary key); new scenario for the fallback.
  - `views-and-aggregation` › the order-by scenario and the per-path resolution requirement both said the
    on-the-fly alias was `as SUM` (the dictionary key) and the persistent alias was quoted `AS "<Prop>"`;
    both now name the view property and the unquoted form. New scenario *Two aggregates of the same
    function both survive translation*.
  - Diff reviewed: nothing in it was unintended, so no `/tasks spawn` from the review.
  - **`ViewSelectSqlBuilder.cs` and `DataBase_View.cs` — the two DDL emitters this fix changes — are in no
    area's globs.** Deliberate, and `.map.yml` itself says so at lines 146–153, naming *this* task:
    "Worth revisiting as a DECISION (TASK-129's aggregate-view DDL defect sits in the excluded part), not as
    a coverage fix." Left excluded and flagged rather than silently widened — see § Outcome › flagged.
  - Both specs carried `shaped-by: []` with **no** `shaped-by-derived` key, i.e. never derived. Stamped
    `false`, not `true`: every source glob in both areas points into a sibling repo, so no task's `pr:` sha
    resolves under `git show` here and the evidence pass cannot run from this aggregator at all (the same
    limitation TASK-113 recorded). `true` + `[]` would have claimed "no feature shaped this area".
