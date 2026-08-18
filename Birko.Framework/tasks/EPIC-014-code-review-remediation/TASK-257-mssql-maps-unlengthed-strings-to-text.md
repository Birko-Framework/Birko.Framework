---
id: TASK-257
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-242, TASK-243, TASK-256]
findings: []
pr: null
github-issue: null
jira-key: null
---

# On MSSql an unlengthed `string` column becomes `TEXT`, so **no predicate on it works**

## Context — found by consumer Symbio (TASK-472) running a real entity against MSSql 2022

A `DeleteWhereAsync(x => x.Label == "a")` against MSSql 16.00.4265 failed, and not on anything to do with the
boundary it was verifying:

```
Microsoft.Data.SqlClient.SqlException :
  The data types text and nvarchar are incompatible in the equal to operator.
   at AbstractAsyncConnector.ReadOnAsync (AbstractAsyncConnector.cs:376)
```

`MSSqlConnector.ConvertType` (`Database/Connector/MSSqlConnector.cs:187-199`) maps `DbType.String` and friends
to `NVARCHAR(n)` **only when the field is a `CharField` with a declared length**, and to **`TEXT`** otherwise:

```csharp
case DbType.String:
…
default:
    if (field is CharField charField) return string.Format("NVARCHAR({0})", charField.Lenght);
    else                              return "TEXT";
```

`TEXT` is deprecated in SQL Server and **cannot be used with `=`, `<>`, `GROUP BY`, `ORDER BY` or `DISTINCT`**.
Parameters bind as `nvarchar`, so every comparison is a type clash.

## Why it matters

A plain `public string Name { get; set; }` with no length attribute is the common shape in consumer entities.
On MSSql that makes every `FindAsync`/`FindAllAsync`/`CountAsync`/`DeleteWhereAsync` predicate touching a
string column throw, along with any `SortBy` over one.

⚠ Consumer Symbio's `CLAUDE.md` § *Pravidla pre repository predikaty* lists `==`, `!=`, `Contains`,
`StartsWith`, `ToLower()` on a string column in its **SAFE** column. That is true on SQLite and PostgreSQL and
**false on MSSql for any unlengthed string** — a rule verified on one backend and assumed on the others, which
is the failure mode that section itself warns about.

⚠ Never exercised: consumers test on SQLite, and the MSSql live suites added by TASK-242/243 assert on bulk
writes and lazy init rather than on string predicates.

## What to decide

1. **Map unlengthed strings to `NVARCHAR(MAX)`.** Supports comparison, fixes the whole class. Changes the
   declared type of existing columns — but a `TEXT` column was already unusable in a predicate, so no existing
   deployment can have depended on that behaviour.
2. **Require a length** — make an unlengthed `string` a schema-time error instead of a silent `TEXT`. Honest,
   and it would have caught this at first use; breaking for every consumer.
3. **Keep `TEXT` and cast in predicates** (`CAST(col AS NVARCHAR(MAX)) = @p`). No schema change, but makes
   every string predicate non-sargable — trades a hard failure for a silent performance cliff, the worse
   outcome.

Option 1 is the obvious candidate; the decision to record is `NVARCHAR(MAX)` versus a default length, and what
happens to existing `TEXT` columns.

## Acceptance criteria

- [ ] A decision recorded with its reason, stating what an unlengthed `string` means on MSSql.
- [ ] Against a real MSSql: `==`, `Contains`/`StartsWith`/`EndsWith`, `ToLower()` on a string column, and a
      `SortBy` over one, all execute and return the **right rows** — asserted on values, not on the absence of
      an exception.
- [ ] An MSSql live fixture whose probe entity has an **unlengthed** string, so the suite covers the shape
      consumers actually write.
- [ ] Stated explicitly what happens to an MSSql database that already holds `TEXT` columns.
- [ ] Proven able to fail.

## Out of scope

- The transaction boundary — TASK-242/243. This is column typing; it merely blocked two of that verification's
  member/provider pairs.
- PostgreSQL's UTC `DateTime` COPY defect — TASK-256, the sibling found in the same consumer run.

## Human test plan

- [ ] N/A — mechanical; the proof is a predicate executing against a real MSSql and returning the right rows.
