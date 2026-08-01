---
id: TASK-128
parent: STORY-051
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-110]
pr: 576707c (Birko.Data.SQL.View) / 4c7962d (Birko.Data.SQL.Views.Tests)
github-issue: null
jira-key: null
findings: []
---

# The view path's ORDER BY still interpolates caller text — the twin TASK-110 did not cover

## Context

Found while fixing [[TASK-110]] (SH-H003), by hand, and **CONFIRMED by reading** — no SH id, so this is a
remediation-discovered defect rather than a harvester claim.

TASK-110 closed the entity-store sort sink by resolving every ORDER BY key against table metadata in the one
layer every SQL entity read funnels through — `AbstractConnector.Select(IEnumerable<Tables.Table>, …)` and its
async twin. **The view path does not pass through that funnel**, so it kept the original behaviour:

- `../Birko.Data.SQL.Views/SqlViewStore.cs:137` — `TranslateOrderBy` copies `OrderByField.PropertyName`
  straight into the dictionary. No resolution, exactly as `DataBaseBulkStore.ReadCore` used to do, so
  `OrderBy<TView>.ByName(request.Sort)` is the same untrusted-input path.
- `../Birko.Data.SQL.View/SQL/Connectors/AbstractConnectorBase_View.cs:91` — a **second, independent**
  ORDER BY emit site (`CreatePersistentViewSelectCommand`), interpolating `kvp.Key` verbatim just like
  `AbstractConnectorBase.cs:558` did.
- The on-the-fly view path reaches `CreateSelectCommand(command, Tables.View view, …)`
  (`AbstractConnector_CreateSelectCommand.cs:13`), which forwards to the **table-name-string** overload —
  below where metadata still exists — so it bypasses the resolver as well.

