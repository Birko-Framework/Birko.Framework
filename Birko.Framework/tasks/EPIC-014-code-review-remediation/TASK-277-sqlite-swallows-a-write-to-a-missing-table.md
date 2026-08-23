---
id: TASK-277
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-23
depends-on: []
blocks: []
related: [TASK-211, TASK-244]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL.SqLite]
---

# On SQLite a write to a missing table reports SUCCESS — `OnException` swallows it and `DoInit` does nothing

## Context — measured while closing TASK-244, and it is the half that turns a lost row into a 200

`SqLiteConnector`'s exception handler (`Database/Connectors/SqLiteConnector.cs:43-58`):

```csharp
private void SqLiteConnector_OnException(Exception ex, string? commandText)
{
    if (!IsInitializing && ex is SqliteException sqliteEx
        && sqliteEx.SqliteErrorCode == 1
        && sqliteEx.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
    {
        DoInit();          // <- and then RETURNS: the failed statement is discarded
    }
    else
    {
        throw new Exception(commandText, ex);
    }
}
```

Two things make this worse than it reads:

- **`DoInit()` does not create the table.** It raises `AbstractConnector.OnInit`, and **nothing in the
  framework subscribes to that event** — only a consumer can, via `IDataBaseRepository.AddOnInit`. So on a
  default setup `DoInit()` is a no-op.
- **The statement is not retried and not reported.** The caller gets a normal return. For a write that means
  the row is silently gone.

**Measured 2026-08-23** (`Birko.Data.SQL.SqLite.Tests.SchemaEnsureRollbackResidueTests`
`.A_write_to_a_missing_table_reports_success_on_sqlite_TASK277`, which asserts the current behaviour so it
cannot be believed fixed): with the table dropped underneath an initialised store, `CreateAsync` returns a
non-empty `Guid`, `sqlite_master` still has no such table, and no exception is raised.

**Why it matters beyond its own oddity.** It is the second half of the chain that cost consumer Symbio a
test environment (its TASK-527, and TASK-244 here). TASK-244 fixed the first half — a store no longer
remembers a schema-ensure that a rollback undid — so the specific chain is broken. This half is still live
for **every** other way a table can be absent: a hand-dropped table, a restored database missing a table, a
migration that did not run, a consumer pointing at the wrong file. In all of those, a write reports success.

⚠ **This is TASK-211's family, and that task explicitly did not finish it.** TASK-211 narrowed the
missing-table *reader* swallow and recorded that `OnException` "is the same decision wearing different
clothes" — it fixed PostgreSQL's and MySQL's handlers, which were running `DoInit()` and returning on a
substring match. SQLite's was not touched. So the rule already exists; this is a provider that never got it.

## Questions to settle

- **Should a missing table on a WRITE ever be swallowed?** The legitimate case the swallow exists for is
  lazy create-on-first-use — but that is `EnsureInitialized`'s job now, and it runs *before* every CRUD
  operation. A missing table at write time therefore means the schema is wrong, not that it is late.
- **If `DoInit()` is kept, should it actually ensure the schema?** An `OnInit` event nothing subscribes to
  is indistinguishable from a no-op; if the intent was "re-create and retry", neither half happens.
- **Does the read side need the same narrowing?** TASK-211's swallow makes a read of a missing table return
  empty. That is a wrong answer too, but it has shipped callers (view-existence probing, CR-M149) — so it
  needs its own decision rather than being assumed.

## Acceptance criteria

- [ ] A write (`INSERT`/`UPDATE`/`DELETE`) against a missing table on SQLite **fails loudly**. The
      `TASK277`-suffixed test in `SchemaEnsureRollbackResidueTests` is inverted rather than deleted — it was
      written to assert the defect, and the pair of before/after assertions is the record.
- [ ] The decision recorded for the READ side too, whichever way it goes: narrowed like the write side, or
      kept with the reason and the callers named (TASK-211 kept it for view-existence probing).
- [ ] `DoInit()`'s role settled — either it genuinely ensures the schema, or the call is removed from the
      handler so it stops implying a recovery that does not happen.
- [ ] Blast radius measured before making it throw: how many suites and consumer paths currently rely on a
      swallowed missing table. TASK-211's narrowing broke two suites that asserted the wide behaviour, and
      those were the interesting ones.
- [ ] Mutation-proven: restore the swallow and the new test goes red.

## Out of scope

- The residue that made this reachable in Symbio's chain — TASK-244 owns it and is done.
- PostgreSQL's and MySQL's handlers — TASK-211 already narrowed them; re-measure rather than re-fix.
- `IsMissingTableException` on the reader path, unless the read-side question above decides to move it.

## Human test plan

- [ ] N/A — automated; SQLite needs no server.

## Implementation plan

_Populated by `/tasks plan TASK-277` — leave empty until then._
