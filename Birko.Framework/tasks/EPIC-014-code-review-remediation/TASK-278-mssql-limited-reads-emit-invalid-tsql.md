---
id: TASK-278
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-23
depends-on: []
blocks: []
related: [TASK-257, TASK-277]
findings: []
pr: ffd8bac (Birko.Data.SQL) · f564461 (.MSSql)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MSSql]
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

- [x] The decision recorded here, with the measurement above as its basis.
- [x] `ReadFirstAsync(filter)` works on live SQL Server — the case § Conventions actively recommends.
- [x] A limited read with no offset, and a paged read with an offset, both work on live SQL Server, with and
      without a caller-supplied sort.
- [x] The other three providers are **unchanged** — assert the emitted SQL for each, since this touches a
      shared read path. SQLite/PostgreSQL/MySQL accept `LIMIT`/`OFFSET` without a sort today.
- [x] Mutation-proven: revert the emitter and the MSSql tests go red — 3 of 7, and the same 3 for the
      synthesised sort.
- [x] ⚠ **Grep the suites for `Should().NotBeNull()` on a bulk-store read.** That shape is vacuous — the
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

---

## Closed 2026-08-23

**Fixed in two places, because the fact needed is known in only one of them.**

- `MSSqlConnector.LimitOffsetDefinition` always emits the `OFFSET`, defaulting to **0** — T-SQL has no
  standalone `FETCH`, so the clause cannot be omitted, only defaulted.
- `AbstractConnectorBase.RequiresOrderByForPaging` (false; **true on MSSql alone**) makes
  `CreateSelectCommand` append `ORDER BY (SELECT NULL)` when there is a limit and the caller passed no sort.

### The questions, answered

- *`TOP (n)` or a synthesised `ORDER BY`?* **The sort.** `TOP` needs no sort but lives in the SELECT list
  rather than in this tail, so it would be a second insertion point — and an offset still forces the
  `OFFSET`/`FETCH` form and therefore the sort anyway. One mechanism for both shapes beats two split by
  argument.
- *Is an unsorted limited read even meaningful?* It returns arbitrary rows on **every** provider; SQL Server
  merely refuses to pretend otherwise. Synthesising the sort therefore makes the four agree rather than
  making one special. Requiring a caller-supplied sort was rejected: it would change behaviour on the three
  providers where it works today, for no correctness gain.
- *Does `LimitOffsetDefinition` know whether an `ORDER BY` was emitted?* No — and it must not be told by
  adding a parameter. It is `public virtual`, so a new signature would leave any existing override
  **silently no longer overriding anything**. The composer knows, so the composer decides.

### Verification

**1,389 tests, 0 failed, 0 skipped** across twelve suites with `BIRKO_REQUIRE_LIVE` set throughout (live
SQL Server 2022, PostgreSQL 16.15, MySQL 8.4.11, on-disk SQLite) — **16 new**, in four places:
`LimitOffsetLiveTests` (MSSql, 7 — including `ReadFirstAsync`, which could not run there at all),
`PagedReadEndToEndTests` (SQLite, 5 — the base path end to end), `LimitOffsetEmissionTests`
(`Birko.Data.SQL`, 3 — the base emission is byte-identical) and `LimitOffsetCapabilityTests` (PostgreSQL and
MySQL, 1 each — the capability's false side).

**⚠ There was no paging coverage in any suite before this.** Not thin — none, on any provider. That is the
whole explanation for how a documented API stayed unusable on a whole provider, and it is why this task adds
tests on SQLite as well as on the provider that was broken.

**Mutations, disjoint by provider:**

| Mutation | MSSql live | SQLite paging | base pins |
|---|---|---|---|
| omit the `OFFSET` when the caller gave none | **3 red** | green | green |
| remove the synthesised `ORDER BY` | **3 red** | green | green |
| `RequiresOrderByForPaging` unconditionally true | green | **1 red** | **1 red** |

The third row is the one worth keeping: an over-broad fix is invisible to the provider it was written for,
and only the false-side assertions catch it.

### Deliberately not done

- **No `TOP (n)` path**, for the reason above.
- **No requirement that a limited read carry a sort** — it would change three working providers.
- **No sweep of every `Should().NotBeNull()` in the suites.** The four instances this task's own family
  introduced were fixed under TASK-277; a tree-wide audit of the shape is left unfiled, because the
  measurement that would justify it is a suite-quality question rather than a defect.
