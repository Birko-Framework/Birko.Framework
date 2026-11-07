---
id: TASK-867
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-865]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB and Cosmos DB translate an ordering against null differently from C#

## Context

Found by TASK-865 (2026-11-08), measured live. In C#, a lifted ordering against null (`x.Score > nul` with `nul`
null) is always false.
- **RavenDB:** `Score > null` matches every row with a non-null Score. So `Score > nul || A > 4` reads and writes the
  wrong rows, silently. The whole-table guard does not catch it, because the filter still constrains.
- **Cosmos DB:** a negated null ordering matches nothing, because NOT of undefined is undefined. So
  `!(Score > nul) && A > 4` misses rows, silently.
- **Elasticsearch:** refuses a null ordering with `NotSupportedException`. That is safe, but the shapes cannot run.

TASK-865's `NullOrderingScopeContract` pins these as `KnownTranslationGap` entries in each provider's test class. Each
one is an inverted test that fails once the backend matches C#, and then gets removed (rule 56).

## Acceptance criteria

- [ ] Re-measure every `KnownTranslationGap` entry (RavenDB, Cosmos DB, Elasticsearch, and also MongoDB if any
      control differs), on reads and on the writes the guard allows. Record each.
- [ ] Each provider's translator folds an ordering against a null value to constant false, as C# does, before
      rendering. Where it cannot, it refuses by name. Elasticsearch may translate to a constant instead of refusing.
- [ ] Remove the `KnownTranslationGap` entries as each one turns green (rule 56). Every importer suite of a changed
      provider stays green, live.

## Out of scope

- The whole-table guard (TASK-865, done).
- SQL (TASK-859, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
