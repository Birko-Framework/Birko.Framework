---
id: TASK-111
parent: STORY-051
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H023]
---

# `rule.Field` reaches the WHERE clause unresolved and unquoted

## Context

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:121` — **CONFIRMED**.

`ConvertLeaf` constructs `new Condition(rule.Field, values, …)`. `rule.Field` is an arbitrary string off the
rule, and it becomes the condition **name** with no property resolution and no quoting at this layer. All
five condition strategies then interpolate `Name` raw — `EqualConditionStrategy:26` is
`$"{condition.Name}{op}{value}"`.

Two consequences, and the benign one is more likely to be hit:

- **Wrong column.** No `LoadTable` / `GetFieldByPropertyName` lookup happens, so a `[NamedField]`-remapped
  property references a column that does not exist, and nothing qualifies the name with a table — making it
  ambiguous in a join.
- **Injection.** A rule tree is *configuration data*, and `docs/rules.md` advertises this path as producing
  a "direct WHERE clause". A `Field` of `1=1 OR 1=1 --` becomes executable SQL. Whether that is reachable by
  an attacker depends on whether a consumer lets users author rules — but the docs invite exactly that.

`DataBase.ResolveColumnName` exists and is not called here.

Rated P1 rather than P0 because reaching it requires a rule tree with a caller-influenced `Field`, whereas
[[TASK-110]]'s twin defect fires for any consumer with a remapped column and a sort. Same root cause, same
intended fix helper.

## Approach

Resolve `rule.Field` through the table metadata before constructing the `Condition`, and quote the resolved
column. An unresolvable field must **throw** — a rule referencing a non-existent property is a
configuration error, and today it produces either a SQL error or, worse, valid SQL that means something
else.

Check whether `Condition.Name` should hold a *resolved, quoted* value as an invariant rather than leaving
each of the five strategies to interpolate a raw string — if the type guaranteed it, this class of bug could
not recur in a sixth strategy.

## Acceptance criteria

- [ ] `rule.Field` is resolved via `DataBase.ResolveColumnName` (or the same field map) and quoted before it
      reaches any condition strategy
- [ ] A `[NamedField("col")]`-remapped property in a rule filters on the **right column**, asserted
      end-to-end
- [ ] A `Field` containing SQL (`1=1 OR 1=1 --`, a batch separator, a quote-escape attempt) throws rather
      than reaching `CommandText`
- [ ] An unresolvable `Field` throws with a message naming the field
- [ ] Existing rule trees over normally-named properties are unaffected apart from added quoting
- [ ] Regression tests in `Birko.Data.SQL.Tests` (emitted SQL + rejection) and
      `Birko.Data.SQL.SqLite.Tests` (remapped column filters correctly end-to-end)
- [ ] `/specs regen` for `filter-expression-translation`, spec diff reviewed

## Out of scope

- The ORDER BY sink ([[TASK-110]]) — shared root cause, separate file and fixture.
- `SH-H041`–`SH-H044`, the `RuleSpecification` match-all degradations. Those are the *in-memory /
  expression-tree* rule path; this is the SQL-text rule path. Tracked as [[TASK-116]].
- `Birko.Rules`' own `RuleExpressionConverter` — it is the reference implementation here, not a defect.

## Human test plan

N/A — covered by automated tests.
