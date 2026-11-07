---
id: TASK-832
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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
→ **Split 2026-11-07 (the user's decision): chunking is TASK-833.** This task closed on the super-linear cost.

## Acceptance criteria

- [x] **Measured first, in the framework:** a Release benchmark or test separates SQL build, parameter binding and
      execution at N = 500, 1000, 2000 and 4000 on SQLite, and the super-linear step is named. If PostgreSQL, SQL
      Server or MySQL shows the same curve (live suites per `live-tests.yml`), that is measured too.
      → `Task832InListCostTests.Measure_the_stages_of_an_in_list_read` (Release, SQLite, 4000 rows). Every stage is
        linear or small except one: **`DataBase.GetGeneratedQuery`**, the diagnostic copy of the SQL with values
        substituted, built eagerly by every SQL read (`ReadOnAsync` and 8 other call sites).

        | ids | parse | render | raw SQLite | GetGeneratedQuery | ReadAsync |
        |---|---|---|---|---|---|
        | 500 | 16 ms | 11 ms | 4 ms | 72 ms | 133 ms |
        | 1000 | 0.5 ms | 18 ms | 17 ms | 655 ms | 687 ms |
        | 2000 | 0.6 ms | 50 ms | 47 ms | 6,520 ms | 6,825 ms |
        | 4000 | 1.2 ms | 166 ms | 96 ms | (not run) | 87,382 ms |

        The cause: one `StringBuilder.Replace` per parameter, each scanning the whole text of a builder that every
        replacement fragmented further.
      → **Not measured live on PostgreSQL / SQL Server / MySQL.** `GetGeneratedQuery` is provider-agnostic and every
        SQL provider's read path calls it, so the cost and the fix apply to all of them. The live curves are not
        recorded.
- [x] The step named above is fixed. The same benchmark shows roughly linear growth (each doubling ≤ ~2.5×) up to at
      least 4000 ids on SQLite. A regression test guards it without being timing-flaky, for example by counting
      operations, or with a generous bound at a large N.
      → `GetGeneratedQuery` makes one pass: at each position where a parameter name can start, it substitutes the
        longest name that matches there, through a span lookup. After the fix: 1.4 / 0.4 / 0.9 / 21 ms, and
        `ReadAsync` 56 / 39 / 130 / 289 ms. From 2000 to 4000 that is 2.2×.
      → Guards:
        - `The_diagnostic_query_stays_linear_in_the_parameter_count`: 20,000 parameters under 5 s. On the old code it
          did not finish in 300 s.
        - `The_longest_name_wins_where_one_name_prefixes_another`: the old behaviour, kept.
        - `A_value_that_contains_a_parameter_name_is_not_rewritten`: red on the old code.
      → Left over and small, both handed to TASK-833: `AddParameter`'s linear `Contains` (render 186 ms at 4,000) and
        SQLite's own named binding (99 ms at 4,000).
- [ ] A filtered read whose IN list would exceed the provider's limit (all parameters counted) is split
      transparently and returns exactly the rows the single query would. `IN` is a union and `NOT IN` an intersection,
      each pinned by a test. Per provider, the chunk size and limit are named. — ⚠ NOT MET — split to TASK-833
- [ ] An IN under OR or a nested expression, and a chunked read with limit, offset or sort, are either correct or
      refused with an exception that names the reason. They are never silently wrong. — ⚠ NOT MET — split to TASK-833
- [ ] Count, delete and update with a chunked IN filter are handled, or refused with a stated limitation. A chunked
      delete or update can never widen its scope (rules 1–3). — ⚠ NOT MET — split to TASK-833
- [ ] MongoDB, RavenDB, Cosmos DB, Elasticsearch, InfluxDB and the in-memory and file stores are each stated as
      needing no chunking, or handled, with the reason. — ⚠ NOT MET — split to TASK-833
- [x] Symbio's repro shape runs through `FindAllAsync` on SQLite at 2000 ids in a time recorded here, against
      Symbio's 6.6–7.1 s.
      → Through the framework's `AsyncSQLiteStore.ReadAsync` (Release): 130 ms at 2000 ids. That is not through
        Symbio's `RepositoryBase` and tenant decorator, and Symbio measured in Debug. Symbio's own re-measurement is
        its confirmation (§ Human test plan).

## Out of scope

- Symbio's own batching (Symbio TASK-1077 / TASK-1074).
- Chunking a list past the provider's parameter limit → TASK-833.

## Human test plan

N/A, provided the Release benchmark and the regression tests in the criteria cover it: build/bind cost and chunk
semantics are measurable by machine. Symbio re-measuring its catalogue listing after the fix is its own confirmation,
not this task's gate.

## Implementation plan
