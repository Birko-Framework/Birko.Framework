---
id: TASK-647
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Data sync: the Custom policy is skipped silently without a known pair; check timestamp round-trip

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `data-sync`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

Two read-only findings in the shared engine. (1) When both sides hold an entity with no known pair (lost or legacy
knowledge), the Custom policy falls to `GetWinner`'s `_ => "local"`, and `CustomConflictResolver` / `OnConflict` are
never asked (`Birko.Data.Sync/Internal/SyncProviderBase.cs:229,284`). (2) UNVERIFIED: the version is
`UpdatedAt.ToString("O")` recorded from the in-memory source and compared with the destination's read-back
(`:536,179-180`). A backend that changes DateTime Kind or precision on round-trip would read as changed every run, a
perpetual update or conflict. Also: a failed conflict resolution is reported twice (`OnError` and a `SyncError`,
`AsyncSyncProvider.cs:520-532`).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] (1) decided and tested
- [x] (2) measured live against the SQL providers, MongoDB, RavenDB and ElasticSearch; fixed if it drifts
- [x] A failure is reported once
- [x] Red on the current code; the spec `docs/specs/data-sync.md` updated if behaviour changes

## Resolution (2026-10-17)

**(2) Measured live first: it drifts on five of seven backends.** A temporary probe (since deleted) wrote
`UpdatedAt = 2026-10-17T12:34:56.1234567Z` and read it back by Guid:

| Backend | Read back |
|---|---|
| SQLite | `…56.1234567`, Kind Unspecified |
| PostgreSQL | `…56.1234560`, Unspecified |
| MySQL | `…56.0000000`, Unspecified |
| SQL Server | `…56.1233333`, Unspecified |
| MongoDB | `…56.1230000Z` |
| ElasticSearch, RavenDB | exact |

None of the five round-trips the `"O"` string. With a store doing what MongoDB does, the second bidirectional run
reported `Updated = 1`: the item read as changed, and the lossy copy then overwrote the precise source.

**Fix (2): rule 63, version what is stored.** Before a knowledge row is recorded, a side this item was written to
(its effective entity is not the one read at the start) is read back from its store by Guid, and its version comes
from that copy, in both providers. Normalising to a common precision was rejected: the floor would be MySQL's whole
second, so two edits within a second would read as unchanged. Changing `GetVersionHash`'s format was rejected too:
it would turn every existing knowledge row into a both-sides conflict on the first run after the upgrade.

**(1) Decision: Custom is asked.** The engine's own design note says "both changed is the policy … NewestWins /
Custom through a conflict carrying both items", so no user decision was needed. With no recorded pair, `Custom`
now returns a `Conflict` ("Present on both sides with no recorded pair") and so reaches `OnConflict` and
`CustomConflictResolver`. When the two copies carry the same version it returns `Skip` instead, so it does not ask
about an identical item; that also records the pair. Before, it fell to `GetWinner`'s `_ => "local"`.

**(3) Reported once, everywhere.** `OnError` ("called when an error occurs") fired only for a failed conflict
resolution, which then rethrew into the per-item handler and was recorded again, while every other failed item
never reached `OnError`. Now `ReportError` is the one producer: once into the result's errors, once to `OnError`,
for per-item and run failures alike. A throwing handler is swallowed (rule 25), and is pinned by a test. The conflict
path's rethrow-only catch is gone.

**Red check, by three neutralisations:** the Custom branch off failed 2 tests, the read-back off failed 2 (async and
sync), and `OnError` off in `ReportError` failed 3. Tests: `Task647SyncEngineTests` (9; the async provider and the
sync provider's twins, with a `LossyStore` / `SyncLossyStore` that keeps a millisecond, Kind-less copy).

**Regression:** the 9 suites that import `Birko.Data.Sync` (189 tests) are green, live. `Birko.Data.Sync.Tests` is 98
of 98.

**Spawned [[TASK-656]]:** MySQL stores a `DateTime` to the second and SQL Server to about 3 ms, for every model.
Sync is now immune; the data is not.

**Docs:** the spec `data-sync` (bidirectional, `GetWinner`, both conflict-error scenarios, the knowledge row,
per-item failures), `docs/sync.md` (decision table, versions, errors; it also had the pre-TASK-630 "no UpdatedAt is
always changed" line), and Recent Updates.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-647` — leave empty until then._
