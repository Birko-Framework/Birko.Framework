---
id: TASK-110
parent: STORY-051
feature: null
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
pr: 2a87f84 (Birko.Data.SQL) / 8e0d2b8 (Birko.Data.Stores) / d845696 (Birko.Data.SQL.Tests) / e84193d (Birko.Data.SQL.SqLite.Tests)
github-issue: null
jira-key: null
findings: [SH-H003, SH-M022]
---

# ORDER BY identifiers reach SQL text unresolved and unquoted

## Context

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:44` — **CONFIRMED**. One call site, two consequences.

`AbstractConnectorBase.cs:558` builds the clause as:

```
" ORDER BY " + string.Join(", ", orderFields.Select(kvp => string.Format("{0} {1}", kvp.Key, …)))
```

The key is interpolated **verbatim** — no `QuoteIdentifier`, no whitelist, not a parameter.

**As an injection sink (SH-H003).** `OrderBy<T>.ByName(string)` accepts an arbitrary caller string, so
`ByName(request.Sort)` puts user input straight into `CommandText`, ahead of `LIMIT`/`OFFSET`. Both
`Microsoft.Data.Sqlite` and Npgsql execute multiple statements per command. Reached from
`AsyncDataBaseBulkStore.cs:61/68` as well.

**As a broken sort (SH-M022).** `OrderBy<T>.ToDictionary()` keys by `member.Member.Name`
(`OrderBy.cs:81/90`) — the **CLR property name** — and `ReadCore` hands that to the string-keyed
`Connector.Select` overload. So ordering by a `[NamedField("col")]`-remapped property emits `ORDER BY` on a
column that does not exist. No injection required, no attacker required — any consumer with a remapped or
ModelMap'd column and a sort hits it.

### Step-3 re-verification (2026-07-31) — measured, two corrections

Probed against a real SQLite file with `OnExecute` capturing `CommandText`, on a model with
`[NamedField("label_col")] Label` and a plain `int Rank`.

**SH-H003 holds, and is worse than filed — arbitrary statement execution, measured.** Emitted text and
observed effects:

| `ByName(...)` payload | emitted | effect |
|---|---|---|
| `Rank; CREATE TABLE Pwned (x INTEGER); --` | `… ORDER BY Rank; CREATE TABLE Pwned (x INTEGER); -- ASC` | **`Pwned` was created** — `sqlite_master` count = 1 |
| `Rank LIMIT 1 --` | `… ORDER BY Rank LIMIT 1 -- ASC LIMIT @LIMIT` | returned **1 row of 3** — the caller's `LIMIT` is commented out and overridden |
| `(SELECT count(*) FROM sqlite_master)` | `… ORDER BY (SELECT count(*) FROM sqlite_master) ASC` | executed, rows reordered — arbitrary subquery in the sort key |

No exception in any case. The trailing ` ASC`/` DESC` the builder appends is neutralised by a `--` comment,
so it is not a mitigation.

**SH-M022 is REAL but MIS-SCOPED — it throws, it does not return empty.** Filed as *"`RunReaderCommand`
swallows the error … the read returns empty instead of throwing"*. It does not: `RunReaderCommand` guards
`ExecuteReader` with `catch when (IsMissingTableException(ex))`, and that predicate matches only
`"no such table"` (`AbstractConnectorBase.cs:55`). A bad **column** raises
`SqliteException: SQLite Error 1: 'no such column: Label'`, which propagates out of the iterator on first
`MoveNext`. So the consequence is *a leaked provider exception at enumeration and a sort that cannot be
performed at all*, not a silent wrong answer. Still a defect — a remapped column is unsortable — but it is
self-reporting, and the acceptance row that asserted "returns empty" has been corrected below.

**The prescribed remedy "emitted quoted" is WRONG for this codebase and has been dropped.** Measured DDL:

```
CREATE TABLE "PRows" (label_col TEXT, Rank INTEGER NOT NULL, Guid TEXT)
SELECT PRows.label_col, PRows.Rank, PRows.Guid FROM "PRows" ORDER BY Rank ASC
```

The convention is **table identifiers quoted, column identifiers never** — DDL emits bare column names, the
WHERE clause emits `condition.Name` bare through every condition strategy, and the SELECT list emits a bare
`Table.Column` prefix. Quoting only the ORDER BY column would make it the single quoted column identifier in
the statement, and on PostgreSQL it would **break a currently-working sort**: an unquoted DDL identifier is
folded to lower case there, so the column is really `rank` and `ORDER BY "Rank"` would raise *column does not
exist*. That directly violates this task's own last acceptance row. Injection is closed by the *resolution*
(the emitted text becomes a name drawn from table metadata, never caller text) — quoting was never what
closed it. Framework-wide identifier-quoting consistency is a separate question, flagged in the Outcome.

The two are one fix, which is why they are one task. And the resolution already exists on the sibling path:
the **expression**-keyed overload (`AbstractConnector_Select.cs:15`) maps through
`DataBase.GetField().GetSelectName()`, and the aggregate path uses `ResolveSqlName`. Only the store path
does neither.

## Approach

Route every ORDER BY key through the same field resolution the expression-keyed overload uses. An
unresolvable key must **throw**, naming the key and the entity type — a sort column that does not map is a
programming error, and today it reaches the database and fails there, in the provider's words.

`OrderBy<T>.ByName(string)` is the API that makes user input reachable. It cannot simply be removed
(consumers sort by a request field), so it must resolve through the same table metadata as the expression
form; **the resolution *is* the whitelist** — that is the whole mechanism, and it is why quoting is not
needed (see the step-3 block: quoting would additionally break PostgreSQL).

Resolve at the one layer every read funnels through and where metadata is still available —
`Select(IEnumerable<Tables.Table> tables, …)` / `SelectAsync(…)`. Deeper is impossible (the next overload
takes only table *name strings*); shallower means several entry points to guard and one of them will be
missed. The expression-keyed overloads currently pre-resolve to a select name before calling in, which would
double-resolve, so they are changed to pass the property name and let the single funnel resolve it.

## Acceptance criteria

Rescoped at step 3 — the "emitted quoted" row is replaced by "resolved, never caller text", and the
SH-M022 row no longer asserts an empty sequence. See the re-verification block above for the measurements
behind both changes.

- [x] ORDER BY keys are resolved through the table's field map (the `GetSelectName()` path); the emitted
      identifier is **always** a name drawn from table metadata, never caller-supplied text
- [x] Ordering by a `[NamedField("col")]`-remapped property returns **correctly ordered rows** — today it
      raises `no such column: <PropertyName>` at enumeration (SH-M022) — asserted end-to-end
- [x] `OrderBy<T>.ByName("…injection…")` throws rather than reaching `CommandText`; the batch-separator
      payload (`Rank; CREATE TABLE Pwned (x INTEGER); --`) and the `LIMIT`-override payload
      (`Rank LIMIT 1 --`) are both covered, each asserting the side effect is absent
- [x] An unresolvable ORDER BY key throws with a message naming the key **and** the entity type
- [x] A key that is already the mapped **column** name still resolves (it works today and must keep working)
- [x] Sync and async paths both fixed (`DataBaseBulkStore.cs:44`, `AsyncDataBaseBulkStore.cs:61/68`)
- [x] Ordering by a normally-named property is byte-identical in emitted SQL — no consumer's working sort
      changes meaning
- [x] Regression tests in `Birko.Data.SQL.Tests` (resolver unit) and `Birko.Data.SQL.SqLite.Tests`
      (end-to-end emitted SQL, remapped-column ordering, injection rejection, async parity)
- [x] `/specs regen` for the covering area, spec diff reviewed

## Out of scope

- `SH-H023` — the same *class* of defect (an unquoted, unresolved identifier reaching the WHERE clause) but
  in `RuleConditionConverter`, a different file with a different resolution need. Tracked as [[TASK-111]].
  Both should end up calling `DataBase.ResolveColumnName`; do not merge the fixtures.
- `SH-M025` (`ReadCore` handing out a lazy iterator that holds an open `DbConnection`) — same line number,
  unrelated defect, and its CONFIRMED verdict is a mis-paste of this finding's. See [[STORY-053]].
- `SH-M024`/`SH-M026` (`PropertyUpdate` value handling).
- **The view twin, found at step 3 and filed as [[TASK-128]].** `SqlViewStore.TranslateOrderBy`
  (`../Birko.Data.SQL.Views/SqlViewStore.cs:137`) copies `OrderByField.PropertyName` into the dictionary
  with no resolution either, and the view ORDER BY has its **own** emit site
  (`../Birko.Data.SQL.View/SQL/Connectors/AbstractConnectorBase_View.cs:91`) that the entity funnel does not
  pass through. Same root cause, same reachability, but a different project, a different metadata source
  (`Tables.View` rather than `Tables.Table`) and a different funnel — so it is a sibling task, not a widening
  of this one. It shares the resolver this task adds.

## Human test plan

N/A — covered by automated tests. The SQLite-backed assertion that a remapped column now sorts and returns
rows is the user-visible consequence, and it is automatable.

## Outcome

**What the fix was.** `DataBase.ResolveOrderFields` (new, `Birko.Data.SQL/SQL/DataBase_OrderBy.cs`) maps every
ORDER BY key through the selected tables' field metadata — property name first, then mapped column name — and
throws `ArgumentException` naming the key and the entity type otherwise. It is invoked from
`Select(IEnumerable<Tables.Table>, …)` / `SelectAsync(…)`: the last layer that still has column metadata (the
next overload down takes bare table-name strings) and the one layer every SQL entity read funnels through.
The expression-keyed overloads used to pre-resolve to a select name before calling in, which would have
double-resolved, so they now pass the property name and let the single funnel do the work. Because the
emitted identifier is always a name read out of metadata, caller-supplied text cannot reach the clause —
**the resolution is the whitelist**, and that, not quoting, is what closes the injection.

**Step-5 split — 10 fix-dependent, 21 contract pins.** With the wiring reverted (the resolver and every test
left in place): SqLite **90/100**, SQL.Tests **342/342**.

- *Fix-dependent (10, all `Birko.Data.SQL.SqLite.Tests.OrderByResolutionTests`)*:
  `Ordering_by_a_remapped_property_returns_ordered_rows`, `..._descending_reverses_it`,
  `Remapped_property_is_emitted_under_its_column_name`, `Async_path_orders_by_a_remapped_property_too`,
  `Multi_key_sort_applies_the_keys_in_order`, `A_batch_separator_payload_cannot_create_a_table`,
  `A_comment_payload_cannot_override_the_callers_limit`, `A_subquery_payload_is_rejected`,
  `The_async_path_rejects_the_same_payload`, `An_unknown_sort_key_fails_before_reaching_the_database`.
- *Contract pins, NOT evidence*: the 4 back-compat cases that must stay green either way
  (`An_ordinary_property_emits_exactly_what_it_emitted_before`, `Ordering_by_an_ordinary_property_still_sorts`,
  `Ordering_by_the_mapped_column_name_still_works`, `No_order_by_is_still_no_ORDER_BY_clause`) **and all 17**
  `OrderFieldResolutionTests`, which call the resolver directly and therefore cannot detect whether it is
  wired into the read path at all. The wiring is proven only by the SqLite suite — chiefly
  `Remapped_property_is_emitted_under_its_column_name`.

### Judgement calls, and why the stricter option was rejected

- **Not quoted, although the finding prescribed quoting — this is the call worth knowing about.** The
  stricter-looking option (`QuoteIdentifier` on the resolved name) was rejected on measurement: DDL emits
  column names bare (`CREATE TABLE "PRows" (label_col TEXT, Rank INTEGER NOT NULL, Guid TEXT)`), and so does
  every WHERE condition strategy and the SELECT list; only table names are quoted. Quoting solely in ORDER BY
  would make it the one quoted column identifier in the statement and would **break a working sort on
  PostgreSQL**, where an unquoted DDL identifier is folded to lower case — the column is really `rank`, so
  `ORDER BY "Rank"` would raise *column does not exist*. It would have failed this task's own "no consumer's
  working sort changes meaning" criterion. Quoting was never the mechanism that closed the injection.
- **The mapped column name is accepted, not just the property name.** The stricter reading — property names
  only — would have broken consumers calling `ByName("label_col")`, which works today. The column name comes
  from the same metadata, so accepting it does not widen what can be emitted by one character; it is still a
  closed set drawn from the table.
- **Resolution failure throws rather than degrading.** A sort column that does not map is a programming
  error. Degrading to "ignore the key" would silently return unordered rows — the exact class of silent wrong
  answer this family's bug history is made of.
- **SH-M022's acceptance row was corrected before any code was written.** It asserted the read "returns
  empty"; measurement showed a `SqliteException: no such column: Label` propagating out of the iterator,
  because `IsMissingTableException` matches only `"no such table"`. Left as filed, the test would have
  asserted the wrong thing and the acceptance list would have silently redefined "done".

### Two side effects of moving resolution to one place, both verified

- `Select<T,P>(Type[] types, …)` with expression keys used to call `GetSelectName(true)` on a
  `DataBase.GetField` result. `LoadField` never assigns `AbstractField.Table`, so that call threw
  `NullReferenceException` before reaching SQL — confirmed by probe (`Table is null? True`,
  `GetSelectName(true) THREW NullReferenceException`). That path now works. It had no test and no finding id.
- The same overload with a **single-element** array now emits a bare column instead of `Table.Column`.
  Unambiguous with one table, and identical to what the single-type overload already emitted.

### Flagged, not fixed

- **The view twin — filed as [[TASK-128]] (P0).** `SqlViewStore.TranslateOrderBy` passes raw property names
  on, and the view ORDER BY has its own emit site
  (`Birko.Data.SQL.View/…/AbstractConnectorBase_View.cs:91`) that does not pass through the funnel this task
  resolves at. Same root cause and same reachability, but a different project, a different metadata source
  (`Tables.View`) and a different funnel to choose — so it is a sibling task, not a widening. **The injection
  sink is still open on the view path**; `bulk-filter-operations.md` records it as a known gap so the spec
  does not read as if it were closed.
- **Framework-wide identifier quoting is inconsistent, and may be a latent PostgreSQL defect.** Measured:
  `FROM "PRows"` (quoted) while the SELECT list emits `PRows.label_col` (unquoted table prefix) and DDL emits
  unquoted columns. On PostgreSQL a mixed-case table name would be created case-sensitively and then
  referenced folded, which cannot match. Not filed as a task: it needs a live PostgreSQL server to confirm
  (those suites are env-gated) and speculating a defect into the backlog is worse than recording the
  evidence here.
- **Multi-key sort order rests on `Dictionary<string,bool>` insertion order**, which is not contractual.
  Pre-existing — `OrderBy<T>.ToDictionary()` has always returned a `Dictionary` and the clause builder has
  always iterated it — and the resolver preserves the input order, so this is no worse than before. Pinned by
  `Multi_key_sort_applies_the_keys_in_order` and `Direction_and_key_order_survive_resolution` so a change
  would be caught, but the underlying type is still the wrong one for an ordered list.
- **Resolution throws at enumeration, not at the call**, because `Select` is an iterator. Consistent with this
  API's documented laziness (and with the pre-fix behaviour, where `no such column` also surfaced at
  enumeration), so it was left alone rather than eagerly validated in the store.

## Progress log

- step 2 — picked; ranked above TASK-113 (TenantSyncProvider unscoped reads/deletes) because the injection
  half is reachable directly from untrusted input (`ByName(request.Sort)` → `CommandText`) rather than from a
  sync-job configuration, and because TASK-110 has no open design question, while TASK-113's tenant-precedence
  decision is entangled with the still-open TASK-127 and so cannot finish unattended.
- step 3 — verified against a live SQLite file, and RESCOPED twice. SH-H003 holds and is stronger than
  filed: `ByName("Rank; CREATE TABLE Pwned (x INTEGER); --")` **created the table** (measured in
  `sqlite_master`), and `ByName("Rank LIMIT 1 --")` overrode the caller's LIMIT — no exception either time.
  SH-M022 is real but mis-scoped: it raises `SqliteException: no such column: Label` at enumeration rather
  than returning empty (`IsMissingTableException` matches only "no such table"), so it is self-reporting;
  Context and the acceptance row corrected. The prescribed remedy "emitted quoted" is DROPPED: DDL emits
  columns unquoted (`CREATE TABLE "PRows" (label_col TEXT, Rank INTEGER NOT NULL, Guid TEXT)`) and so does
  every WHERE strategy, so quoting only ORDER BY would break mixed-case columns on PostgreSQL — resolution,
  not quoting, is what closes the injection. View twin found and filed as TASK-128 rather than absorbed.
- step 4 — fix in `Birko.Data.SQL/SQL/DataBase_OrderBy.cs` (new: `DataBase.ResolveOrderFields`),
  `SQL/Connectors/AbstractConnector_Select.cs` + `AbstractAsyncConnector_Select.cs` (resolve at the
  `Tables.Table` funnel; expression-keyed overloads now pass property names instead of pre-resolved select
  names), `Birko.Data.SQL.projitems` (register), plus an XML-doc warning on `OrderBy<T>.ByName` in
  `Birko.Data.Stores/OrderBy.cs`. Tests in `Birko.Data.SQL.Tests/DataBase/OrderFieldResolutionTests.cs` (17)
  and `Birko.Data.SQL.SqLite.Tests/OrderByResolutionTests.cs` (14). Suites: SQL 342/342, SqLite 100/100,
  plus Views 18, ViewModel 11, SqLite.View 9, Providers 8, Caching 7, View.Migrations 11, Workflow.SQL 12,
  BackgroundJobs.SQL 17, Migrations.SQL 34, Sync.Sql 7, Models.SQL 4, Models.Users.SQL 9 — all green. No
  CS86xx in any changed file.
- step 5 — reverted the wiring only (`git stash push` of the two `*_Select.cs` files, leaving the resolver
  and all tests in place): SqLite **90/100 — 10 failed**, SQL.Tests **342/342 — 0 failed**.
  Fix-dependent (10, all in `OrderByResolutionTests`): `Ordering_by_a_remapped_property_returns_ordered_rows`,
  `..._descending_reverses_it`, `Remapped_property_is_emitted_under_its_column_name`,
  `Async_path_orders_by_a_remapped_property_too`, `Multi_key_sort_applies_the_keys_in_order`,
  `A_batch_separator_payload_cannot_create_a_table`, `A_comment_payload_cannot_override_the_callers_limit`,
  `A_subquery_payload_is_rejected`, `The_async_path_rejects_the_same_payload`,
  `An_unknown_sort_key_fails_before_reaching_the_database`.
  Contract pins, NOT evidence — 4 back-compat cases that must stay green either way
  (`An_ordinary_property_emits_exactly_what_it_emitted_before`, `Ordering_by_an_ordinary_property_still_sorts`,
  `Ordering_by_the_mapped_column_name_still_works`, `No_order_by_is_still_no_ORDER_BY_clause`) and **all 17**
  `OrderFieldResolutionTests`, which call `DataBase.ResolveOrderFields` directly and so cannot detect whether
  it is wired in at all. The wiring is proven only by the SqLite suite.
- step 6 — respecced `bulk-filter-operations` (partial regen, the covering area). `.map.yml` gained four
  sources for it — `OrderBy.cs`, `DataBase_OrderBy.cs` and both `*_Select.cs` — because the fix landed in
  files **no area glob reached**, which would have been silent under-coverage. Requirement added: "SQL sort
  keys are resolved against table metadata before reaching the statement" (8 scenarios, one of which records
  the view path as a known gap). Diff reviewed: purely additive, no existing requirement reworded, nothing
  unintended. `views-and-aggregation`'s "Order-by keys are interpolated verbatim" scenario is left alone —
  it describes the view path, which is still true and is now TASK-128.
- step 7 — gate clean (no blockers, no warnings; the `Recent Updates` check is carved out for EPIC-014 by
  CLAUDE.md). Committed 2a87f84 (Birko.Data.SQL) / 8e0d2b8 (Birko.Data.Stores) / d845696
  (Birko.Data.SQL.Tests) / e84193d (Birko.Data.SQL.SqLite.Tests).
