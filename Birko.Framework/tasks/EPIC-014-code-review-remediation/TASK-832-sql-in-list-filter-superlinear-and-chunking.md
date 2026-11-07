---
id: TASK-832
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# A SQL `ids.Contains(x.Col)` filter costs super-linear time in the list, and a list past the provider's limit is not split

## Context

Reported by Symbio (session symbio-32, on the user's word, 2026-11-07). The text is kept close to the report.
Symbio waits for this fix instead of chunking on its side. The user decides the order.

**Symptom, measured by Symbio** on real SQLite (`AsyncSQLiteStore`, a Symbio `RepositoryBase` with the tenant
decorator), in a Debug build, against 2,000 Products and 4,000 BasePrices. Each call is one
`FindAllAsync(x => ids.Contains(x.Col))`:

| ids | BasePrices `ids.Contains(x.ProductGuid)` | Products `ids.Contains(x.Guid!.Value)` (PK) |
|---|---|---|
| 500 | 107 ms | 78 ms |
| 1000 | 742 ms | 646 ms |
| 2000 | 6602 ms | 7108 ms |

- Each doubling costs about 7–9×.
- The primary-key lookup degrades the same way, so the cause is not a missing index. It lies in building, binding
  or executing the IN list.

**Where Symbio looked (nothing pinned):**
- `InConditionStrategy.BuildInClause` adds one parameter per value.
- `SqlBuilderContext.GenerateParameterName` produces `@WHERE{field}{i}_{command.Parameters.Count}` and runs
  `_sanitizeRegex.Replace` on every value.
- `_connector.AddParameter` follows.
- Candidates:
  - something O(n) per parameter: a name lookup, or `Parameters.Contains` / `IndexOf` in the connector;
  - Microsoft.Data.Sqlite binding by name, looking each SQL parameter up in the collection;
  - re-parsing the SQL.
- A profiler, or a micro-benchmark that separates build from execute, will tell which.

**Why it matters:**
- Symbio TASK-1077 batches the pricing engine. Each kind of row is read once with `productIds.Contains(...)`, so a
  price-ordered catalogue listing (Symbio TASK-1074) prices the whole matching set.
- Batching cut 500 products from 2.9 s to 0.15 s, but at 2,000 the IN reads alone take about 13 s, slower than the
  old per-product loop.
- Target: a few thousand ids per IN in roughly linear time, on every SQL provider.

**Repro shape:** create N rows, then time `FindAllAsync(x => ids.Contains(x.Guid!.Value))` for N = 500, 1000 and 2000.

**Chunking (Symbio's second message, asked for in this same task).** A filtered read whose `ids.Contains(x.Col)` list
is longer than the provider can bind, or longer than a chunk size that keeps it fast, is split by the framework into
several IN chunks, and the results are combined. Callers keep writing one `FindAllAsync(x => ids.Contains(...))`.
Points to settle:
- **The limit counts every parameter in the command,** not only the list's. Known limits:
  - SQL Server allows 2100, so a 2000-id IN plus the tenant and soft-delete parameters is already near it;
  - SQLite allows 32766;
  - PostgreSQL and MySQL have their own limits.
- **Semantics across chunks:**
  - `IN` is a union of the chunks;
  - `NOT IN` is an intersection: the AND of the chunks' NOT INs, never a union;
  - an IN inside an OR or a nested expression is handled, or refused loudly;
  - a `limit`, an `offset` or a sort makes chunking a different query (a global sort and page), so it is refused or
    done correctly. Symbio's batch reads use none of these.
- **Count, delete and update** with the same IN filter get the same treatment, or a stated limitation. Rules 1–3 on
  scope guards apply to a chunked delete or update.
- **MongoDB (`$in`) and the in-memory stores** probably need no chunking. Say so explicitly, per provider.

The super-linear cost needs its own fix. Chunking alone would only hide it inside each chunk.

**Size.** Filed as one task because Symbio asked for one. These are two separable units: the build/bind cost, and
chunking with its semantics on every SQL provider. Planning should judge it against `slicing.md` and spawn chunking as
its own task if they do not fit one context.

## Acceptance criteria

- [ ] **Measured first, in the framework:** a Release benchmark or test separates SQL build, parameter binding and
      execution at N = 500, 1000, 2000 and 4000 on SQLite, and the super-linear step is named. If PostgreSQL, SQL
      Server or MySQL shows the same curve (live suites per `live-tests.yml`), that is measured too.
- [ ] The step named above is fixed. The same benchmark shows roughly linear growth (each doubling ≤ ~2.5×) up to at
      least 4000 ids on SQLite. A regression test guards it without being timing-flaky, for example by counting
      operations, or with a generous bound at a large N.
- [ ] A filtered read whose IN list would exceed the provider's limit (all parameters counted) is split
      transparently and returns exactly the rows the single query would. `IN` is a union and `NOT IN` an intersection,
      each pinned by a test. Per provider, the chunk size and limit are named.
- [ ] An IN under OR or a nested expression, and a chunked read with limit, offset or sort, are either correct or
      refused with an exception that names the reason. They are never silently wrong.
- [ ] Count, delete and update with a chunked IN filter are handled, or refused with a stated limitation. A chunked
      delete or update can never widen its scope (rules 1–3).
- [ ] MongoDB, RavenDB, Cosmos DB, Elasticsearch, InfluxDB and the in-memory and file stores are each stated as
      needing no chunking, or handled, with the reason.
- [ ] Symbio's repro shape runs through `FindAllAsync` on SQLite at 2000 ids in a time recorded here, against
      Symbio's 6.6–7.1 s.

## Out of scope

- Symbio's own batching (Symbio TASK-1077 / TASK-1074).

## Human test plan

N/A, provided the Release benchmark and the regression tests in the criteria cover it: build/bind cost and chunk
semantics are measurable by machine. Symbio re-measuring its catalogue listing after the fix is its own confirmation,
not this task's gate.

## Implementation plan
