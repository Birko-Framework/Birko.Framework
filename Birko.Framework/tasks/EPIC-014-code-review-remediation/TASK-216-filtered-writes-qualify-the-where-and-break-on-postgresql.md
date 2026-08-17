---
id: TASK-216
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-211, TASK-209, TASK-110, TASK-217]
pr: 7b60044
github-issue: null
jira-key: null
findings: []
---

# A filtered DELETE / UPDATE qualifies its `WHERE` with a bare table name, so every filtered write fails on PostgreSQL

## Context

Measured on real PostgreSQL 16.4 (2026-08-15) while closing [[TASK-211]], which fixed the identical
mechanism on the **read** path. Spawned rather than absorbed: it is a different builder, it needs a
different fix, and its failure mode is the opposite one.

`DataBase.ResolveColumnName(exprType, property, withTableName: true)` qualifies every condition name, so a
filtered write emits the qualifier bare while the statement's target table is quoted:

```sql
DELETE FROM "OfPersons" WHERE OfPersons.Name = $1
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 31
```

On PostgreSQL — the one supported provider that case-folds an unquoted identifier — `OfPersons` folds to
`ofpersons` and does not match the quoted relation. Every `Delete(filter)` / `Update(filter, …)` on an
entity whose table name is not already all-lower-case fails.

**Unlike the read defect, this one is LOUD.** `missing FROM-clause entry for table` does not contain the
missing-relation wording, so `PostgreSQLConnector_OnException` rethrows rather than swallowing (and after
TASK-211's narrowing it rethrows for an even wider set). So this is a hard failure a consumer would notice,
not the silent-empty-result class TASK-211 was filed for. That is the whole reason it was left out: the
severity is genuinely lower, and the fix is not the same one.

**TASK-211's fix does not reach here, and its mechanism does not port.** That task emits the FROM/JOIN table
as `"T" AS T` — a quoted relation with a bare alias, so every bare qualifier folds onto the alias. The same
trick on a write is not portable:

| Provider | `DELETE FROM t AS a` |
|---|---|
| PostgreSQL | supported |
| SQLite | supported |
| MySQL | only since 8.0.16 |
| **MSSql** | **not supported** — requires `DELETE a FROM t AS a` |

So the write path needs its own decision, which is what this task owns.

## Approach

Two candidates; neither is obviously right, so cost both before choosing.

1. **Don't qualify a single-table statement.** A write always targets exactly one table, so the qualifier is
   never needed for disambiguation. `DataBase_OrderBy` already does exactly this
   (`withTableName = tableList.Length > 1`), so the precedent is in the codebase. Cheapest and most
   portable, but the condition parser is shared with reads and does not know which statement it is feeding —
   changing it globally would strip the qualifier from multi-table reads, which need it.
2. **Quote the qualifier at render time** in the write builders, where the target table name is known. Fixes
   it without touching the parser, and matches what `ViewSelectSqlBuilder.QuoteFieldReference` does for the
   DDL path. Note the trap TASK-211 recorded: a qualifier can arrive function-wrapped (`LOWER(T.Col)`,
   `COALESCE(…)`, the `.Date` rewrite), so a naive split-on-first-dot silently misses those — and a missed
   producer is the same failure, which is how this family keeps surviving.

Whichever is chosen, **verify against real PostgreSQL** — the recipe is in TASK-211's § Measured (EDB
portable binaries, no Docker, no admin).

## Acceptance criteria

- [x] Reproduced on real PostgreSQL first, and the recipe recorded
- [x] A filtered `Delete(filter)` and a filtered `Update(filter, …)` over a PascalCase model both work on
      PostgreSQL, including a filter whose column reference is function-wrapped (`LOWER`, `COALESCE`, `.Date`)
      — that shape is the one a partial fix misses
- [x] The chosen mechanism is recorded in `CLAUDE.md` § Conventions next to TASK-211's alias rule, and says
      why the write path does not use the alias
- [x] `SelectCount` and the aggregate path are checked on PostgreSQL too — they share the condition
      renderer, and this task should not leave a fourth sink for someone else to rediscover
- [x] Red-verified; split as numbers, contract pins named as pins. **A SQLite-only assertion is a pin by
      construction** (SQLite is case-insensitive for identifiers), as TASK-209 recorded
