---
id: TASK-867
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Re-measure every `KnownTranslationGap` entry (RavenDB, Cosmos DB, Elasticsearch, and also MongoDB if any
      control differs), on reads and on the writes the guard allows. Record each.
      → Measured live 2026-11-08: a read contract was added, 22 shapes × Read/ReadAsync/Count/CountAsync checked
        against compiled C#.
        - **RavenDB:** writes 18 of 18 diverged; reads 68 of 88 wrong (`Score > nul` counted 3, not 0).
        - **Cosmos DB:** writes 12 of 12 diverged; reads 52 of 88 wrong (every NOT shape missed rows).
        - **Elasticsearch:** writes 18 of 18 refused; reads 68 of 88 refused.
        - **MongoDB (new):** reads 12 of 88 wrong (`$gte: null` matched the null rows).
        - **Cosmos view:** 6 of 10 wrong. **Raven view:** wrong too.
- [x] Each provider's translator folds an ordering against a null value to constant false, as C# does, before
      rendering. Where it cannot, it refuses by name. Elasticsearch may translate to a constant instead of refusing.
      → One producer: `Birko.Data.Core/Expressions/NullOrderingFold.cs`. It replaces a lifted ordering against null
        with `false` and reduces the constants that leaves, so only a whole filter can be constant. `MatchesNothing`
        reports that case. `PredicateScope.IsNullOrdering` delegates to it.
      → It is called after the write guard in each translator:
        - **RavenDB:** `RavenFilterRewriter`. A filter that folds to false is answered without a query, since RavenDB
          refuses constant predicates.
        - **Cosmos DB:** the stores and `CosmosViewStore`.
        - **Elasticsearch:** match_none / match_all.
        - **MongoDB:** the stores and `MongoViewStore`.
- [x] Remove the `KnownTranslationGap` entries as each one turns green (rule 56). Every importer suite of a changed
      provider stays green, live.
      → With the fix in and the entries still present, exactly the 18 + 12 + 18 inverted tests failed with "now matches
        C#". The entries were then removed; the hook stays in the contract, unused.
      → New tests:
        - `NullOrderingFoldTests` (20);
        - Cosmos and Raven view tests;
        - an Elasticsearch translation test. Red against HEAD: Cosmos view 6 of 10, Raven view, Elasticsearch 5 of 5.
      → 31 importer suites of RavenDB, Cosmos DB, Elasticsearch and MongoDB (Views included) green live, plus
        Data.Core, InMemory, JSON, XML, SQL.SqLite (1272) and SQL.Tests (810).
      → **Found, filed:** TASK-868. `RavenDBIndexManager` and the aggregation helpers skip the fold; `RavenViewStore`'s
        new `Contains` → `In` rewrite has no test of its own.

## Out of scope

- The whole-table guard (TASK-865, done).
- SQL (TASK-859, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
