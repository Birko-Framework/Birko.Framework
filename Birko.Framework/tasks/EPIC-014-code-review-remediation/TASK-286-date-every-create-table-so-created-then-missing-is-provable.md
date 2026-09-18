---
id: TASK-286
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-30
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Date every `CREATE TABLE`, so "created and then missing" stops being unprovable

## Context

Instrumentation for consumer **Symbio TASK-602**, where a table was created and then reported missing
**19 log lines later**, and **eleven hypotheses** failed to name why.

Every mechanism reachable by *reading* the code was eliminated there: the store's init gate always runs,
it is not overridden or bypassed, the rollback guard is measured working, the store is a singleton, and
`AmbientSqlTransaction` is a per-flow `AsyncLocal`. The remaining candidates are timing or visibility
effects that only an **observation** can separate — so guessing further was the wrong instrument.

## What it does

`AbstractConnector` records the time of each completed `CREATE TABLE` (`TablesCreated`), and
`EnsureSchemaAndReport` annotates the exception it **already throws**:

```
… [schema-ensure escape: reported missing at <t2>, but this connector already created it —
   CustomerAccounts created <t1>. The store's init gate had therefore passed, so the table was
   created and later found absent; these two timestamps bound the window]
```

…and when there is no such record, it **says so** rather than staying silent. The two cases must be
distinguishable: a table never created is ordinary lazy schema-ensure; a table created and then missing
is the anomaly.

⚠ **The exception message, not an event** — measured, not assumed. Symbio surfaces connector diagnostics
through **boot-time** checks (`UniqueIndexDataCheck`, `SchemaDriftCheck`), which by construction cannot
see a runtime escape, and it subscribes to no connector event. An event would have been recorded nowhere.

⚠ **Worth doing even though TASK-285 removed the 500.** That fix masked only the `COUNT` shape. A
**write** hitting the same condition still throws — deliberately, per TASK-277 — so the underlying fault
is untouched; only the statement shape that happened to be observed twice is quiet now. This makes the
next occurrence self-reporting, which matters *because* the loud symptom is gone.

⚠ **Diagnostic only — nothing branches on it.** And `TablesCreated` is **not** an inventory and **not**
proof of existence: schema-ensure is lazy, so an untouched table is absent from it while existing happily
in an older database, and a create rolled back with a caller's boundary stays recorded. It dates an
event; it does not assert a state — the same caveat `IndexCreationFailures` carries, for the same reason.

## Acceptance criteria

- [x] Each completed `CREATE TABLE` is recorded with a timestamp, in the single
      `CreateTable(string, fields)` chokepoint every overload funnels through.
- [x] A create that **threw** is not recorded — recording an event that never happened is worse than
      recording nothing.
- [x] The escape message names the earlier create and its timestamp.
- [x] The no-record case is stated explicitly rather than silently omitted.
- [x] Mutation-proven both ways: stop recording creates → 3 of 5 red; remove the annotation → 2 of 5 red.
- [x] No behaviour change — nothing reads the map to make a decision. Full suite 261/261, and Symbio
      builds against it and passes 2223/2223.

## Out of scope

- **The root cause itself** — Symbio TASK-602 stays open. This makes the next occurrence diagnostic; it
  does not explain the previous ones.
- **Surfacing it as a boot check or an event** — deliberately rejected above, with the measurement.

## Human test plan

- [x] N/A — the verdict is the content of an exception message, which an assertion measures better than
      a person.

## Implementation plan

_Landed with the task; see `Birko.Data.SQL` 75ec192 and `Birko.Data.SQL.SqLite.Tests` 3fa9136._
