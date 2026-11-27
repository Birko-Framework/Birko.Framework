---
id: TASK-1008
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-1002]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Regenerate the filter-expression-translation spec in several passes

## Context

At the 2026-11-27 `/specs regen`, the harvest of `filter-expression-translation` reported the area too large to harvest
honestly in one pass, so no spec was written; the user asked for this task. Measured at `f5473629`:

- The spec was last generated at `22cd73f3` (2026-11-07). It is 153 KB with 55 requirements.
- Its 54 source files changed by +4582 / −882 lines in 38 files since then.
- The changes came from the SQL filter work of the last three weeks:
  - NULL semantics and orderings against null (TASK-859, TASK-861, TASK-865, TASK-867);
  - ordinal string lists (TASK-873, TASK-895);
  - rejected column names (TASK-891);
  - aggregates and value fragments (TASK-876 to TASK-911);
  - Cosmos DB and Elasticsearch translation (TASK-896, TASK-899, TASK-902, TASK-905, TASK-851).

## Acceptance criteria

- [ ] The delta is harvested in about five scoped passes, each over its own slice of the sources, with its own diff
  review:
  1. NULL semantics;
  2. method calls, casts and string matching;
  3. booleans and parameters;
  4. rules;
  5. Elasticsearch (and other non-SQL translators).
- [ ] The stable-wording rule holds: unchanged requirements stay verbatim; each pass changes only what the code
  contradicts.
- [ ] Each pass's diff is reviewed by the user (accept or reject), with suspected bugs offered as tasks.
- [ ] The spec is stamped once, after the last pass, at the HEAD the passes harvested. The usual unmapped and
  spec-links checks are run.
- [ ] Map question decided by the user: should `SqlValueFragmentDialect_Aggregate.cs` and `SqlValueFragmentDialect_View.cs`
  leave this area's glob? They implement aggregate and view rendering, which `views-and-aggregation` covers.
- [ ] Consider splitting the area itself (for example SQL translation vs. non-SQL translation) if a pass shows the
  spec is still too large to review as one diff. That is a map change, the user's decision.

## Out of scope

- Code changes. Suspected bugs found while harvesting become their own tasks.

## Human test plan

N/A — fully covered by automated tests (the deliverable is a reviewed spec).
