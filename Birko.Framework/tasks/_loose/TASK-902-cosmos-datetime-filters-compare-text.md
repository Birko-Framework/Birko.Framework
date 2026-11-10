---
id: TASK-902
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-899]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB `DateTime` filters compare the stored text, not the instant

## Context

Found by TASK-899 (2026-11-10), reproduced on the emulator: with UTC values stored at 10:00:00 and 10:00:00.5,
`x.T > 10:00:00Z` returns nothing where C# returns the 10:00:00.5 document. The stored fraction has variable length
(`.5Z` sorts before `Z`), and values stored with `Z`, an offset or no suffix compare wrongly against each other too.
Affects store and view filters and view sorts by a `DateTime` field. TASK-897's `WallClockKey` already solved ordering
for aggregates.

## Acceptance criteria

- [x] Reproduce (rule 54) on reads, counts, writes, aggregates' filters and view sorts: fractions, kinds, offsets,
      `DateTime?`, against C#.
      → Serializer facts (measured 2026-11-10): ISO 8601 with only the fraction digits needed (`10:00:00`, `.5`,
        `.1234567`); UTC `Z`, Local the writer's offset, Unspecified no suffix. C# compares ticks and ignores Kind; Cosmos
        compares text, so order is right across seconds and wrong only inside one second (`"…00"`, `"…00+01:00"`,
        `"…00.5Z"`, `"…00Z"`): whole-second values (midnight, `DateTime.Today`) are the common trigger.
      → Before, against C# (12 documents, every kind and fraction): store filters 76 of 88 shapes wrong on some path
        (reads 73, counts 70, aggregate WHERE 70, update/delete 76; 6 threw); store sorts 62 of 120 page cases; view
        filters 53 of 72; view sorts 22 of 32.
- [x] Every comparison and sort orders as C# does (one producer, e.g. TASK-897's wall-clock key or a normalized stored
      form), or a shape that cannot is refused by name.
      → Filters: one step `CosmosFilterRewrite.DateTimeKeys`, last in `Prepare` (store, view, counts, writes, aggregate
        WHERE, chunking): every DateTime comparison compares a wall-clock key (`SUBSTRING(t,0,19)` + fraction padded to 7
        digits); caller values keyed from ticks in C#; against a value, the key sits behind a plain text range on the
        member so the range index still serves it. Member vs member, `Contains` lists (chunking and TASK-899's missing
        leg kept), `.Date` vs `.Date`, `Equals`, `Compare`/`CompareTo` against 0 translated.
      → Sorts: new `CosmosDateTimeOrder` (both stores and the view) keeps the server's text order and re-sorts each run of
        rows sharing a second by ticks; a run crossing a page edge is fetched whole.
      → Refused by name before any request: DateTimeOffset comparisons/lists/sorts, members of a stored DateTime
        (`Year`, `Ticks`…), a DateTime computed from a member (`AddDays`), `Compare` against anything but 0 — before, 7 of
        these 9 answered wrongly and 2 threw.
      → Assumption (as TASK-897): an offset-stored value was written with the reader's offset. Write-side fixed-width
        storage and Cosmos computed properties considered and recorded (existing documents keep their text; the emulator
        returns nothing for computed properties).
- [x] Tests, red before the fix.
      → `Task902DateTimeLiveTests`, `Task902DateTimeRenderingTests` (8 offline), `Task902CosmosViewDateTimeTests`; two
        `CosmosViewTranslateValueTests` inverted (rule 56). Red with the call sites reverted: store 4 live + 4 of 8
        rendering, view 4 + 2; after: 0 wrong everywhere, refusals on every path with rows unchanged.
      → Live: CosmosDB 436, Views 165, ViewModel 5, BackgroundJobs 28, Migrations 44, Sync 4, Workflow 8. Cosmos is not
        configured in any consumer; if enabled, Symbio's ~61 DateTime filters become exact and keep the range index.
      → **Found, filed:** TASK-905 (two key formulas to unify; DateTimeOffset and TimeSpan support; computed-property
        design on a real account).

## Out of scope

- The items TASK-899 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
