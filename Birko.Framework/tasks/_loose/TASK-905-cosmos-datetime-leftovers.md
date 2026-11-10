---
id: TASK-905
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-902]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB DateTime: leftovers after TASK-902

## Context

Found by TASK-902 (2026-11-10):
- Two wall-clock key formulas: TASK-897's `CosmosAggregationHelper.WallClockKey` (SQL text, with suffix, for aggregates)
  and TASK-902's filter key (expression tree, no suffix). They agree on ordering but should have one producer.
- DateTimeOffset comparisons, lists and sorts are refused; supporting them needs a UTC-normalising key, and Cosmos
  `DateTimeAdd` is not reachable from LINQ.
- `TimeSpan` members were not examined; their text likely has the same variable-length problem.
- An index-served sort and filter would need Cosmos computed properties, which the emulator does not evaluate; needs a
  real account.

**Scoped by the user (2026-11-10):** do the TimeSpan check (fix or refuse) and unify the key producer now.
DateTimeOffset support and computed properties are deferred to TASK-903 (until a consumer runs on Cosmos; none does
today) and are not acceptance criteria here.

## Acceptance criteria

- [x] Measure each (rule 54).
      → Measured 2026-11-10 on the emulator: System.Text.Json stores a TimeSpan as `[-][d.]hh:mm:ss[.fffffff]` (day
        count only when present, unpadded; 7 fraction digits only with a fraction), and Cosmos compared that text:
        `"1.02:03:04"` < `"23:00:00"`, `"10.00:00:00"` < `"2.00:00:00"`, `"-00:00:01"` < `"-00:00:02"`. Over 17 documents
        and 124 shapes against C#: reads 68 wrong, counts and aggregate WHERE 60, update/delete 68; view filters 59 of 96;
        sorts 4 of 4; `Equals` threw `DocumentQueryException`; 8 shapes with no exact rendering (`Ticks`, `TotalSeconds`,
        `Days`, `Add`, `+`, negation, `Duration()`, `Compare(..) > 1`) answered on every path. Equality was exact.
- [x] Unify the key producer; fix or refuse TimeSpan.
      → TimeSpan orderings fixed (`CosmosFilterRewrite.TimeSpanKeys`, last in `Prepare`): sign as a plain range on the
        member, then a fixed 23-character magnitude key (`SUBSTRING`/`REPLACE`/`INDEX_OF`/`CONCAT`, so the view renders
        it), negatives in reverse magnitude, caller values keyed in C# (`TimeSpanKeyOf`, `MinValue` included); `==`, `!=`,
        `Contains` keep their index. `TimeSpanShapes` (before `NullSemantics`) translates `Equals` and `Compare`/`CompareTo`
        against 0. Refused by name before any request: TimeSpan members, TimeSpan arithmetic on a member, `Compare`
        against non-zero, and any TimeSpan sort (`CosmosDateTimeOrder.Plan`). After: 0 of 124 wrong on every store path.
      → One producer (rule 16): `WallClockParts` defines the DateTime key; the filter key and the new `WallClockKeySql`
        render it; `CosmosAggregationHelper.WallClockKey` delegates. Two Task897 SQL-text pins updated on purpose.
- [x] Tests, red before each fix.
      → `Task905TimeSpanLiveTests`, `Task905KeyTests`, `Task905CosmosViewTimeSpanTests`. Red with the call sites
        reverted: store 11 of 21, view 3 of 3, the unification structural test red with the old aggregate formula.
      → Live on a HEAD + patch snapshot against a fresh emulator: CosmosDB 473, Views 169 (the view TimeSpan matrix 96 shapes, 0 wrong; member-vs-member `==`/`!=` compare stored texts, as the view translator refuses any member-vs-member comparison otherwise), ViewModel 5, BackgroundJobs 28, Migrations 44, Sync 4, Workflow 8 - all green.
      → No consumer stores a TimeSpan in Cosmos (Symbio's `Cooldown` / `Duration` are `[NotMapped]`; Cosmos is configured
        nowhere). Recorded: the keys assume System.Text.Json's formats (as TASK-902); a TimeSpan sort could later sort on
        stored ticks. Local note: the long-running emulator broke bulk clients (`PartitionKeyRange is unexpected`) and
        views until restarted.

## Out of scope

- DateTime filters and sorts (TASK-902, done).
- DateTimeOffset support and computed properties (deferred to TASK-903).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
