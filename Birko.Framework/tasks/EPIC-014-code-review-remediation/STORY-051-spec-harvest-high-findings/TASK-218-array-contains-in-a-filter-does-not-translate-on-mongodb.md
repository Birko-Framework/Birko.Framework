---
id: TASK-218
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-214, TASK-212]
pr: null
github-issue: null
jira-key: null
findings: []
---

# An `IN` filter over a C# **array** does not translate on MongoDB — `NotSupportedException`

## Context

Found by [[TASK-214]] (2026-08-16). Not that task's root cause — filed rather than folded in, because the
mechanism is expression binding, not serialization.

`MongoFilterMatrixLiveTests` is gated on `BIRKO_MONGO_HOST` and, per TASK-214's own write-up, **had never
run in this environment**. TASK-214 fixed the serialization defect that stopped every write, which let the
suite reach its assertion for the first time. It reports **26 of 27** filter shapes matching the
compiled-delegate oracle. The one divergence:

```
inClosure      -> THROW NotSupportedException: Specified method is not supported.
```

for the shape `x => amounts.Contains(x.Amount)` where `amounts` is `int[]`.

**Characterised offline** (no server needed — `Builders<M>.Filter.Where(e).Render(...)`, driver 3.2.0,
.NET 10):

| written as | renders |
|---|---|
| `int[] arr; x => arr.Contains(x.Amount)` | **`NotSupportedException: Specified method is not supported.`** |
| `List<int> list; x => list.Contains(x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |
| `IEnumerable<int> seq; x => seq.Contains(x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |
| `x => Enumerable.Contains(arr, x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |

Only the *instance-style call on an array* fails, and the explicit `Enumerable.Contains(arr, …)` form over
the **same array** succeeds — so it is not the array, it is which method the compiler binds. On .NET 9+ an
array's `.Contains(x)` binds to `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` rather than to
`Enumerable.Contains`, and the driver's LINQ translator does not know that method.

**This framework already knows about this binding.** `Birko.Data.Expressions.PredicateScope` documents and
handles it explicitly — "On .NET 9+ an ARRAY `set.Contains(x.Col)` binds to
`MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)` … so the collection arrives wrapped
in an implicit ReadOnlySpan conversion" — because `RequireBoundedFilter` had to see through it. So the
unbounded-filter guard is **not** affected. What is affected is the *translation* on backends that forward
the raw expression to a driver.

## Why this is worth a task rather than a note

- It is a **silent-at-compile-time, loud-at-runtime** trap with a working look-alike one keystroke away:
  `new[] { … }` fails, `new List<int> { … }` succeeds, and nothing warns.
- The obvious workaround (`.ToList()`) is invisible unless you already know, and the exception message —
  *"Specified method is not supported"* — names no method.
- MongoDB has **no hand-rolled parser**; the expression goes straight to the driver. Backends that *do*
  parse (SQL, ElasticSearch) may or may not recognise the span binding — **unmeasured, and that is the
  point of the first acceptance row.** Do not assume it is MongoDB-only.

## Approach

1. **Measure the other backends before choosing a fix.** Run the same four shapes through the SQL
   connector's `ParseConditionExpression` and ElasticSearch's translator. A defect present in one backend
   argues for a per-backend fix; present in several argues for normalising the expression once, upstream of
   all of them.
2. Candidate fixes, cheapest first:
   - **Document it** in `Birko.Data.MongoDB/CLAUDE.md` and let it throw. Honest, but leaves a trap.
   - **Normalise the expression** — rewrite a `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` node to the
     equivalent `Enumerable.Contains(IEnumerable<T>, T)` before handing it to the driver. `PredicateScope`
     already unwraps the implicit span conversion, so the detection half exists and should be **reused,
     not re-derived** (the one-producer rule in CLAUDE.md § Conventions).
   - Reject shape-by-shape special-casing in each backend — that is how this family of defects spreads.
3. Whichever is chosen, the matrix suite is the regression: it already covers the shape and already fails.

## Acceptance criteria

- [ ] The four `Contains` bindings are measured against SQL and ElasticSearch as well as MongoDB, and the
      result recorded here — this decides whether the fix is per-backend or shared
- [ ] `x => arr.Contains(x.Member)` over an array either translates correctly on every backend that
      forwards expressions, or throws an exception that **names the binding and the workaround** rather
      than `Specified method is not supported`
- [ ] `MongoFilterMatrixLiveTests` reports 27 of 27, or its `inClosure` row is replaced by an assertion
      that pins the documented refusal — not deleted
- [ ] A **non-gated** test pins the chosen behaviour; the render check needs no server (see the table above)
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- `PredicateScope` / `RequireBoundedFilter`. It already handles the span binding and was re-read to confirm
  it; changing it is not part of this.
- [[TASK-214]]'s serialization fix, which is what made this reachable.

## Human test plan

- [ ] With a live MongoDB, run `Birko.Data.MongoDB.Tests` and confirm the matrix suite's report — the value
      of this suite is that it prints all 27 shapes, so a fix that breaks a different shape is visible in
      the same output.

## Implementation plan

_Populated by `/tasks plan TASK-218` — leave empty until then._
