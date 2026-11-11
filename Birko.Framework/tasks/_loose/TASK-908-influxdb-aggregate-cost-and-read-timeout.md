---
id: TASK-908
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-907]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB: aggregate cost, native Flux and the read timeout

## Context

Measured by TASK-907 (2026-11-10, live container, 10 groups, Count + Sum): since TASK-906 every InfluxDB aggregate reads
every row and aggregates through `AggregateHelper`. 10k points ≈ 4.0 s; **100k points fail at the client's 10 s default
timeout** (any read of that size, not only aggregates). Reading 100k rows takes 45–50 s and ~290 MiB; the in-memory
aggregation 51–79 ms. A server-side Flux pivot + group + sum takes 46–49 s too: each point carries its own `Guid` tag, so
every point is its own series and the pivot dominates. Native Flux would not be faster without a schema change.

**Decide with the user:** drop native Flux (recommended: measured no faster), and whether the per-point `Guid` tag and the
10 s timeout should change.

**Decided by the user (2026-11-11):** the read timeout becomes configurable on the InfluxDB settings (the consumer's
value wins); reproduce the 100k-point failure first and document the series-per-point cost. Native Flux: re-measured after the point-layout change in TASK-910 (filed at the user's request); not built
here.

## Acceptance criteria

- [x] The user's decision on native Flux recorded.
      → Native Flux moved to TASK-910 (re-measured after the point-layout change), filed at the user's request
        2026-11-11; not built here.
- [x] The read timeout over ~100k points reproduced (rule 54) and fixed or documented; the series-per-point cost decided.
      → Reproduced 2026-11-11 live (InfluxDB.Client 5.1.0, 100k points): `ReadAsync()` failed after 10,052 ms and
        `AggregateAsync` after 10,016 ms with a raw `TaskCanceledException` (→ `IOException` → `SocketException 995`); the
        library default is 10 s, overridable by `?timeout=` in the URL (honoured: 700 ms failed at 787 ms).
      → New `Settings.RequestTimeout` (`TimeSpan?`, default null = the URL's timeout, else the library's 10 s, kept on
        purpose); the setter refuses zero, negatives and > 4,294,967,294 ms (each measured to break the client);
        `InfiniteTimeSpan` accepted; setting it while the URL carries a `timeout` is refused when the client is built.
        One funnel `Settings.GetClientOptions()` builds every store client (repositories and `InfluxDbUnitOfWork.FromStore`
        reuse it); `GetConnectionString()` still runs before `Token` is read (Symbio.Affiliate derives its token there);
        `LoadFrom` copies it. With 3 minutes: 100k read 49.9 s, aggregate 40.4 s. Series-per-point cost documented in
        `Birko.Data.InfluxDB/CLAUDE.md` and README (→ TASK-910).
      → `Task908RequestTimeoutTests` (20 offline, incl. a never-answering local server: sync and async give up at 500 ms)
        and `Task908InfluxReadTimeoutLiveTests` (2 live; 100k behind `BIRKO_INFLUX_HEAVY`). Red with the old client
        wiring: 6 of 22; green 22/22. Live: InfluxDB 88, InfluxDB.ViewModel 5, Migrations.InfluxDB 46 on the working tree
        and on a staged HEAD + patch snapshot. Consumers: Symbio.Affiliate builds the client as before (default null).
      → **Found, filed:** TASK-914 (a timeout surfaces as the caller's cancellation and is never retried; `LoadFrom` drops
        `RetryPolicy`).

## Out of scope

- Aggregate correctness (TASK-906, TASK-907, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
