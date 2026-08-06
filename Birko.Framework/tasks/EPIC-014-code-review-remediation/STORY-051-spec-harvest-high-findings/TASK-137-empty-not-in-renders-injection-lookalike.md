---
id: TASK-137
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-03
depends-on: [TASK-109]
blocks: []
related: [TASK-109]
pr: null
github-issue: null
jira-key: null
findings: []
---

# An empty `NOT IN` renders `1 = 1` — indistinguishable from `' OR 1=1--` in a query log

## Context

Found while planning [[TASK-109]] (2026-08-03), which proposed rendering every "matches everything"
predicate as `WHERE 1 = 1` and had that design **rejected on this exact ground**. The rejection then applies
to what already ships.

`../Birko.Data.SQL/SQL/Connectors/Strategies/InConditionStrategy.cs:33` renders an empty value set as a
constant with the same set semantics:

```csharp
if (IsEmpty(condition.Values))
    return condition.IsNot ? "1 = 1" : "1 = 0";
```

Both are **semantically correct** and the reasoning for them is sound and written out in the file: `Col IN ()`
is a syntax error on PostgreSQL and MSSQL, `1 = 0` / `1 = 1` are valid on every supported dialect, need no
parameters, and compose inside AND/OR chains exactly as a real `IN` would. Shipped 2026-07-27; this task does
not dispute any of that.

The problem is **operational, not semantic**: `1 = 1` is the signature of the most recognisable SQL-injection
payload (`' OR 1=1--`). Emitting it during normal operation puts a false positive into query logs, alert
rules and audit trails — and the cost is not the noise, it is that operators learn to scroll past the pattern
they are supposed to react to. A defence that trains people to ignore it is worse than no signal.

Reachable today whenever a caller passes an empty collection to a negated `Contains` —
`ids.NotContains(x.Field)` shapes, i.e. `!ids.Contains(x.Field)` with `ids` empty, which the empty-`IN` work
of 2026-07-27 deliberately made a *legitimate* translation rather than an error.

## Approach

The semantics to preserve: **empty `NOT IN` matches every row.** Three candidate renderings, and the third is
the one worth pursuing:

1. **`WHERE TRUE`** — ruled out already, and for the same reason `1 = 1` was chosen: valid on
   PostgreSQL/MySQL/SQLite, a **syntax error in T-SQL**. Portability is why the constant idiom exists.
2. **A different always-true constant** (`0 = 0`, `'a' = 'a'`) — cosmetic reshuffling. Any always-true
   comparison reads like a tautology probe; this trades one lookalike for a less familiar one.
3. **Drop the term instead of rendering it.** `A AND TRUE ≡ A`, so an always-true condition inside an AND
   chain can be *removed* rather than emitted, and a chain that reduces to nothing means no `WHERE` at all.
   This produces the cleanest SQL — the query says what it means — and introduces no constant.

**Option 3 interacts directly with [[TASK-109]] and must land after it.** "The chain reduced to nothing, so
emit no `WHERE`" is precisely the shape TASK-109 refuses on destructive paths. The two must agree: a
*reduced-away* always-true term on a `DELETE` has to reach TASK-109's deliberate-all-rows path (the same one
`x => true` maps to), not its refusal path — otherwise `!emptyIds.Contains(x.Field)` on a delete starts
throwing, which would be a regression, not a fix.

Note the asymmetry: `1 = 0` (empty `IN`, matches nothing) is **not** a problem and should stay. It has no
injection connotation, and dropping an always-false term is not sound anyway — `A AND FALSE` is `FALSE`, not
`A`. Only the always-true side is in scope.

## Acceptance criteria

- [ ] An empty `NOT IN` no longer emits `1 = 1`; the term is dropped from its AND chain and the emitted SQL
      is what the remaining conditions say
- [ ] An empty `NOT IN` as the **sole** condition emits no `WHERE` on reads, and reaches
      [[TASK-109]]'s deliberate-all-rows path (not its refusal) on `DELETE` / `UPDATE`
- [ ] Set semantics are unchanged and asserted against the compiled-delegate oracle: empty `NOT IN` matches
      every row, empty `IN` matches none
- [ ] `1 = 0` for an empty `IN` is **unchanged** — an always-false term cannot be dropped, and it carries no
      injection connotation
- [ ] Always-true terms inside `OR` chains are handled correctly too — `A OR TRUE` is `TRUE`, so the chain
      collapses rather than dropping the term (dropping it would silently narrow the result)
- [ ] `grep -rn "1 = 1"` over the emitted-SQL paths returns nothing outside tests
- [ ] Regression tests in `Birko.Data.SQL.Tests` (statement text) and `Birko.Data.SQL.SqLite.Tests`
      (end-to-end row sets), including a `DELETE` with an empty `NOT IN` — the case where this task and
      TASK-109 meet
- [ ] `/specs regen` for `filter-expression-translation` and `bulk-filter-operations`, spec diffs reviewed

## Out of scope

- The empty-`IN` → `1 = 0` rendering (correct, and keeping it is a criterion above).
- Widening what the parser can translate — [[TASK-109]] `## Out of scope` applies here unchanged.
- Auditing consumers' log-alerting rules. This task removes the false signal at the source; what anyone
  greps for is their own configuration.

## Human test plan

N/A — covered by automated tests. Statement text is assertable directly and the SQLite-backed suites cover
the row-set consequence.
