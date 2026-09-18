---
id: TASK-302
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-276]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL test suites have leaked ~90,000 temp directories, and every teardown swallows the failure

## Context — measured while working [[TASK-276]]

Counting `%TEMP%\birko-*` on the development machine, 2026-09-08:

```
89,775 directories
```

By prefix, the largest offenders:

| prefix | dirs |
|---|---|
| `birko-sql-parity` | 19,055 |
| `birko-sql-norm` | 9,512 |
| `birko-emptynotin` | 6,783 |
| `birko-orderby` | 5,993 |
| `birko-destructive` | 5,573 |
| `birko-sql-enumin` | 4,721 |
| `birko-tx` | 4,606 |

Sampling 400 of them: **164 still hold files**, the rest are empty directories whose contents were
deleted but which were never removed themselves.

## Why nobody has noticed

Every teardown in these suites has the same shape:

```csharp
try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
```

The `catch { }` is deliberate — a teardown must not fail a green test — but it means the delete failing
is **completely invisible**. The suites have been leaking for as long as they have existed and no signal
was ever produced.

Two distinct failure modes are visible in the data and they want different fixes:

- **Empty directory left behind** — the files went, the directory did not. On Windows a recursive delete
  can fail to remove the directory if a file deletion has not yet been observed by the filesystem; a
  short retry usually succeeds.
- **Directory still holding files** — something still had the file open at teardown. TASK-276's
  `SqlitePool` helper addresses the SQLite-pool case precisely; this ticket is about whatever remains.

## What to do

1. **Measure per prefix which of the two modes dominates**, rather than assuming. The two need different
   remedies and the mix decides the effort.
2. **A shared teardown helper** that retries the delete a few times with a short backoff, and — the part
   that matters — **records** a failure somewhere a human can see rather than swallowing it. A count
   written to test output is enough; the current state is not that the cleanup is unreliable, it is that
   nobody can tell.
3. **Clean up the existing 90k** as a one-off, and say so in the task rather than leaving it to whoever
   next notices their disk.

## Acceptance criteria

- [ ] The two failure modes measured per prefix, with numbers.
- [ ] A shared teardown helper used by the SQL suites, with a retry and a **visible** record of failure.
- [ ] A run of the full SQL suites leaves no new leaked directory — measured by counting before and after,
      not by inspection.
- [ ] Proven able to fail: a teardown whose directory genuinely cannot be removed must produce the record,
      and there must be a test for that.

## Out of scope

- The SQLite connection-pool half — [[TASK-276]] owns it and its `SqlitePool` helper already clears the
  fixture's own pools precisely.
- Making teardown failures fail the test. A flaky teardown turning a green suite red is worse than the
  leak; the point is a signal, not a gate.

## Human test plan

- [ ] Count `%TEMP%\birko-*` before and after a full SQL suite run and confirm the delta is zero.
