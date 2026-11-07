---
id: TASK-851
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] The limit can be set per store or per index, from the store's settings or read once from the index settings and
      cached per index, with the static kept as the default.
- [ ] The translator receives the limit for the index it translates for, and no shared state is written per call.
- [ ] Tests: two stores with different limits split at their own limit, in parallel.

## Out of scope

- The split itself (TASK-835).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
