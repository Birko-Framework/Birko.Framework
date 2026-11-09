---
id: TASK-877
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-868]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB native facet aggregation is refused for every filter

## Context

Found by TASK-868 (2026-11-09), measured live: `Aggregate` with one GROUP BY field and no time bucket sends a facet
query against the collection, and RavenDB refuses it for any filter, plain ones included: `InvalidQueryException: Facet
query must be executed against static index`. Loud, not wrong rows. Pinned by the inverted test
`Known_gap_the_native_facet_aggregate_is_refused_by_RavenDB_for_any_filter`.

## Acceptance criteria

- [ ] **Decide with the user:** fall back to the LINQ aggregate (already correct for several GROUP BY fields), or deploy
      a static index for the facet query.
- [ ] Implement; invert the known-gap test and remove `IsNativeFacetGap` (rule 56).

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
