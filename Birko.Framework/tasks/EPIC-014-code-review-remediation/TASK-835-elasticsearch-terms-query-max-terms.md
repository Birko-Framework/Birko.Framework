---
id: TASK-835
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch: an `ids.Contains(x.Col)` filter is refused past `index.max_terms_count`

## Context

Found by TASK-833's survey (2026-11-07), from reading the code; not measured. The user chose to file it separately.

- The Elasticsearch translator renders `ids.Contains(x.Col)` as one `TermsQuery` (`Birko.Data.ElasticSearch`,
  `ElasticSearch.cs` around line 823).
- Elasticsearch refuses a terms query with more terms than `index.max_terms_count`, which defaults to 65,536.
- Past it the query fails loudly.
- **A cheap fix that stays correct within one query:** a bool `should` of several terms queries, each under the limit.
  Unlike TASK-833's SQL chunking it stays one request, so it would be valid under OR or NOT too.

## Acceptance criteria

- [x] Measured against the live Elasticsearch suite (`live-tests.yml`): a list of 65,537 Guids fails today, with
      the error recorded.
      → Measured 2026-11-07 against the local `birko-live-es` (7.17.25, `BIRKO_ES_URL=http://localhost:9202`).
      `live-tests.yml` has no Elasticsearch job, so this was a local run only.
      `ReadAsync(x => ids.Contains(x.Guid!.Value))` with 65,537 ids returned HTTP 400
      `search_phase_execution_exception` / `query_shard_exception`: "The number of terms [65537] used in the Terms
      Query request has exceeded the allowed maximum of [65536]".
- [x] A list past `index.max_terms_count` is rendered as a bool `should` of terms queries, each within the limit, and
      the result equals the single-query oracle. A `NOT` around it stays correct: `must_not` of the bool.
      → `ElasticSearch.TermsWithin` returns one `TermsQuery` up to the limit, and past it
        `BoolQuery { Should = chunks, MinimumShouldMatch = 1 }`. It is still one request, so it holds under `||`, and
        `!` gives `must_not` of the bool.
      → Pinned by `Task835TermsCountLimitTests`: 7 shape tests with no server (65,536 stays one query;
        65,537 / 131,072 / 131,073 give 2 / 2 / 3 chunks; NOT; small limits) and a live read, its negation and an OR
        count, each against the C# oracle.
      → Red before wiring: 3 shape tests ("found TermsQuery") and the live test (the 400 above).
- [x] The limit is read from the index settings or declared, and a test pins whichever.
      → Declared: `ElasticSearch.MaxTermsCount`, default 65,536, below 1 refused. It sits beside `MaxResultWindow`.
        The translator is static with no client, so reading the setting would cost a request per query. The default is
        pinned by a test.
      → The live test uses the real limit rather than lowering the process-wide static, which would affect parallel
        tests (rule 61).
      → Suites green with live Elasticsearch: Data.ElasticSearch 333, BackgroundJobs.ES 22, ViewModel 6, Views 27,
        Migrations.ES 40, Sync.ES 13, Health.Data.ES 6, Workflow.ES 9.

## Out of scope

- The SQL providers (TASK-833) and Cosmos DB (TASK-834).

## Human test plan

N/A — fully covered by automated tests against the live suite.

## Implementation plan
