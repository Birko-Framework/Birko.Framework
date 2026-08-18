---
id: TASK-251
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-142, TASK-208]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Framework]
---

# Regen the three wide-surface spec areas DV7 still reports

## Context

`/roadmap --check` reported **DV7 ×9** on 2026-08-18. Six were closed the same day — two by
measurement (`repository-contract`, `views-and-aggregation` had no content drift at all, only unknown
baselines, and the repos in question had no commits touching those sources since the specs were
written), and four by harvest (`migrations`, `schema-index-and-ddl`, `event-bus-and-messaging`,
`background-jobs`).

**Three remain, and they are left deliberately rather than rushed.** Each has accumulated drift from a
wide set of tasks, so a faithful harvest is a real piece of work and its diff review is supposed to be a
deliberate gate — the point of the review is to ask "was this behavioural change intended", which a
hurried pass cannot answer.

| area | drifted sources | behaviour to absorb |
|---|---|---|
| `filter-expression-translation` | Birko.Data.SQL (3), .MSSql / .MySQL / .PostgreSQL / .SqLite (1 each) | TASK-242/243 (bulk writes joining a boundary, the DDL funnel), TASK-245/248/249 (shared `AbstractConnectorBase` + `DataBase_RuleField` changes), TASK-229/230/234 (package declarations, no behaviour) |
| `bulk-filter-operations` | Birko.Data.MongoDB (1), Birko.Data.SQL (2); **unknown baseline** for Birko.Data.ElasticSearch | TASK-212 / SH-M023 (`RequireBoundedFilter`, and it not being wired into the base it lives on), TASK-222/224 (boolean-constant reduction, the `.Date` half-open rewrite), TASK-240 (Mongo reads joining the session) |
| `unit-of-work-and-transactions` | Birko.Data.ElasticSearch (1), Birko.Data.Patterns (3), Birko.Data.SQL (1); **unknown baseline** for CosmosDB, InfluxDB, MongoDB, RavenDB | TASK-240 (per-backend statement of what a boundary promises; ElasticSearch declaring it cannot honour one), SH-H041–044 (a leaf that cannot be evaluated never widens the filter) |

## Why this is not just "run the command"

- **Much of the behaviour is already written down** in the aggregator `CLAUDE.md` § Conventions — the
  bounded-filter guard, the per-backend boundary promises, the span-`Contains` rewrite family, the
  `.Date` truncation. The specs are the part that never caught up. So the harvest is mostly
  transcription *from code, cross-checked against those rules* rather than fresh discovery — but it is
  still per-scenario work across three large files (`filter-expression-translation` alone is the widest
  area in the map).
- **Two areas carry unknown baselines that could NOT be closed by measurement**, unlike the six already
  done: ElasticSearch under `bulk-filter-operations`, and four document stores under
  `unit-of-work-and-transactions`, all have commits touching those sources since their spec was written.
  Those subsets need a real content check before a baseline is stamped — stamping them without one is
  precisely the "mark a stale spec fresh" failure DV7 exists to catch.
- **The spec diff review is the deliverable, not the file rewrite.** Any behavioural change in the diff
  that no decision explains is a *finding*, and this batch spans ~10 tasks' worth of change — a good
  chance of surfacing something, which is worth doing attentively.

## Acceptance criteria

- [ ] `filter-expression-translation`, `bulk-filter-operations` and `unit-of-work-and-transactions`
      regenerated from current code, per-scenario, with the stable-wording rule respected — the diff must
      mean "behaviour changed", not "the harvester rephrased everything".
- [ ] Every behavioural change in each diff classified: matches a recorded decision / intended anyway /
      **unexplained → filed as its own task**. Record the classification, don't just skim it.
- [ ] The 5 unresolvable unknown baselines (ElasticSearch ×1, CosmosDB/InfluxDB/MongoDB/RavenDB ×4)
      either verified against current source and stamped, or left unstamped with the reason recorded —
      never stamped on assumption.
- [ ] `generated-at` / `generated-on` / `source-commits` re-stamped only for what was actually harvested.
- [ ] `/roadmap --check` reports **DV7: none** afterwards, and the run is recorded here with the count.
- [ ] `shaped-by` re-derived for the three areas (the evidence pass already ran project-wide on
      2026-08-18; re-run it so the values reflect any newly-attributed tasks).

## Out of scope

- **DV5 ×17** — the `_loose` tasks with no epic and no feature. A separate, larger question about where
  that pile belongs; [[TASK-149]] and [[TASK-142]] already circle parts of it.
- The spec **map**'s own gaps. `schema-index-and-ddl` was found not to glob the connectors that emit
  index DDL (recorded in [[TASK-245]]), and [[TASK-142]] owns the coverage audit. Do not widen globs here
  as a side effect of a regen — a map change alters what every future DV7 measures.
- `shaped-by-unresolved` is 80 of 203 feature-linked tasks project-wide. Improving that ratio means
  backfilling `pr:` on old tasks, which is its own job; the count is stamped honestly in the meantime.
