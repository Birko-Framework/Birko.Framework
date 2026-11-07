---
id: TASK-833
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-832]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# A SQL `ids.Contains(x.Col)` list longer than the provider can bind is split into chunks transparently

## Context

Split from [[TASK-832]] (the user's decision, 2026-11-07). TASK-832 fixed the super-linear cost of an IN-list read:
the diagnostic query built on every read took 6.5 s of a 6.8 s read at 2,000 ids. 2,000 ids now read in 130 ms and
4,000 in 289 ms on SQLite (Release), about 2.2× per doubling. This task is Symbio's second ask (symbio-32, on the
user's word), kept close to its text.

**Ask:** a filtered read whose `ids.Contains(x.Col)` list is longer than the provider can bind, or longer than a chunk
size that keeps it fast, is split by the framework into several IN chunks, and the results are combined. Callers keep
writing one `FindAllAsync(x => ids.Contains(...))`. Symbio does not chunk on its side.

**Points to settle:**
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
  scope guards apply.
- **MongoDB (`$in`) and the in-memory stores** probably need no chunking. Say so explicitly, per provider.

**Left over from TASK-832's measurement.** Two costs still grow faster than linear, and both are small:
- `AbstractConnectorBase.AddParameter` checks `command.Parameters.Contains(name)` before each add. That is a linear
  scan on SqliteParameterCollection, so rendering 4,000 parameters takes 186 ms.
- SQLite's own binding of named parameters takes 99 ms at 4,000 with no framework involved.

A chunk size caps both. Whether `AddParameter` gets a fast path for names that are unique by construction is this
task's call.

## Decisions (the user, 2026-11-07, before any code)

- **NOT IN past the limit refuses** (`NotSupportedException`, pinned by a test). Splitting it into separate statements
  is wrong for writes: deleting "not in chunk 1" deletes chunk 2's rows. This rewords the first criterion's
  "NOT IN an intersection", before the work.
- **Sort, limit or offset past the limit refuses.** An in-memory merge cannot reproduce the database's collation. Below
  the limit nothing changes.
- **Only non-string lists are chunked** (Guid, numbers, enums, dates, bool). A string list past the limit refuses:
  under a case- or accent-insensitive collation two distinct C# strings can be one database value. A row would then
  match two chunks, so Count would over-count and an increment would apply twice. Symbio's lists are Guids.
- **Defaults accepted:**
  - chunk size 1000 everywhere;
  - `UpdateAsync` / `SelectCountAsync` (and sync twins) become a non-virtual wrapper over a `protected virtual …Core`
    (rule 12), after grepping the consumer repos for overrides;
  - Cosmos DB and Elasticsearch ceilings filed as two small tasks;
  - `AddParameter`'s linear `Contains` deferred unless a re-measurement shows render above ~20% of a read.

## Acceptance criteria

- [x] A filtered read whose IN list would exceed the provider's limit (all parameters counted) is split
      transparently and returns exactly the rows the single query would. `IN` is a union, pinned by a test; `NOT IN`
      past the limit refuses, pinned by a test (decision above). Per provider, the chunk size and limit are named.
      → `InListChunkPlanner` (Birko.Data.SQL, pure) and `AbstractConnectorBase.PlanInListChunks`:
        - **Limits (`MaxCommandParameters`):** SQLite 32766, SQL Server 2098, PostgreSQL 65535 (TimescaleDB inherits
          it), MySQL 65535.
        - **Chunk size (`PreferredInListChunkSize`):** 1000.
        - **The other parameters are counted at render time,** by one probe build on an unopened command.
        - MySQL: `Prepare()` is used only by the bulk update and delete by key, so filtered statements are filled in on
          the client.
      → Pinned by `InListChunkPlannerTests` (29+) and `Task833InListChunkingTests` (SQLite, max 40, chunk 10): a read
        with duplicates and the tenant and soft-delete filters equals the single-query oracle, sync and async; NOT IN
        refuses.
- [x] An IN under OR or a nested expression, and a chunked read with limit, offset or sort, are either correct or
      refused with an exception that names the reason. They are never silently wrong.
      → A shape that cannot be split stays one statement within the limit, so nothing that worked refuses. Past the
        limit it gets `NotSupportedException` before `DoCommand`, naming the operation, count, maximum, column and
        reason, never a value.
      → Reasons: OR, negated group, NOT IN, string values, mixed value types, a `DateTimeOffset` instant at two
        offsets, sub-microsecond or mixed-`Kind` `DateTime` values, sort / limit / offset, aggregate, and an update
        that writes the filtered column.
- [x] Count, delete and update with a chunked IN filter are handled, or refused with a stated limitation. A chunked
      delete or update can never widen its scope (rules 1–3).
      → Count is the sum of the chunks. Delete and update run each chunk through the single-statement path, so the
        whole-table guard and `AddRequiredWhere` check every chunk and every chunk carries a non-empty IN.
      → All chunks share one boundary, `RunAtomically(Async)` on `AbstractConnector`: join the ambient transaction,
        or own one. A failure at chunk 4 or 7 rolls every chunk back (proved red without it), and so does an enclosing
        unit of work that rolls back. `matched` is the sum.
      → Aggregates refuse past the limit.
      → Rule 12: `SelectCount(Async)` and `Update(Async)` are now non-virtual wrappers over `protected virtual …Core`.
        No overrides exist in `Birko.*` or `C:\Source\Birko\Consumers`.
      → **Review blocker, found and fixed before close:** an update whose SET writes the filtered column applied twice
        when chunked. A row moved into a later chunk's values was matched again: scores 0..29 incremented in chunks of
        10 left 9 and 19 incremented twice (reproduced). Such a list is never split now; past the limit it refuses. The
        4 regression tests were red before the fix, and an update of a different column still splits.
      → **Review should-fix:** .NET ticks are finer than PostgreSQL / MySQL microseconds, so date lists split only
        when every value is whole microseconds and of one `Kind`.
- [x] MongoDB, RavenDB, Cosmos DB, Elasticsearch, InfluxDB and the in-memory and file stores are each stated as
      needing no chunking, or handled, with the reason.
      → **No chunking needed:**
        - MongoDB: `$in` has no element limit; the 16 MB BSON command is about 500k Guids.
        - RavenDB: the list binds as one `In<T>` array parameter.
        - InfluxDB, InMemory, JSON, XML: the filter is compiled and run in memory.
      → **Handled in their own tasks:**
        - Cosmos DB: TASK-834, split past 13,105 Guids (measured).
        - Elasticsearch: TASK-835, a bool `should` of terms queries past 65,536 (measured).
- [x] SQL Server, which has the lowest limit (2100), is exercised live per `live-tests.yml` with a list past the limit.
      → `InListChunkingLiveTests` against the local `birko-live-mssql`:
        - 5000 ids through read, count, delete and update;
        - 2098 parameters bind and 2099 fail with Msg 8003, which proves the declared limit (rule 52).
      → On this machine it connects only with `BIRKO_MSSQL_HOST=127.0.0.1`.
      → The live SQL suites ran green with `BIRKO_REQUIRE_LIVE=1`: SQL, PostgreSQL (+View), MySQL (+View), MSSql
        (+View), Providers, Views, Migrations.SQL, BackgroundJobs.SQL and Localization.SQL. TimescaleDB was not run;
        no container is up.

## Out of scope

- The super-linear build cost (TASK-832, done).
- `AddParameter`'s linear `Contains`: TASK-836. Step E, measured after this task: rendering is about a third of a
  1000-value read, past the user's 20% threshold.
- View stores (`Birko.Data.SQL.View`) keep the driver's own error past the limit: TASK-837, together with Cosmos DB's
  view store.
- `RunAtomically` repeats the bulk stores' `Atomically` shape, so there are two producers: TASK-838.
- A chunked read or count outside a transaction is not one snapshot. That is harmless for key lists and stated in the
  XML docs.

## Human test plan

N/A — fully covered by automated tests: chunk semantics and per-provider limits are row counts and refusals, which a
machine asserts.

## Implementation plan

Surveyed by a Plan agent on 2026-11-07; the decisions above are applied. **Steps A and B are independently
completable.**

### Facts the plan rests on
- **The connector funnels.** No provider overrides them; the only per-provider override is `AddParameter`. Chunk once
  here, with a sync and an async twin:
  - **read:** the last `SelectAsync(IEnumerable<string> tableNames, fields, transform, conditions, orderFields, limit,
    offset, ct)` overload in `AbstractAsyncConnector_Select.cs`, and its twin `AbstractConnector.Select(...)` in
    `AbstractConnector_Select.cs`;
  - **count:** `SelectCountAsync(tableNames, joinconditions, conditions, ct)` and `SelectCount(...)`;
  - **delete:** the private `DeleteAsyncAsync(tableName, conditions, ct, allowAllRows)` and `Delete(string, ...)` in
    `AbstractConnector_Delete.cs`;
  - **update:** `UpdateAsync(tableName, fields, values, conditions, isExpressionValues, ct, allowAllRows)` and
    `Update(string tableName, ...)`.
  - `Update(filter, Action<T>)` reads, then updates by key, so chunking the read covers it.
  - Aggregates and Views (`Birko.Data.SQL.View`) are not wired. Aggregates refuse past the limit; Views keep the
    driver's own error, a stated limitation.
- **How conditions combine.**
  - The root list is a flat chain: a term with `IsOr` is joined with OR, otherwise AND.
  - The parser writes every `&&` / `||` as a group with `SubConditions`, whose `IsOr` / `IsNot` apply to all of its
    children.
  - The tenant filter (`ModelByTenant`) and soft delete (`SoftDeleteFilter`) arrive as nested AND groups.
  - `!ids.Contains(x)` produces an `In` leaf with `IsNot` set.
- **When an IN can be split.** All of these must hold:
  - no root term after the first has `IsOr`;
  - every group on the path to the leaf has `!IsOr && !IsNot`;
  - the leaf has `!IsNot && !IsField`, no `SubConditions`, and non-string values.
  
  Then `IN(L) AND R` equals the union over chunks of `IN(Lk) AND R`.
- **Refusals must be raised before `DoCommand`.** Exceptions inside a command callback pass through `InitException` /
  `OnException`, so a check after that point does not reach the caller as-is (rule 3).
- **The existing boundary pattern** is `AsyncDataBaseBulkStore.AtomicallyAsync`: join the ambient transaction, or
  `OpenOwnedTransactionAsync` and publish it with `AmbientSqlTransaction.Enter`.

### Step A — planner
New file `Birko.Data.SQL/SQL/Connectors/InListChunkPlanner.cs`, pure, no I/O.
- `Plan(...)` returns one of: no chunking, a chunk plan, or a refusal with its reason.
- It picks the largest IN leaf that can be split and drops duplicate values.
- It never mutates the caller's tree: it copies the path from the root to the leaf, one copy per chunk, each with a
  non-empty slice of values.
- An empty IN is never chunked; it keeps its `1 = 0` rendering.
- **When it refuses:** `overhead + list > maxParams` and the list cannot be split. The reasons are OR, NOT, NOT IN,
  string values, sort/limit/offset, or aggregate. The message names the operation, the count, the maximum and the
  reason, and never the values (rule 42).
- **Speed chunking** applies only to a shape that can be split. A shape that cannot be split stays one statement while
  it is within the maximum, so nothing that works today starts refusing.

### Step B — provider limits
- `AbstractConnectorBase` gets `public virtual int MaxCommandParameters => int.MaxValue` and
  `public virtual int PreferredInListChunkSize => 1000`.
- Overrides:
  - **SQLite 32766:** Microsoft.Data.Sqlite 10.x bundles SQLite ≥ 3.32. A test pins it with
    `sqlite3_limit(handle, 9, -1)`.
  - **SQL Server 2098:** 2100 minus the 2 that `sp_executesql` takes. A live test proves 2098 binds and 2099 fails
    (rule 52).
  - **PostgreSQL 65535**, which TimescaleDB inherits.
  - **MySQL 65535:** MySqlConnector fills unprepared parameters in on the client side; grep for `Prepare()` and
    record the result.

### Step C — count the other parameters at render time
- **Overhead** is the parameter count of one probe build of the same command (IN cut to 1 value, on an unopened
  command) minus 1. This runs only when the list is longer than the chunk size.
- **Chunk size** is `min(PreferredInListChunkSize, MaxCommandParameters - overhead)`.

### Step D — wire the funnels
Rule 12 applies: the planning goes in a non-virtual public wrapper, and today's body moves to a
`protected virtual ...Core`. **Grep `C:\Source\Birko\Consumers` for overrides first.**
- **Read** streams chunk after chunk. It is chunked only with no `orderFields`, `limit` or `offset`.
- **Count** is the sum of the chunk counts.
- **Delete and update** run each chunk through the existing single-statement path, so `WouldTargetEveryRow` and
  `AddRequiredWhere` still guard each statement. All chunks share **one** boundary (rule 6):
  - with an ambient transaction, each chunk joins it;
  - otherwise open `OpenOwnedTransaction(Async)`, publish it with `AmbientSqlTransaction.Enter`, commit, and call
    `RunCommitted(Async)`; on failure roll back and call `DiscardCommitted`;
  - one producer: `RunAtomically(Async)` on the connector.
- The reported `matched` is the sum of the chunks (TASK-555).

### Step E — `AddParameter`
Deferred. Re-run `Task832InListCostTests` after Step D, and spawn a task only if render is above ~20% of a read.

### Step F — non-SQL providers
- **No chunking needed:**
  - **MongoDB:** `$in` has no element limit; the 16 MB BSON command is about 500k Guids.
  - **RavenDB:** `RavenFilterRewriter` makes it `In<T>`, one array parameter.
  - **InfluxDB, InMemory, JSON, XML:** the filter is compiled and run in memory.
- **Spawn a task for each:**
  - **Cosmos DB:** the 512 KB query-text limit, about 13k Guids, fails loudly.
  - **Elasticsearch:** `TermsQuery` (`ElasticSearch.cs:823`) is refused past `index.max_terms_count` (65,536).

### Tests
- **`tests/Birko.Data.SQL.Tests`, planner:**
  - split under AND and nested AND;
  - refuse under OR, a negated group, NOT IN, a string list, and sort/limit/offset;
  - duplicates dropped, no empty chunk, the caller's tree not mutated, the largest IN picked, the reason in the
    message.
- **`tests/Birko.Data.SQL.SqLite.Tests/Task833InListChunkingTests.cs`,** with a `LowLimitSqLiteConnector`
  (max 40, chunk 10), sync and async:
  - a read equals the single-query oracle, with duplicates and the tenant decorator present;
  - Count equals the oracle;
  - Delete and both Update overloads affect exactly the oracle rows, and `matched` is the sum;
  - a failure injected at chunk k rolls back every chunk, counted on another connection;
  - inside a `SqlUnitOfWork` that rolls back, nothing survives;
  - each chunk's SQL has a non-empty IN (captured via `OnExecute`);
  - each refusal: `NotSupportedException`, and nothing executed;
  - below the limit with a sort: one statement, which works;
  - the `sqlite3_limit` pin.
- **SQL Server live,** `InListChunkingLiveTests.cs` (`BIRKO_MSSQL_HOST`):
  - 5000 ids through read, count, delete and update;
  - 2098 binds and 2099 fails.

### Risks
- A chunked read outside a boundary is not a snapshot. That is harmless for key lists; document it.
- `Update(filter, Action)` is per-row and not atomic, which predates this task.
