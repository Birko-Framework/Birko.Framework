---
id: TASK-110
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
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

**As a silent wrong answer (SH-M022).** `OrderBy<T>.ToDictionary()` keys by `member.Member.Name`
(`OrderBy.cs:81/90`) — the **CLR property name** — and `ReadCore` hands that to the string-keyed
`Connector.Select` overload. So ordering by a `[NamedField("col")]`-remapped property emits `ORDER BY` on a
column that does not exist, and `RunReaderCommand` swallows the error (`InitException` / `yield break`): the
read **returns empty instead of throwing**. No injection required, no attacker required — any consumer with
a remapped or ModelMap'd column and a sort hits it.

The two are one fix, which is why they are one task. And the resolution already exists on the sibling path:
the **expression**-keyed overload (`AbstractConnector_Select.cs:15`) maps through
`DataBase.GetField().GetSelectName()`, and the aggregate path uses `ResolveSqlName`. Only the store path
does neither.

## Approach

Route every ORDER BY key through the same field resolution the expression-keyed overload uses, then quote
the result with `QuoteIdentifier`. An unresolvable key must **throw**, not degrade — a sort column that
does not map is a programming error, and today it silently returns zero rows.

`OrderBy<T>.ByName(string)` is the API that makes user input reachable. It cannot simply be removed
(consumers sort by a request field), so it must resolve through the same table metadata as the expression
form; the resolution *is* the whitelist. Check whether it should carry a distinct name or an XML doc
warning once it validates.

## Acceptance criteria

- [ ] ORDER BY keys are resolved through the table's field map (the `GetSelectName()` path) and emitted
      quoted
- [ ] Ordering by a `[NamedField("col")]`-remapped property returns **correctly ordered rows**, not an
      empty sequence (SH-M022) — asserted end-to-end
- [ ] `OrderBy<T>.ByName("…injection…")` throws rather than reaching `CommandText`; a batch-separator
      payload is covered explicitly
- [ ] An unresolvable ORDER BY key throws with a message naming the key, instead of yielding zero rows
- [ ] Sync and async paths both fixed (`DataBaseBulkStore.cs:44`, `AsyncDataBaseBulkStore.cs:61/68`)
- [ ] Ordering by a normally-named property is byte-identical in emitted SQL where it already worked, or the
      change is limited to added quoting — no consumer's working sort changes meaning
- [ ] Regression tests in `Birko.Data.SQL.Tests` (emitted SQL, injection rejection) and
      `Birko.Data.SQL.SqLite.Tests` (remapped-column ordering returns rows)
- [ ] `/specs regen` for `bulk-filter-operations`, spec diff reviewed

## Out of scope

- `SH-H023` — the same *class* of defect (an unquoted, unresolved identifier reaching the WHERE clause) but
  in `RuleConditionConverter`, a different file with a different resolution need. Tracked as [[TASK-111]].
  Both should end up calling `DataBase.ResolveColumnName`; do not merge the fixtures.
- `SH-M025` (`ReadCore` handing out a lazy iterator that holds an open `DbConnection`) — same line number,
  unrelated defect, and its CONFIRMED verdict is a mis-paste of this finding's. See [[STORY-053]].
- `SH-M024`/`SH-M026` (`PropertyUpdate` value handling).

## Human test plan

N/A — covered by automated tests. The SQLite-backed assertion that a remapped column now sorts and returns
rows is the user-visible consequence, and it is automatable.
