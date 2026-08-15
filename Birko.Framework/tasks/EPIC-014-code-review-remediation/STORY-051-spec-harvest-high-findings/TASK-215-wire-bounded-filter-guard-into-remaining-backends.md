---
id: TASK-215
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-212, TASK-109]
pr: null
github-issue: null
jira-key: null
findings: []
---

# Wire `RequireBoundedFilter` into the InMemory and ElasticSearch destructive overrides

## Context

Spawned by [[TASK-212]] (2026-08-15). That task built the portable guard —
`AbstractBulkStore<T>.RequireBoundedFilter` / `AbstractAsyncBulkStore<T>.RequireBoundedFilter`, backed by
`Birko.Data.Expressions.PredicateScope` — which refuses a filter-based destructive operation whose predicate
covers every entity, unless every entity was asked for explicitly. It **wired it into MongoDB's four
overrides only**.

The guard is already available to every backend: it sits on the shared bulk bases, so adopting it is one line
per override. What is *not* established is whether the shape actually reaches a destructive path on the other
two backends, and TASK-212's discipline was one measured backend per task — wiring the rest blind would be
exactly the silent scope-widening the drain loop exists to avoid.

The same SH-M023 sweep that found MongoDB's four overrides also found these:

- **InMemory** — `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>` override the public
  `Delete(filter)`. These are read-then-loop over an in-process dictionary, evaluating the predicate as a
  **C# delegate**, so `!empty.Contains(x.Field)` is true for every entity by definition — no translation
  layer is involved and no measurement of a driver is needed. This one is provable by inspection and is the
  cheaper half.
- **ElasticSearch** — four public overrides. `ElasticSearch.cs` renders an empty negated `Contains` as
  `must_not MatchNoneQuery`, i.e. match-everything, and whether that reaches a destructive
  `DeleteByQuery` / `UpdateByQuery` unrefused is the open question. Needs the same offline-rendering
  treatment TASK-212 used for MongoDB (build the query, inspect it) before any wiring.

## Approach

1. **InMemory first** — it needs no backend measurement, only a test that the shape currently deletes
   everything and is refused afterwards.
2. **ElasticSearch second**, and only after establishing what the destructive paths actually send. If the
   client rejects the shape, or the store already refuses it elsewhere, record that and wire nothing.
3. Reuse `RequireBoundedFilter` exactly as MongoDB does — call it immediately after the existing
   `RequireFilter`, so the null case and the unbounded case stay two distinct, separately-named refusals.

## Acceptance criteria

- [ ] InMemory's overrides refuse a present-but-unbounded filter, with a test that fails on revert
- [ ] ElasticSearch's behaviour is **measured and recorded** before any wiring; if the shape cannot reach a
      destructive path there, that is written up and no guard is added
- [ ] Every backend touched keeps its explicit door working (`x => true`, `DeleteAll()`), executed by a test
      rather than assumed (§ SH-H037)
- [ ] The false-positive direction is covered per backend: a bounded filter, a non-empty negated `Contains`
      and an empty un-negated `Contains` are all still allowed
- [ ] Red-verified with the split as numbers; pins named as pins
- [ ] `/specs regen bulk-filter-operations` — the area globs the InMemory and ElasticSearch stores

## Out of scope

- Changing `PredicateScope` itself. If a backend needs a shape it does not model, that is a change to the
  analyser with its own blast-radius measurement, not a local workaround.
- Converting these public overrides to `protected *Core` methods, which is the real fix for why the guard has
  to be repeated at all. Pre-existing, noted in the spec, and its own work.

## Human test plan

`N/A — fully covered by automated tests.` Both backends run in-process (InMemory entirely; ElasticSearch's
query construction can be asserted without a cluster), so there is nothing a human can check that a test
cannot.

## Implementation plan

_Populated by `/tasks plan TASK-215` — leave empty until then._
