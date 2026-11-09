---
id: TASK-866
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Condition.IsOr` means two things in a flat condition list

## Context

Noticed by TASK-861 (2026-11-08) while writing a test; not shown to produce wrong rows yet.
- In a flat condition list, `Condition.IsOr` means "this term joins the previous one with OR".
- On a group, it means "this group's children are OR'd".
- A group in a flat list therefore carries both meanings on one flag. A hand-built condition, or a future parser
  path, can set it for one meaning and get the other. Rule 43 is the same family.

## Acceptance criteria

- [x] Find every producer and reader of `IsOr` on a group in a list (parser, `RuleConditionConverter`, the chunk
      planner, `ConstantTruth`, the renderers). Record whether any shape the parser or the public API can produce
      renders a different meaning than intended. Reproduce before changing anything (rule 54).
      → Inventory 2026-11-09: the parser's binary `&&`/`||` branch, comparison leaves, the `IsNullOrEmpty` and `.Date`
        groups, `BuildColumnComparison`, `ApplyCSharpNullSemantics` / `WithNullLegs`, `RuleConditionConverter`
        (`AndSubCondition` / `OrSubCondition`, `AsAndTerm`), `InListChunkPlanner`, view resolution (`Copy`) and the
        renderer (`ConditionDefinition`, `AppendSubConditionsTo`, `ConstantTruth`, `IsAlwaysTrueChain`) agree with each
        other. **One producer did not: `DataBase.ReturnSingleSubCondition`** (since ad13f68b, 2026-03-28).
      → **Reachable, silent, on all four providers (measured live):** when `&&`/`||` has a constant operand next to a
        group, the collapse wrote the enclosing operator's flag over the surviving group's own separator, so an OR group
        rendered as AND and the reverse. 7 of 9 shapes wrong on reads and writes. `!((x.A == 10 || x.A == 20) && t)`
        rendered `NOT (A = 10 AND A = 20)`, and its Delete removed every row with no `WholeTableWriteException`
        (the clause depends on the row, so the guard cannot see it). Reads, counts, views, Delete and all three Update
        forms run through it.
- [x] If one is reachable: separate the two meanings (for example a join flag and a children-combine flag), with
      tests red before the fix. If none is: record that and cancel, or add a guard test that pins the invariant.
      → Fixed at the producer, not by splitting the flag: `parent.IsOr = surviving.IsOr` (rule 43: the collapsed
        operator joined two operands, one of which is gone, so it combines nothing; the parent brings no separator of its
        own). The unused parameter is gone. The flag split was not needed for the parser path; the public
        `Condition.Or*` API can still put a group at index > 0 with both meanings, and no in-repo or consumer caller
        does (grep).
      → Tests: `CollapsedGroupSeparatorTests` (SQL.Tests, 7), shared `CollapsedGroupContract` (16 shapes incl. nested,
        nullable with NULL legs, value fragments, column vs column; read, count and six writes vs `Compile()`; 112 cases
        per provider), `Task866ViewCollapsedGroupTests` (16, on-the-fly and persistent, four providers).
      → Red with HEAD's line, in a scratch copy: separator tests 7/7, contract 98/112 per provider (100 on SQL Server),
        views 16/16. Green with the fix.
      → Live, `BIRKO_REQUIRE_LIVE=1`: SQL.Tests 884, Views 338, SQLite 2082, SQL Server 1557 (run alone), MySQL 1522 of
        1523 (the known TASK295 flake), PostgreSQL green apart from TASK-864's in-progress contract and the known
        `With_retry_off`.
      → Consumers: no filter with this shape found (grep, not exhaustive).

## Out of scope

- The constant reduction itself (TASK-861, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
