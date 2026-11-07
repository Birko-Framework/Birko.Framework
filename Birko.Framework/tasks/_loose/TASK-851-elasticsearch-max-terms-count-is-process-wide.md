---
id: TASK-851
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-835]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch's terms limit is one process-wide setting

## Context

Flagged by the filter-expression-translation respec (2026-11-07). The user chose to file it.
- TASK-835 declared `ElasticSearch.MaxTermsCount` as a static, beside the existing `MaxResultWindow`, because the
  translator is static and has no client.
- An index created with a smaller `index.max_terms_count` makes the consumer lower the setting for every index in the
  process, so other indexes pay the splitting cost for nothing.
- An index with a larger setting cannot use it.
- Rules 36 and 62 warn about process-wide state that carries per-index (per-caller) meaning.

## Acceptance criteria

- [x] The limit can be set per store or per index, from the store's settings or read once from the index settings and
      cached per index, with the static kept as the default.
      → `IndexSettings.MaxTermsCount` (null means the default) for the entity stores, and
        `ElasticSearchViewStore.MaxTermsCount { init; }` for views; below 1 is refused. The static `MaxTermsCount`
        (65,536) is the default.
      → Not read from the index: `index.max_terms_count` can change on a live index, so a cached value goes stale,
        and reading it costs a request and a privilege. A wrong declaration fails loudly with a 400, never with wrong
        rows.
- [x] The translator receives the limit for the index it translates for, and no shared state is written per call.
      → An `int termsLimit` is passed down every recursive `Parse*` method. `ParseFilterQuery`,
        `ParseRequiredFilterQuery` and `ParseExpression` take `int? maxTermsCount`; null reads the static once per
        translation. The stores pass `IndexMaxTermsCount` at all 14 translation sites.
- [x] Tests: two stores with different limits split at their own limit, in parallel.
      → `Task851PerIndexTermsLimitTests`: a sync and an async store with limits 3 and 7 run 50 counts each in parallel;
        a live test uses two indexes created with `max_terms_count` 5 and 8, where a store declaring nothing is refused
        by the server. `Task851ViewTermsLimitTests`: two view stores with limits 2 and 5, in parallel.
      → Red proof: with the store helper returning null and the view passing null, the parallel store, live and view
        tests failed.
      → Suites green against live Elasticsearch: ElasticSearch 339, Views 33, BackgroundJobs 22, ViewModel 6,
        Migrations 40, Sync 13, Health 6, Workflow 9.
      → A stale Views `CLAUDE.md` line (it said `BuildFilterQuery` delegates to `ParseFilterQuery`; it keeps its own
        CR-H047 guard) was corrected at close.

## Out of scope

- The split itself (TASK-835).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
