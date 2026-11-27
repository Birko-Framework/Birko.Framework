---
id: TASK-1006
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Verify first: run `StoreFactory.Create("SQLite", "Data Source=:memory:", p => p.AddSqLite())` on Windows and
  Linux and record what it does. If it already works, cancel this task with the measurement.
- [ ] Otherwise decide with the user between:
  - supporting it, with one connector holding the connection open, since an in-memory SQLite database lives only as
    long as its connection;
  - refusing it with a clear `ArgumentException`.
- [ ] The decision also covers the `Mode=Memory` and `file::memory:` forms.
- [ ] A test pins the chosen behaviour.

## Human test plan

N/A — fully covered by automated tests.
