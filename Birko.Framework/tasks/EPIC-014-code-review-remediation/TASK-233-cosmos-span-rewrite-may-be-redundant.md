---
id: TASK-233
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-218, TASK-220, TASK-229]
findings: []
pr: null
github-issue: null
jira-key: null
---

# DECISION: the CosmosDB span-`Contains` rewrite may now be redundant — the SDK fixed it upstream

## Context

Surfaced by [[TASK-229]]'s float, and surfaced **by the test written to surface exactly this**.

[[TASK-220]] wired `SpanContains.Rewrite` into CosmosDB because the SDK could not translate an array's
`.Contains` — on .NET 9+ that binds `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` rather than
`Enumerable.Contains`, and `Microsoft.Azure.Cosmos` 3.46.1 threw `NotSupportedException`. Alongside the
rewrite it added a **premise pin**, `Without_the_rewrite_the_provider_rejects_the_array_spelling`, whose
comment read:

> *"Pins the defect itself, so the test above cannot quietly become vacuous if the provider starts
> supporting `MemoryExtensions.Contains` on its own — this is what would fail first."*

Floating the driver to `3.*` moved it **3.46.1 → 3.62.1**, and that test failed first, exactly as written.
The newer SDK translates the span-bound overload natively, and measured, it produces **byte-identical SQL**
to the rewrite's output:

```
SELECT VALUE root FROM root WHERE (root["Amount"] IN (1, 5))
```

So on 3.62.1 the Cosmos wiring is **redundant but harmless**. The test has been rewritten to assert
*equivalence* rather than rejection, which keeps it a live pin in both directions: if a later SDK regresses,
the raw spelling throws again and the test says the rewrite is load-bearing once more.

**Why this is a decision and not a deletion.** `3.*` admits a *range*, so "the SDK supports it" is not one
fact but a property of every version the float can resolve. Removing the wiring on the evidence of one
version would reintroduce the original defect for anyone pinned lower — and the original defect is a
**silent** one for some backends, which is the worst class to reintroduce.

## What has to be measured before deciding

- [ ] The **first** `Microsoft.Azure.Cosmos` 3.x version that translates the span-bound overload. Bisect the
      nuspecs / render offline with `ToQueryDefinition()`; no live service is needed, which
      [[TASK-220]] established
- [ ] Whether that version is at or below any floor the framework is willing to require. If the fix landed
      recently, `3.*` can still resolve an affected version and the wiring must stay
- [ ] Whether the rewrite is genuinely a no-op on a fixed SDK for **every** shape, not just `int[]` — the
      enum case takes the `IEqualityComparer<T>` overload and is the one most likely to differ
- [ ] Whether MongoDB is in the same position. Its driver was floated to `3.*` in the same change, and
      TASK-218's defect there was the same overload. **Do not assume it moved too** — measure it; that
      assumption is what TASK-220 caught TASK-218 making about Cosmos

## Decision to take

Retire the Cosmos wiring, keep it as belt-and-braces, or raise a minimum SDK floor and then retire it.
Record the reason either way. Note that keeping it costs a pre-pass on every Cosmos read, and
`SpanContains.Rewrite` already returns the predicate **by reference** when it finds no span node, so the
cost on a fixed SDK is one tree walk rather than a rebuild.

## Out of scope

- The rewrite itself, and its MongoDB wiring — both correct for the SDK versions they were measured
  against, and neither is a defect.
- The float decision — [[TASK-229]], taken by the user. This task is a consequence of it, not a challenge
  to it: the float is what revealed the upstream fix, which a pin would have hidden indefinitely.

## Human test plan

N/A — SQL rendering is observable offline.

## Implementation plan

_Populated by `/tasks plan TASK-233` — leave empty until then._
