---
id: TASK-1006
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-988, TASK-1001]
findings: []
pr: null
github-issue: null
jira-key: null
---

# SqLiteSqlStoreFactory.FromConnectionString turns :memory: into a file path

## Context

Found at the 2026-11-27 `/specs regen` of `backend-store-implementations`; the user asked for it to be filed. This
comes from reading the code; it has not been run.

`SqLiteSqlStoreFactory.FromConnectionString` (`Birko.Data.SQL.SqLite/Stores/SqLiteSqlStoreFactory.cs:40-53`,
[[TASK-988]]) takes the data source and makes it absolute with `Path.GetFullPath(path)`, then splits the result into
directory and file name.

For `Data Source=:memory:` (or a bare `:memory:`) that gives `<current directory>\:memory:`. One of two things follows:
- the call throws on Windows, where a colon is not allowed in a file name;
- SQLite opens a file of that name instead of an in-memory database.

The factory also creates the directory ([[TASK-1001]]), so a file-backed database may appear in the working directory.

## Acceptance criteria

- [x] Verify first: run `StoreFactory.Create("SQLite", "Data Source=:memory:", p => p.AddSqLite())` on Windows and
  Linux and record what it does. If it already works, cancel this task with the measurement. (Windows only, measured;
  Linux not run, and moot after the decision, as the factory now refuses before any path is made.)
- [x] Otherwise decide with the user between:
  - supporting it, with one connector holding the connection open, since an in-memory SQLite database lives only as
    long as its connection;
  - refusing it with a clear `ArgumentException`.
- [x] The decision also covers the `Mode=Memory` and `file::memory:` forms.
- [x] A test pins the chosen behaviour.

## Evidence 2026-11-27

**Verified first**, on Windows, with a temporary probe run from an empty working directory:

| Data source | Factory | First operation |
|---|---|---|
| `:memory:`, `Data Source=:memory:`, `Data Source=file::memory:` | accepted | `SqliteException`: "SQLite Error 14: unable to open database file" |
| `Mode=Memory` | refused at once (`RequireOnlyKeys`: the settings cannot carry it) | n/a |

No file was written on Windows. The path became `<working directory>\:memory:`, which Windows does not allow. On
Linux `:memory:` is a legal file name, so it would most likely become a database file of that name. That was not
run here.

**Decision (the user, 2026-11-27): refuse it clearly.** `FromConnectionString` throws `ArgumentException` for any
in-memory data source (`:memory:` in any case, or a `file::memory:` URI with or without a query), naming the InMemory
backend and a file path as the alternatives. An in-memory SQLite database lives as long as one connection, and the
connector opens one per operation.

**Test:** `SqlStoreFactoryTests.SQLite_refuses_an_in_memory_data_source`, 7 cases: red 7/7, then green 7/7.

**Regressions:** SQL.Providers 65, StoreFactory 26, EventSourcing.SQL 37 and SQL.SqLite 3194 pass.

**Docs:** the SQLite project's `CLAUDE.md` and `README.md` both advertised `Data Source=:memory:` as a connection
string. Both now say it is not supported, and why.

**Not covered:** a `SqLiteSettings` built directly with a `:memory:` file name. It still fails on the first operation, as
before. The factory's connection string is the path the filing named.

## Human test plan

N/A — fully covered by automated tests.
