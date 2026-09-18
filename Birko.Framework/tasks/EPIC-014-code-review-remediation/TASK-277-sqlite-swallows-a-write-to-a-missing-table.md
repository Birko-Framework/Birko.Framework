---
id: TASK-277
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-23
depends-on: []
blocks: []
related: [TASK-211, TASK-244]
findings: []
pr: 78df775 (Birko.Data.SQL) · c407ea3 (.SqLite) · 2838e81 (.MySQL) · 4cf7550 (.PostgreSQL) · ceb2344 (.MSSql)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.SqLite, Birko.Data.SQL.MySQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MSSql]
---

# A write to a missing table reports SUCCESS on EVERY provider — `OnException` swallows it and `DoInit` does nothing

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
`.A_write_to_a_missing_table_reports_success_on_sqlite_TASK277` — inverted at the close into
`.A_write_to_a_missing_table_now_fails_instead_of_reporting_success`): with the table dropped underneath an initialised store, `CreateAsync` returns a
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

- [x] A write (`INSERT`/`UPDATE`/`DELETE`) against a missing table on SQLite **fails loudly** — and on the
      other three providers, which turned out to have the same swallow. The
      `TASK277`-suffixed test in `SchemaEnsureRollbackResidueTests` is inverted rather than deleted — it was
      written to assert the defect, and the pair of before/after assertions is the record.
- [x] The decision recorded for the READ side too, whichever way it goes: narrowed like the write side, or
      kept with the reason and the callers named (TASK-211 kept it for view-existence probing).
- [x] `DoInit()`'s role settled — either it genuinely ensures the schema, or the call is removed from the
      handler so it stops implying a recovery that does not happen.
- [x] Blast radius measured before making it throw: how many suites and consumer paths currently rely on a
      swallowed missing table. TASK-211's narrowing broke two suites that asserted the wide behaviour, and
      those were the interesting ones.
- [x] Mutation-proven: restore the swallow and the new test goes red — one test in each of the four suites.

## Out of scope

- The residue that made this reachable in Symbio's chain — TASK-244 owns it and is done.
- PostgreSQL's and MySQL's handlers — TASK-211 already narrowed them; re-measure rather than re-fix.
- `IsMissingTableException` on the reader path, unless the read-side question above decides to move it.

## Human test plan

- [ ] N/A — automated; SQLite needs no server.

## Implementation plan

_Populated by `/tasks plan TASK-277` — leave empty until then._

---

## Closed 2026-08-23

**A write that cannot be applied now throws, on all four providers.** One producer,
`AbstractConnector.EnsureSchemaAndReport`, called by each provider's `OnException` handler: ensure the schema
if the failure looks like a missing table, then always report it.

### The scope was wider than this file first said

This task was filed from a SQLite measurement and said SQLite's handler was the one TASK-211 had not reached.
That is true of the **narrowing** — but the **swallow itself was on all four providers**, so this was
framework-wide silent data loss rather than a SQLite quirk. Corrected in the title and the `affects:` list
rather than left as filed.

### Answers to the questions

- *Should a missing table on a WRITE ever be swallowed?* **No.** `EnsureInitialized` runs before every CRUD
  operation, so a missing table at write time means the schema is wrong, not late.
- *If `DoInit()` is kept, should it actually ensure the schema?* It is kept **and** it still only raises
  `OnInit` — which is honest now that the failure is reported: a consumer with a registered handler gets its
  schema ensured for the *next* attempt, and nobody is told this attempt worked. The branch could never have
  repaired anything on its own, because nothing in the framework subscribes to that event and the statement
  is not retried.
- *Does the read side need the same narrowing?* **It is a different decision and it is untouched**, because
  the read path never reaches this handler: `RunReaderCommandOn` catches `IsMissingTableException` itself and
  yields break. TASK-211 owns that answer and keeps its stated callers. Both sides are now pinned by tests,
  so the asymmetry reads as a choice.

### Bonus fix in the same handler

MSSql was still classifying by raw message substring (`"Invalid object name"`, case-sensitively) — the shape
TASK-211 removed from PostgreSQL and MySQL and never reached here. All four now use
`IsMissingTableException`, which for MSSql also tests error **208**, so it is narrower *and* case-correct.

### Verification

**1,384 tests, 0 failed, 0 skipped** across twelve suites with `BIRKO_REQUIRE_LIVE` set throughout (live
PostgreSQL 16.15, SQL Server 2022, MySQL 8.4.11, on-disk SQLite) — 5 new. **Blast radius measured before
shipping: exactly 1 test broke**, the defect-pin written under TASK-244, which was **inverted rather than
replaced**. Compare TASK-211, whose narrowing broke two suites that asserted the wide behaviour: a removal
that breaks nothing is a swallow nothing relied on.

**Mutation:** restoring `DoInit(); return;` fails exactly one test in each of the four provider suites.

### ⚠ A vacuous assertion was hiding a second defect

TASK-244's tests asserted `read.Should().NotBeNull()` after a write. On a **bulk** store the bulk
`Read(filter)` overload hides the single-result one and returns the **collection** (§ Conventions), so that
passes on an empty enumerable and proves nothing — it was green in the committed suite. Rewriting it to
assert the row failed immediately on MSSql:

| Statement | Result on SQL Server 2022 |
|---|---|
| `FETCH NEXT 1 ROWS ONLY` (no OFFSET, no ORDER BY) | **Msg 153** |
| `OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` (no ORDER BY) | **Msg 102** |
| `ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` | OK |
| `SELECT TOP (1) …` | OK |

So `ReadFirstAsync` — the call `Birko.Data.SQL/CLAUDE.md` recommends for a single row — **cannot work on SQL
Server at all**, and an unsorted paged read fails too. Filed as [[TASK-278]] (P1) with the table above and
the fix shape; these tests read the collection instead, so they are portable today.

### Deliberately not done

- **The read-side swallow** — TASK-211's decision, its callers named there, and now pinned by a test on this
  side too.
- **TASK-278 itself** — a different subsystem (limit/offset rendering) found through this task's tests.
- **No audit of every `Should().NotBeNull()` in the suites.** It is recorded as a criterion on TASK-278
  instead, since that is where the shape's cost was measured.