- [x] Full SQL suite sweep (TASK-211 swept 23 suites; the same set applies) — **23 suites green**

## Out of scope

- The read path — [[TASK-211]] closed it and this task must not regress it.
- A permanent PostgreSQL CI tier (STORY-042's Docker tier). This needs one reproduction, not a suite.

## Outcome

**What was wrong.** Every filtered `Delete`/`Update` on a PascalCase entity failed on PostgreSQL: the
condition names are qualified while the target table is quoted, so the bare qualifier folded and matched
nothing. Reproduced on 16.4 with all four shapes failing in the first run — plain DELETE, plain UPDATE,
`LOWER(T.Col)`, and the `.Date` rewrite's `(T.Seen >= @a AND T.Seen < @b)`.

**The fix strips the qualifier, and § Approach's two options were both rejected on the same ground.** A
write targets exactly one table, so the qualifier carries no information and a bare column cannot be
ambiguous. Quoting it (option 2) would have made the write path the only place a *qualifier* is quoted while
reads resolve theirs against a bare alias — two conventions for one thing. Option 1's end state was right
but reached in the shared parser, which multi-table reads need. Stripping in `AddRequiredWhere` keeps one
framework-wide invariant: **a qualifier is only ever emitted where a bare alias introduces it.**

**`AddRequiredWhere` has exactly four callers and they are all writes**, and it already received the target
`tableName` — so the fix is four lines with no new plumbing and no reachable path to the read side. Worth
checking for that shape before designing plumbing.

**Split.** Live PostgreSQL 16.4: **4 of 22** fail on revert. Offline: **3 of 500** (2 new strip pins + the
one existing assertion that named the old spelling). Green after restore: 22/22 live, 23 SQL suites offline.

**Contract pins, named as pins.** `A_filtered_count_returns_the_right_number` and
`An_unbounded_delete_is_still_refused` pass either way, as do three of the five offline tests — the
longer-table-name guard, the reads-keep-their-qualifiers regression guard, and the empty-clause refusal.
Those three guard *this fix* rather than the defect, which is their point. And every offline assertion here
is a shape pin by construction: SQLite is case-insensitive for identifiers.

**Judgement calls.**

- **Criterion 4 was answered by measurement, not by reasoning.** `SelectCount` goes through
  `CreateSelectCommand`, so TASK-211's alias already covered it — the probe was written anyway, and passing
  on the first run is the answer the criterion asked for rather than a fourth sink left to rediscover.
- **The regression suite found an unrelated defect and it was filed, not asserted.** The obvious
  `Update(Table, values, conditions)` overload builds its SET list from **every** column while binding only
  the caller's subset. Measured on both providers before filing ([[TASK-217]]): loud on each, row unchanged
  on SQLite — the initial guess (unbound binds NULL, silently blanking columns) was wrong and would have
  justified a much larger fix. The test moved to the fields/values shape the store layer uses, so this
  task's evidence measures one thing.
- **One existing assertion named the replaced spelling and was updated, not weakened.** TASK-137's
  `ARealTermBesideAnAlwaysTrueTerm_IsNotRefused` asserted `WHERE Widgets.Count > `; its subject is that a
  real term survives the always-true reduction, which is independent of the column's spelling.

## Human test plan

N/A — a filtered write either removes/changes the right rows on a live PostgreSQL or it does not, which an
automated test observes directly.

## Implementation plan

Drafted 2026-08-15 at `/tasks pick`, after reading the two write builders. **Provisional until the
reproduction runs** — TASK-211's premise was wrong by two orders of magnitude and only measurement showed it.

**The funnel is already there, and it is exactly the right one.** `AddRequiredWhere(conditions, command,
operation, tableName, allowAllRows)` has **four callers and they are all writes** — sync and async
`Delete` and `Update`. It already receives the target `tableName`, and reads use `AddWhere` instead. So the
whole fix fits in one method with no new plumbing and no risk to the read path.

**A third option, preferred over both in § Approach: STRIP the target table's qualifier.** A write targets
exactly one table, so a qualifier carries no information there and a bare column is unambiguous.

- vs. § Approach option 1 (don't qualify at parse time): same end result, but achieved where the statement
  is known instead of in the shared parser, so multi-table reads keep the qualifier they need.
- vs. § Approach option 2 (quote the qualifier): quoting would make the *write* path the one place a
  qualifier is quoted, while reads resolve theirs against a **bare alias** (TASK-211). Two conventions for
  one thing in one codebase is the "two producers" smell this family keeps being bitten by. Stripping keeps
  a single invariant: *a qualifier is only ever emitted where a bare alias introduces it, and a single-table
  write emits none.* It is also provider-independent, which the alias trick is not.

Steps:

1. **Reproduce on live PostgreSQL first** — filtered `Delete`, filtered `Update`, and the function-wrapped
   filter shapes (`LOWER`, `COALESCE`, `.Date`) that a partial fix would miss.
2. `AbstractConnectorBase.StripTargetTableQualifier(sql, tableName)` — remove `TableName.` from the rendered
   clause. Text-level on purpose: the qualifier can arrive **function-wrapped** (`LOWER(T.Col)`,
   `COALESCE(T.A, T.B)`, `DATE(T.When)`), so operating on condition names one at a time is what misses them.
   Guard the left edge (`(?<![A-Za-z0-9_."])`) so a *different* table whose name ends with the target's —
   `MyPerson.Col` when the target is `Person` — is not corrupted into `MyCol`.
3. Call it from `AddRequiredWhere` only. **Never mutate the caller's `Condition` objects** — this file has
   been bitten three times by writing to a caller-owned object (CR-M168, TASK-113), and the rendered string
   is ours.
4. Verify the two clauses this does NOT need to touch, rather than assuming: the UPDATE **SET** list builds
   from `field.Name` / `GetSelectFields()` (both bare) and `ParseExpression(..., withTableName: false)`, so
   it is already unqualified.
5. `SelectCount` + the aggregate path (criterion 4) go through `CreateSelectCommand`, so TASK-211's alias
   should already cover them — **confirm on the live server**, don't reason it.
6. Red-verify, 23-suite sweep, record the rule in § Conventions beside TASK-211's.

**Parameter names are safe, checked before choosing this.** `SqlBuilderContext.GenerateParameterName`
sanitizes with `[^a-zA-Z0-9_]`, so a parameter derived from `OfPersons.Name` is `@WHEREOfPersonsName0_0`
with **no dot** — a `TableName.` strip cannot corrupt a binding. Had it kept the dot, this approach would
have silently broken every parameterized filter, which is the kind of thing to check before writing code
rather than after a suite goes red.

## Progress log

- step 1 — picked via `/tasks pick TASK-216`; plan drafted inline. PostgreSQL 16.4 re-provisioned from the
  scratchpad (TASK-211's recipe; Docker still unavailable — third use, so the recipe is well proven).
- step 2 — reproduced: **4 of 6** first-run probes failed, all four write shapes including both
  function-wrapped ones. The two that passed are the pins above. No production code changed yet.
- step 3 — fix in `AbstractConnectorBase.StripTargetTableQualifier` (new) + `AddRequiredWhere`. Verified by
  reading, not assuming, that the UPDATE **SET** list needed nothing: it builds from `field.Name` /
  `GetSelectFields()` and `ParseExpression(..., withTableName: false)`, all bare.
- step 4 — spawned [[TASK-217]] (the all-columns SET list), measured on two providers first.
- step 5 — sweep: 23 SQL suites green. One existing test needed updating; see § Outcome.
- step 6 — red-verified: **4 of 22** live, **3 of 500** offline. Restored, re-run, all green.
- step 7 — close gate: `verify-conventions` clean (0 warnings, no CS8xxx, single-file production diff,
  § Conventions + § Recent Updates both updated in the same change). `code-review` / `security-review`
  inline: the rewrite only ever *deletes* text matching the escaped target name, so it cannot widen a
  clause or inject; the pattern is linear (no ReDoS); `Regex.Replace`'s static cache absorbs the per-call
  pattern build. The one real risk — corrupting a longer table name — is the left-edge guard, and it has
  its own test.
- step 8 — closed `done`; production `7b60044`, tests `5b3deb9` + `d317189`.
