---
id: TASK-278
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-23
depends-on: []
blocks: []
related: [TASK-257, TASK-277]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL.MSSql]
---

# On SQL Server every limited read emits invalid T-SQL — `ReadFirstAsync` and paging both fail

## Context — found while closing TASK-277, by strengthening an assertion that had been vacuous

`MSSqlConnector.LimitOffsetDefinition` (`Database/Connector/MSSqlConnector.cs:325-339`) emits
`FETCH NEXT @LIMIT ROWS ONLY` and prepends `OFFSET @OFFSET ROWS` **only when an offset was supplied**.
T-SQL requires more than that. **Measured 2026-08-23 on live SQL Server 2022 (16.0.4265.3):**

| Statement | Result |
|---|---|
| `SELECT Id FROM T FETCH NEXT 1 ROWS ONLY` | **Msg 153** — Invalid usage of the option NEXT in the FETCH statement |
| `SELECT Id FROM T OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` | **Msg 102** — Incorrect syntax near '0' (no `ORDER BY`) |
| `SELECT Id FROM T ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` | OK |
| `SELECT Id FROM T ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` | OK |
| `SELECT TOP (1) Id FROM T` | OK — no `ORDER BY` needed |
| `SELECT Id FROM T OFFSET 1 ROWS` | **Msg 102** |

So there are **two** defects, not one:

1. **A limit with no offset is always invalid** (Msg 153). That is the common case: `ReadFirstAsync` —
   which `Birko.Data.SQL/CLAUDE.md` § Conventions tells consumers to use for a single result — goes through
   `ReadCoreAsync`'s `limit: 1` and therefore **cannot work at all on SQL Server**.
2. **Offset+limit is invalid without an `ORDER BY`** (Msg 102). So paging works only when the caller also
   supplies a sort; an unsorted page fails.

**How it surfaced, which is the part worth keeping.** TASK-244's new test asserted
`read.Should().NotBeNull()` after a write — and on a **bulk** store the bulk `Read(filter)` overload hides
the single-result one and returns the *collection* (§ Conventions), so that assertion passed on an empty
enumerable and proved nothing. Rewriting it to assert the row exposed this immediately on MSSql. The
vacuous assertion had been green in the committed suite.

## Questions to settle

- **`TOP (n)` or a synthesised `ORDER BY`?** `TOP (n)` is valid with no sort and is the natural fix for the
  no-offset case, but it cannot express an offset. `ORDER BY (SELECT NULL)` unlocks both forms uniformly.
  A hybrid (`TOP` when there is no offset, synthesised sort when there is) is two code paths for one
  feature.
- **Is an unsorted limited read even meaningful?** Without `ORDER BY` the rows returned are arbitrary on any
  provider; SQL Server merely refuses to pretend otherwise. So a third option is to **require** a sort
  whenever a limit or offset is given, and throw a clear framework error rather than emit SQL that fails —
  but that changes behaviour on the three providers where it currently works.
- **Does `LimitOffsetDefinition` know whether an `ORDER BY` was emitted?** If not, the synthesised-sort fix
  needs that information threaded to it, which is the real cost of the uniform option.

## Acceptance criteria

- [ ] The decision recorded here, with the measurement above as its basis.
- [ ] `ReadFirstAsync(filter)` works on live SQL Server — the case § Conventions actively recommends.
- [ ] A limited read with no offset, and a paged read with an offset, both work on live SQL Server, with and
      without a caller-supplied sort.
- [ ] The other three providers are **unchanged** — assert the emitted SQL for each, since this touches a
      shared read path. SQLite/PostgreSQL/MySQL accept `LIMIT`/`OFFSET` without a sort today.
- [ ] Mutation-proven: revert the emitter and the MSSql tests go red.
- [ ] ⚠ **Grep the suites for `Should().NotBeNull()` on a bulk-store read.** That shape is vacuous — the
      bulk overload returns a collection — and it is what hid this defect. Any other instance is hiding
      something too.

## Out of scope

- The missing-table swallow — TASK-277, done.
- `ReadFirstAsync`'s existence or naming; only its emitted SQL is in question here.
- Whether an unsorted paged read should be an error on every provider — worth asking (see the questions),
  but changing the three working providers is a separate, wider decision.

## Human test plan

- [ ] N/A — live-server behavioural tests plus emitted-SQL assertions.

## Implementation plan

_Populated by `/tasks plan TASK-278` — leave empty until then._
