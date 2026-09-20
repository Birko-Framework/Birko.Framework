---
id: TASK-479
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: in-progress
priority: P2
assignee: ai
created: 2026-09-20
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-009]
pr: null
github-issue: null
jira-key: null
---

# 19 live gates return silently instead of honouring BIRKO_REQUIRE_LIVE

## Context

`.github/workflows/live-tests.yml` declares `BIRKO_REQUIRE_LIVE: '1'` at workflow level, and its own
header says why:

> **BIRKO_REQUIRE_LIVE=1 IS MANDATORY HERE.** Without it, a suite whose server never came up reports
> "skipped" and the job goes green — a broken fixture and a passing run then look identical.

That promise is not kept across the tree. An audit on 2026-09-20 (prompted by [[TASK-042]]'s red run,
[35458738975](https://github.com/Birko-Framework/Birko.Framework/actions/runs/35458738975)) compared
every `BIRKO_*` name read under `tests/` against the 16 the workflows set. **19 live gates across 8
files fall through to a bare `return;`** when their server is absent — so they report **Passed**, not
Skipped, and `BIRKO_REQUIRE_LIVE` never sees them:

| file | gates |
|---|---|
| `tests/Birko.BackgroundJobs.Redis.Tests/RedisJobLockProviderTests.cs` | 6 |
| `tests/Birko.BackgroundJobs.Redis.Tests/RecurringSchedulerLeaderElectionTests.cs` | 3 |
| `tests/Birko.BackgroundJobs.SQL.Tests/RecurringSchedulerLeaderElectionTests.cs` | 3 |
| `tests/Birko.Data.MongoDB.Tests/Stores/MongoStoreRoundTripLiveTests.cs` | 2 |
| `tests/Birko.Data.MongoDB.Views.Tests/MongoViewIdentityLiveTests.cs` | 2 |
| `tests/Birko.Data.MongoDB.Tests/Stores/MongoArrayContainsLiveTests.cs` | 1 |
| `tests/Birko.Data.MongoDB.Tests/Stores/MongoFilterMatrixLiveTests.cs` | 1 |
| `tests/Birko.Data.RavenDB.Tests/Stores/RavenFilterMatrixLiveTests.cs` | 1 |

(The per-file counts are from grepping the `return;` gate shape and are approximate where a file
routes several tests through one helper; the file list is exact.)

**These all run today** — their variables *are* set, and the 2026-09-20 green run proves real server
work (Redis 16 s, RavenDB 24 s, MongoDB 97 tests / 0 skipped). The defect is latent: **if a container
failed to start, every one of them would go green having done nothing**, which is precisely the state
the workflow header forbids.

**The asymmetry is the tell.** The SQL suites are near-total — MSSql 13/13 files honour it,
PostgreSQL 12/12, TimescaleDB 5/5, MySQL 11/12 — and every gap is non-SQL. The SQL side was hardened
during EPIC-014; the others were not.

Two adjacent items found by the same audit, folded in here because they are the same file family:

- **`tests/Birko.Data.SQL.PostgreSQL.View.Tests/` (3 files)** uses `[SkippableFact]` + `Skip.IfNot`.
  That is *honest* — an absent server reports **Skipped**, visibly, unlike the silent returns — but it
  still leaves the job green, so it needs the same promotion.
- **`PostgreSqlViewRoundTripTests.cs:62`** still documents the gate as `BIRKO_PG_TEST` while the code
  at `:74-79` reads `BIRKO_PG_HOST`. A fossil of the same packed-variable convention [[TASK-042]]
  removed — documentation-only, no behavioural effect.

The reference shape is the one TASK-042 landed
(`tests/Birko.Data.SQL.Providers.Tests/ProviderStoreFactoryTests.cs`, `Resolve`) and the twelve SQL
suites that predate it: write the skip to `ITestOutputHelper` naming the variable to set, and throw
`InvalidOperationException` when `BIRKO_REQUIRE_LIVE` is non-blank.

## Acceptance criteria

- [ ] Each of the 8 files above reports its skip to test output naming the variable that would opt it
      in, instead of returning silently
- [ ] Each honours `BIRKO_REQUIRE_LIVE`: a non-blank value turns an absent server into a failure
- [ ] The 3 `Birko.Data.SQL.PostgreSQL.View.Tests` files honour it too — `Skip.IfNot` is kept for the
      opt-out case, but a required run fails rather than skipping
- [ ] `PostgreSqlViewRoundTripTests.cs:62` names `BIRKO_PG_HOST`, not `BIRKO_PG_TEST`
- [ ] **Proven, not asserted:** for each affected job, a run with the gate variable unset and
      `BIRKO_REQUIRE_LIVE=1` fails, and the same run without `BIRKO_REQUIRE_LIVE` passes with visible
      skip lines. A fix that only changes green to green has not been demonstrated
- [ ] `live-tests` is green end-to-end afterwards, with each affected suite's duration showing the
      tests still genuinely run

## Out of scope

- **`Birko.Data.CosmosDB.Tests`** — also silent-return, but its gate variable is set by no workflow
  and there is no Cosmos job at all, so promoting its gate would turn a green job red without giving
  it a server. That is [[TASK-480]], and this task must not touch it.
- Adding new live assertions or new coverage — this is about whether existing ones can be trusted to
  have run, nothing more.
- `Birko.Workflow.MongoDB.Tests` and `Birko.BackgroundJobs.MongoDB.Tests`, which sit in the MongoDB
  live job but touch no server at all (8 and 6 tests, 81 ms / 182 ms). Offline suites in a live job
  is a real oddity — there is less Mongo live coverage than the job list implies — but it is a
  coverage question, not a gate one.

## Human test plan

- [ ] Stop one container the SQL job depends on (or point its `BIRKO_*_HOST` at a dead port), run the
      affected suite with `BIRKO_REQUIRE_LIVE=1`, and confirm it goes **red** rather than green.
      Expected before the fix: green.
- [ ] Repeat with `BIRKO_REQUIRE_LIVE` unset and confirm the suite reports visible skip lines and
      passes — the developer-without-Docker path must stay usable.

## Implementation plan

_Populated by `/tasks plan TASK-479` — leave empty until then._
