---
id: TASK-895
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-873]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# String equality: leftovers after TASK-873

## Context

Found by TASK-873 (2026-11-09):
- **TASK-833's planner still refuses a string IN list past the parameter limit**, because string comparison followed
  the collation. Since TASK-873 `==` and IN are exact, so splitting would be safe. That changes TASK-833's recorded
  decision: **decide with the user.**
- **Rules (`RuleConditionConverter`) and hand-built `Condition`s keep `=` and the collation.** Should rules be exact
  too? **Decide with the user.**
- `string.Equals(x.Col, v, Ordinal)` (TASK-869's TextEqual) renders a BIN2 LIKE: no index seek, and SQL Server's 4000-
  character LIKE limit. It could go through `OrdinalTextEquality`.
- `string.Equals(x.A, x.B, Ordinal)` over two columns is refused, while `x.A == x.B` is now exact.
- View joins built from view attributes (`ViewSelectSqlBuilder.BuildViewJoinConditionSql`) render plain `=` on string
  keys and follow the collation.
- `utf8mb4_0900_bin` does not exist on MySQL 5.7 or MariaDB (string equality would fail there loudly).
- **In-memory `RuleEvaluator` string `Equal` is case-insensitive (SH-M295)** (noted by TASK-881). Making SQL rules exact
  per the decision below would make a rule answer differently in memory and in SQL unless the evaluator follows.
- PostgreSQL `CHAR(n)` (bpchar): `::text` strips padding while C# reads it padded; the framework never creates them.

## Acceptance criteria

- [x] Get the user's decisions on the two items above, and record them.
      → **Decided 2026-11-10 by the user:** (1) a string IN list past the parameter limit is split into chunks like
        other lists (reverses TASK-833's refusal for strings, now that string IN is exact); (2) rule string
        Equal / NotEqual / In / NotIn are exact like lambda filters (one answer for a rule and a lambda).
- [x] Implement the decided items and the consistency items (TextEqual through `OrdinalTextEquality`, two-column
      `Equals(Ordinal)`, view join keys), or record why not; tests red before each fix.
      → Measured 2026-11-10 live on all four providers, then fixed:
        (a) A string IN list past the limit was refused (4 of 4 shapes + the rule `In`): `InListChunkPlanner` now splits a
        list marked `Ordinal` (every parser- and rule-built list); a hand-built string `Condition`, a `char` list and NOT
        IN stay refused, as TASK-833 for other types. Chunks disjoint (ordinal de-duplication; TASK-859's IS NULL leg in
        the first chunk only); verified with case, accent, ß/ss and trailing-space twins across chunks.
        (b) Rules: SQL rule vs compiled rule, 42 cases — MySQL 30 wrong, SQL Server 36; `RuleEvaluator` 30 of 42.
        `DataBase.MarkRuleOrdinalText` calls the lambda path's `MarkOrdinalText` for Equal/NotEqual/In/NotIn on string
        columns with string values; `ComparisonHelper.AreEqual` compares two strings ordinally (string methods stay
        case-insensitive). SH-M295 was recorded as "decided" in TASK-144 without a user attribution, so the user's
        2026-11-10 decision governs.
        (c) `string.Equals(x.Col, v, Ordinal)` rewritten to `==`, so it renders through `OrdinalTextEquality` (index seek,
        no 4000-character LIKE limit); `OrdinalIgnoreCase` keeps TASK-869's path.
        (d) Two columns with `Ordinal` translate the same way (were refused); with `OrdinalIgnoreCase` still refused.
        (e) View join keys: a string join over case and space twins returned 18 extra pairs on SQL Server and 20 on MySQL
        (both builders, both read paths). Made exact through one producer `DataBase.MarkOrdinalJoin` and the dialect's
        `OrdinalTextEquality` in the DDL; the plain `=` in front keeps the key's index (100k rows: SQL Server 44→100 ms,
        MySQL 313→396 ms for a full join). The provider-less migrations generator keeps `=` (TASK-898).
        (f) MySQL 5.7 / MariaDB (no `utf8mb4_0900_bin`, fails loudly) and PostgreSQL `CHAR(n)` documented.
      → Tests: `StringEqualityLeftoversContract` (68 per provider; red SQLite/PostgreSQL/MySQL 39, SQL Server 47),
        `Task895StringEqualityLeftoversTests` (red 8/12), `Task895RuleEvaluatorStringEqualityTests` (red 28/41),
        `Task895ViewJoinKeyTests` + a split test in `Task837` (red 13/37), SQLite `Task833` split test (red 1/29); all
        green. TASK-833/837's refusal pins inverted (rule 56).
      → Live: 99 importers run; SQLite 2348, PostgreSQL 1768, MySQL 1790, SQL Server 1824 after fixing one stale refusal
        pin; SQL.Tests 1124, Rules 515. Consumers: Symbio's IoT rules use decimals (unchanged); `SlugHistoryService`'s
        `oldPaths.Contains` would now split instead of refusing on SQL Server.

## Out of scope

- `==` / IN in lambda filters (TASK-873, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