`docs/specs/bulk-filter-operations.md` records this as a known gap under
*"The view path is not covered by this funnel"*, and `views-and-aggregation.md` already documented the
verbatim interpolation as shipped behaviour (*"Order-by keys are interpolated verbatim while aggregate
columns are aliased by function name"*).

### Step-3 re-verification (2026-07-31) — measured on both paths, and the ordinary-consumer half is worse than filed

Probed against a real SQLite file through `SqlViewStore<TView>.QueryAsync`, with `OnExecute` capturing
`CommandText`, over a two-table join view whose `VPerson.Name` is projected as the view property
`PersonName`.

**Injection holds on BOTH paths — arbitrary DDL executed, no exception:**

| path | emitted | effect |
|---|---|---|
| on-the-fly | `SELECT VOrders.Guid, VPersons.Name FROM "VOrders" INNER JOIN … ORDER BY Name; CREATE TABLE Pwned (x INTEGER); -- ASC` | **`Pwned` created** |
| persistent | `SELECT "Guid", "Name" FROM "VOrderPersistent" ORDER BY Name; CREATE TABLE Pwned2 (x INTEGER); -- ASC` | **`Pwned2` created** |

**The type-safe API is broken on both paths, which the task under-stated.** `OrderBy<TView>.By(x =>
x.PersonName)` emits `ORDER BY PersonName` and both paths raise
`SqliteException: no such column: PersonName`. This is worse than the entity-store twin: there, only a
`[NamedField]`-remapped column was affected, whereas **renaming is what a view is for** — the view property
essentially never equals the source column, so *sorting a view by one of its own properties does not work at
all today*. Only `ByName("<source column>")` works, i.e. the one overload that is also the injection sink.

**The metadata needed is already there.** `SqlViewTranslator` (and `DataBase.LoadView` for attribute-declared
views) sets `loadField.Property = viewProp` while leaving `field.Name` as the source column, so a view field
knows both names: `Property.Name` = `PersonName`, `Name` = `Name`. Resolution by property name works for both
view kinds.

**The two paths need different emitted forms, and each resolves consistently with its own SELECT list** — so
the acceptance row's "or record the divergence" escape hatch is not needed:

| | on-the-fly | persistent |
|---|---|---|
| SELECT list | `view.GetSelectFields()` — always `Table.Column`, bare | `GetPersistentViewSelectFields()` — `IsAggregate ? Property.Name : Name` |
| so ORDER BY resolves to | `field.GetSelectName(true)` | `field.IsAggregate ? field.Property.Name : field.Name` |
| aggregate sorts by | `SUM(VOrders.Amount)` — valid in a grouped query | the `AS "PropertyName"` alias the view DDL emits |

**Quoting is deliberately left untouched** (see TASK-110's step-3 block for the reasoning). Note the
persistent path *does* quote its SELECT list (`QuoteIdentifier(f)`) while its ORDER BY would stay bare —
recorded as an observation under the framework-wide quoting question, not changed here, because changing it
could break a working sort on PostgreSQL exactly as it would have in TASK-110.

## Why this is a separate task rather than a widening of TASK-110

Same root cause and same reachability, but a different project (`Birko.Data.SQL.View` +
`Birko.Data.SQL.Views`, both outside `Birko.Data.SQL`), a different metadata source (`Tables.View`, whose
fields live across `View.Tables[*].Fields` and whose aggregate columns are keyed by SQL function name rather
than by property), and a different funnel that has to be chosen. It also lands in a different spec area.
Absorbing it would have doubled TASK-110 and left it unfinished in one session.

## Approach

Reuse `DataBase.ResolveOrderFields` — it already takes `IEnumerable<Tables.Table>`, and `View.Tables` is
exactly that, so the entity resolver may work unchanged. Verify that first; the view's aggregate-field
naming is the part most likely to need a `View`-aware overload, because `SqlViewTranslator` keys
`table.Fields` by the SQL function name while `GetPersistentViewSelectFields` names aggregate columns by view
property — the two view paths disagree about what an aggregate column is called, so "what should an aggregate
sort key resolve to" has to be answered per path.

Pick the funnel deliberately, the same way TASK-110 did: one resolution point that both the persistent and
the on-the-fly view command builders pass through, or one per builder if no single point exists. Do **not**
resolve in `SqlViewStore.TranslateOrderBy` alone — that leaves the connector's view overloads open to any
other caller.

Do **not** quote the resolved identifier (see TASK-110's step-3 block: this codebase emits column
identifiers bare everywhere, and quoting would break mixed-case columns on PostgreSQL).

## Acceptance criteria

- [x] View ORDER BY keys are resolved against the view's field metadata; the emitted identifier is always a
      name drawn from that metadata, never caller-supplied text
- [x] Both view paths are covered — persistent (`CreatePersistentViewSelectCommand`) and on-the-fly — and the
      test names say which is which
- [x] Sorting a view by a renamed / remapped view property returns correctly ordered rows — today
      `OrderBy<TView>.By(x => x.ViewProp)` raises `no such column: ViewProp` on **both** paths
- [x] The aggregate-column sort key resolves consistently on each path — each to what that path's own SELECT
      list uses (measured at step 3; no divergence needs recording)
- [x] `ByName` injection payloads are rejected: the batch-separator payload
      (`Rank; CREATE TABLE Pwned (x INTEGER); --`) asserts the side effect is absent, as in
      `Birko.Data.SQL.SqLite.Tests.OrderByResolutionTests`
- [x] An unresolvable view sort key throws naming the key and the view
- [x] A view sort that works today keeps returning the same rows in the same order. Note the only one that
      works is `ByName("<source column>")`; on the on-the-fly path its emitted identifier gains the table
      prefix the view's own SELECT list already uses, which is a strict improvement — bare `Name` is ambiguous
      when two joined tables both have that column
- [x] Tests in `Birko.Data.SQL.Views.Tests` and/or `Birko.Data.SQL.SqLite.View.Tests`
- [x] `/specs regen` for `views-and-aggregation` — its "interpolated verbatim" scenario becomes wrong once
      this lands, and `bulk-filter-operations.md`'s known-gap scenario has to go with it

## Out of scope

- The entity-store path — done in [[TASK-110]].
- `SH-H023` (`RuleConditionConverter`, an unresolved identifier in the WHERE clause) — [[TASK-111]].

## Human test plan

N/A — covered by automated tests.

## Outcome

**What the fix was.** `DataBase.ResolveViewOrderFields` (new,
`Birko.Data.SQL.View/SQL/DataBase_ViewOrderBy.cs`) resolves every view sort key against the view's field
metadata — view property first, then source column — and throws `ArgumentException` naming the key and the
view otherwise. Both view builders already carried what was needed: `SqlViewTranslator` and the
attribute-driven `LoadView` assign the view property to `AbstractField.Property` while leaving the source
column in `Name`, so one field knows both names.

It resolves in `Select(Tables.View, …)` / `SelectAsync(…)`, **after `usePersistent` is computed**. That
placement is the substance of the fix: it is the one method every view read funnels through — both paths, both
the string- and expression-keyed entry points — and the only one that knows which path will run. The two paths
expose their columns under *different* names, so a single resolved form would be wrong for one of them:
on-the-fly → `field.GetSelectName(true)` (the `Table.Column` form `View.GetSelectFields()` always projects),
persistent → `IsAggregate ? Property.Name : Name` (matching `GetPersistentViewSelectFields` and the
`AS "<ViewProperty>"` alias the view DDL emits). The two expression-keyed overloads pre-resolved to the
qualified source column, which a persistent view does not have, so they now pass the view property name.

**Step-5 split — 16 fix-dependent, 4 contract pins.** With the wiring reverted (resolver and all tests left in
place): Views.Tests **22/38**. Names are in the step-5 progress-log line. The pins are the three back-compat
cases plus `Persistent_aggregate_sort_uses_the_view_property_alias`.

### Judgement calls, and why the stricter option was rejected

- **Resolution placed after `usePersistent`, not in the command builders.** Resolving in each builder would
  have put the logic where the metadata is thinnest and duplicated it across sync and async; resolving
  *before* the branch would have had to pick one form and be wrong for the other path. The funnel is the only
  point that is both single and path-aware.
- **Not in `SqlViewStore.TranslateOrderBy`.** That is where the key originates and would have been the
  smallest diff, but it leaves every connector view overload reachable unguarded by any other caller — the
  same reason TASK-110 resolved in the connector rather than in the store.
- **Quoting untouched, as in TASK-110.** Quoting would break mixed-case columns on PostgreSQL, where an
  unquoted DDL identifier is folded to lower case. Recorded below that the persistent path quotes its SELECT
  list while its ORDER BY stays bare — a genuine local inconsistency, deliberately not resolved by a bug fix.
- **The source column name is still accepted, not only the view property.** `ByName("<source column>")` was
  the only view sort that worked before this fix; rejecting it would have broken the one working usage. It
  comes from the same metadata, so accepting it widens nothing.
- **The predicted aggregate divergence did not need documenting as a limitation.** The acceptance row offered
  "or record the divergence" as an escape hatch; measurement showed each path resolves consistently with its
  own SELECT list, so the stricter reading (make them agree with each other) would have meant emitting a name
  one of the two paths does not have.

### Flagged, not fixed

- **[[TASK-129]] (P1), filed.** `ViewSelectSqlBuilder` emits a **double alias** for an aggregate —
  `COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — which is a syntax error on every provider, so
  `CREATE VIEW` fails and **no persistent aggregate view can be created at all**. Two overlapping aliases from
  two different fixes: `Table.GetSelectFields` already appends `as <function name>`, and CR-L195 added
  `AS "<ViewProperty>"` on top. Out of scope here (different function, invalid-DDL rather than an injection
  sink, and self-reporting), so the two persistent-aggregate tests use hand-written DDL matching what the
  generator intends, with the reason in the test comment.
- **Sorting a persistent view emits a bare identifier while its SELECT list quotes.** Locally inconsistent,
  and it means a reserved-word view column can be selected but not sorted by. Belongs to the framework-wide
  quoting question already recorded in TASK-110's Outcome, which needs a live PostgreSQL server to settle.
- **`Birko.Data.SQL.View` remains out of scope in `docs/specs/.map.yml`** except for the three files this task
  touched, which were added so the behaviour is not unmapped. The rest of that project is still
  uncovered — a deliberate carry-over of the map's existing decision, not a new gap.

## Progress log

- step 2 — picked; ranked above TASK-113 (TenantSyncProvider unscoped reads/deletes) on the same two keys
  that decided TASK-110: this is arbitrary statement execution reachable directly from untrusted input (a
  view query's sort parameter) and silent, while TASK-113's cross-tenant reach needs a particular sync-job
  configuration; and this has no open design question — the resolver already shipped and the one sub-question
  (aggregate-column naming) carries an explicit "or record the divergence" escape hatch — while TASK-113's
  tenant-precedence decision is still entangled with the open TASK-127.
- step 3 — verified on a live SQLite file through `SqlViewStore.QueryAsync`, on **both** paths. Injection
  holds: the batch-separator payload created `Pwned` on the on-the-fly path and `Pwned2` on the persistent
  path, neither raising. **Under-stated in the filing**: `OrderBy<TView>.By(x => x.PersonName)` — the
  type-safe API — raises `no such column: PersonName` on both paths, so sorting a view by one of its own
  properties does not work at all today; worse than the entity twin, because renaming is what a view is for.
  Acceptance rows updated. The predicted aggregate-naming question **resolved cleanly**: each path resolves to
  what its own SELECT list uses (`GetSelectName(true)` on-the-fly, `IsAggregate ? Property.Name : Name`
  persistent), so the "or record the divergence" escape hatch is not needed. Quoting left untouched, per
  TASK-110.
- step 4 — fix in `Birko.Data.SQL.View/SQL/DataBase_ViewOrderBy.cs` (new: `DataBase.ResolveViewOrderFields`
  + `GetViewOrderKey`), `SQL/Connectors/AbstractConnector_SelectView.cs` and
  `AbstractAsyncConnector_SelectView.cs` (resolve in the `Select(Tables.View, …)` funnel, after
  `usePersistent`; the two expression-keyed overloads now pass the view property name), and
  `Birko.Data.SQL.View.projitems` (register). Tests in
  `Birko.Data.SQL.Views.Tests/ViewOrderByResolutionTests.cs` (19). Suites: Views 37/37, SQL 342/342,
  SqLite.View 9/9, ViewModel 11/11, View.Migrations 11/11, SqLite 100/100. No CS86xx in any changed file.
  **Found while writing the persistent-aggregate test and filed as TASK-129**: `ViewSelectSqlBuilder` emits a
  double alias for an aggregate (`COUNT(VOrders.PersonId) as COUNT AS "OrderCount"`), a syntax error on every
  provider, so no persistent aggregate view can be created at all. Out of scope here — different function,
  different defect (invalid DDL, self-reporting) — so that one test uses hand-written DDL matching what the
  generator intends, with the reason in the test comment.
- step 5 — reverted the wiring only (`git stash push` of the two `*_SelectView.cs` files, leaving the resolver
  and all tests in place): Views.Tests **22/38 — 16 failed**, i.e. 16 of the 20 new cases are fix-dependent.
  Fix-dependent (16): `OnTheFly_sorting_by_a_view_property_returns_ordered_rows`, `..._descending_reverses_it`,
  `OnTheFly_emits_the_table_qualified_source_column`, `Persistent_sorting_by_a_view_property_returns_ordered_rows`,
  `Persistent_emits_the_bare_source_column_not_the_qualified_one`, `Async_path_sorts_by_a_view_property_too`,
  `Multi_key_view_sort_applies_the_keys_in_order`, `OnTheFly_aggregate_sort_uses_the_aggregate_expression`,
  `Persistent_aggregate_sort_by_the_sql_function_name_resolves_to_the_alias`,
  `OnTheFly_batch_separator_payload_cannot_create_a_table`,
  `Persistent_batch_separator_payload_cannot_create_a_table`,
  `A_comment_payload_cannot_override_the_callers_limit`, `A_subquery_payload_is_rejected`,
  `The_async_path_rejects_the_same_payload`, `An_unknown_sort_key_names_the_key_and_the_view`,
  `A_column_of_a_source_table_that_the_view_does_not_project_is_rejected`.
  Contract pins, NOT evidence (4): the three back-compat cases
  (`Sorting_by_the_source_column_name_still_returns_the_same_rows`,
  `Persistent_sorting_by_the_source_column_name_is_unchanged`, `No_order_by_emits_no_ORDER_BY_clause`) **and
  `Persistent_aggregate_sort_uses_the_view_property_alias`** — the first measurement showed that one passing
  with the fix reverted, because on the persistent path an aggregate's view property name and its resolved
  column name coincide, so the unresolved key was accidentally already right. Rather than let it read as
  proof, `Persistent_aggregate_sort_by_the_sql_function_name_resolves_to_the_alias` was added (`ByName("COUNT")`
  — a real field name that is NOT the persistent column) and it does fail when reverted.
- step 6 — respecced `views-and-aggregation` (the covering area). `.map.yml` gained the three changed
  `Birko.Data.SQL.View` files for it — that project is otherwise out of scope, but this spec documents the
  behaviour those files implement, so leaving them unmapped would be silent under-coverage. Requirements
  changed: the scenario *"Order-by keys are interpolated verbatim…"* retitled and rewritten to
  *"…are resolved against the view's fields, per path"* (its title asserted the removed behaviour); the *SQL
  view store execution* SHALL clarified that `TranslateOrderBy` passes view property names through on purpose;
  new requirement *"View sort keys are resolved against the view's field metadata, per path"* with 4 scenarios.
  Also updated `bulk-filter-operations`, whose known-gap scenario recorded this defect as open. Diff reviewed:
  nothing unintended.
- step 7 — gate clean (no blockers, no warnings; `Recent Updates` carved out for EPIC-014 by CLAUDE.md). The
  correctness pass over the diff found one NRE path — the persistent branch dereferenced `field.Property` for
  an aggregate matched on `Name` alone — now guarded. Committed 576707c (Birko.Data.SQL.View) / 4c7962d
  (Birko.Data.SQL.Views.Tests).
