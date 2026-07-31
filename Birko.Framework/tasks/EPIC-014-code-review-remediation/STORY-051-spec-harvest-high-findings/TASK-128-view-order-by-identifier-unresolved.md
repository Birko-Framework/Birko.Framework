---
id: TASK-128
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-110]
pr: null
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

- [ ] View ORDER BY keys are resolved against the view's field metadata; the emitted identifier is always a
      name drawn from that metadata, never caller-supplied text
- [ ] Both view paths are covered — persistent (`CreatePersistentViewSelectCommand`) and on-the-fly — and the
      test names say which is which
- [ ] Sorting a view by a renamed / remapped view property returns correctly ordered rows
- [ ] The aggregate-column sort key resolves consistently on each path, or the divergence is recorded
      explicitly in the spec as a deliberate limitation
- [ ] `ByName` injection payloads are rejected: the batch-separator payload
      (`Rank; CREATE TABLE Pwned (x INTEGER); --`) asserts the side effect is absent, as in
      `Birko.Data.SQL.SqLite.Tests.OrderByResolutionTests`
- [ ] An unresolvable view sort key throws naming the key and the view
- [ ] A view sort that works today keeps emitting the same SQL
- [ ] Tests in `Birko.Data.SQL.Views.Tests` and/or `Birko.Data.SQL.SqLite.View.Tests`
- [ ] `/specs regen` for `views-and-aggregation` — its "interpolated verbatim" scenario becomes wrong once
      this lands, and `bulk-filter-operations.md`'s known-gap scenario has to go with it

## Out of scope

- The entity-store path — done in [[TASK-110]].
- `SH-H023` (`RuleConditionConverter`, an unresolved identifier in the WHERE clause) — [[TASK-111]].

## Human test plan

N/A — covered by automated tests.
