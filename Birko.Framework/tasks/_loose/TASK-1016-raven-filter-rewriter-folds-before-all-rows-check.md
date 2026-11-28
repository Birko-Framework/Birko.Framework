---
id: TASK-1016
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1008, TASK-867, TASK-865, TASK-215]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenFilterRewriter folds a null ordering before its all-rows check

## Context

Found by [[TASK-1008]]'s pass 1 (NULL semantics) on 2026-11-28; the user asked for it to be filed.

`RavenFilterRewriter.Rewrite` (`Birko.Data.RavenDB/Expressions/RavenFilterRewriter.cs`, line 70) runs
`NullOrderingFold.Rewrite` before `PredicateScope.IsExplicitAllRows`. So `!(x.Score > nul)` with a null `nul` folds to
`true` and is returned as "no predicate", which a write treats as every document. Today that is safe only because the
public write guard (TASK-865) refuses the filter before `Rewrite` runs. A future caller of `Rewrite` on a write path
that does not guard first would write the whole collection with no exception.

## Acceptance criteria

- [ ] Every write path that calls `Rewrite` is listed and shown to guard first (or the ones that do not are fixed,
  red first).
- [ ] `Rewrite` no longer turns a folded-to-true filter into "no predicate" silently on a write: either the order is
  changed so only an explicit `x => true` drops the predicate, or a write-path test pins the guard-first order.
- [ ] RavenDB's live suite passes.

## Human test plan

N/A — fully covered by automated tests.
