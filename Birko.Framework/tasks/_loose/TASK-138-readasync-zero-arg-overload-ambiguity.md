---
id: TASK-138
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-08-03
depends-on: []
blocks: []
related: [TASK-109]
pr: null
github-issue: null
jira-key: null
findings: []
---

# `ReadAsync()` with no arguments does not compile — CS0121 between the read-all and filtered overloads

## Context

Hit while writing [[TASK-113]]'s regression suite (2026-08-03) and worked around in **six** call sites, then
raised again while planning [[TASK-109]].

`../Birko.Data.Stores/AbstractAsyncBulkStore.cs` declares two overloads that are **both callable with zero
arguments**, because every parameter of each has a default:

```csharp
public virtual async Task<IEnumerable<T>> ReadAsync(
    Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null,
    int? limit = null, int? offset = null, CancellationToken ct = default)   // :50

public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)   // :72
```

So the natural way to read everything — `await store.ReadAsync()` — is a **compile error**:

```
error CS0121: The call is ambiguous between ... ReadAsync(Expression<Func<T,bool>>?, OrderBy<T>?, int?, int?,
CancellationToken) and ... ReadAsync(CancellationToken)
```

The workaround is `ReadAsync(CancellationToken.None)`, which reads as "pass no cancellation" when the intent
is "read everything" — the token is incidental and the reader has to know it is only there to disambiguate.
The sync side has the same overload pair (`Read(filter = null, …)` `AbstractBulkStore.cs:31` and `Read()`
`:43`) but **no ambiguity**, because the read-all overload is genuinely parameterless — so this bites the
async path only.

Not a correctness defect: everything works once written the long way. It is a papercut on the single most
common call in the store API, in a base class **8 backends inherit** (CosmosDB, ElasticSearch, InMemory,
InfluxDB, JSON, MongoDB, RavenDB, XML), so every consumer of every one of them meets it.

## Prior decision this must not re-open

While planning TASK-109 a `ReadAll()` alias was **rejected on symmetry grounds** — it would have been a second
name for behaviour `Read()` already provides, added only so the destructive `DeleteAll()` / `UpdateAll()` API
would look symmetric. That reasoning still stands, and the naming asymmetry is deliberate (read-all is the
short parameterless overload because reading everything is harmless; a destructive all-rows operation gets the
louder `*All` name, and `Delete()` must never be spelled parameterless).

**This task has a different justification: the ambiguity, not the symmetry.** A fix that happens to be named
`ReadAllAsync` is acceptable *because it resolves CS0121*, not because it matches `DeleteAll`. Anyone revisiting
this should not conclude the symmetry argument was accepted after all.

## Approach — three options, none obviously right

1. **Add `ReadAllAsync(CancellationToken ct = default)`** as a thin, unambiguous alias delegating to
   `ReadAsync(ct)`. Non-breaking and additive. Cost: a third name in the read family, and the `ReadAsync()`
   ambiguity still exists for anyone who tries it — this adds a working door rather than fixing the broken one.
2. **Remove the default from the filtered overload's first parameter** (`filter` becomes required). Kills the
   ambiguity at the root and leaves exactly one way to say "read everything". **Breaking** for any caller
   writing `ReadAsync(orderBy: …)` or similar named-argument forms that rely on the `filter` default — needs a
   sweep of the framework, all 8 backends and the consumer solutions before it can be judged.
3. **Document it and move on.** `ReadAsync(CancellationToken.None)` works; the cost is one confusing line per
   call site and an XML-doc note. Legitimate if 1 and 2 both prove worse than the papercut.

Decide by measuring option 2's blast radius first — if nothing relies on the `filter` default, option 2 is
strictly the best answer and options 1 and 3 are unnecessary.

## Acceptance criteria

- [ ] `await store.ReadAsync()` either compiles and reads everything, or the chosen alternative is documented
      on both overloads' XML docs so the next caller does not rediscover CS0121
- [ ] Option 2's blast radius is **measured and recorded as a number** — how many call sites across the
      framework, the 8 inheriting backends and the consumer solutions rely on the `filter` default — before
      the option is chosen or dismissed
- [ ] Whichever option lands, the sync/async read-all pair stays consistent in *naming*, and the
      read-vs-destructive naming asymmetry recorded by [[TASK-109]] is preserved (this task must not
      re-introduce a `*All` read alias as a symmetry play)
- [ ] No behaviour change to what a read-all returns, on any backend
- [ ] The six `ReadAsync(CancellationToken.None)` workarounds in
      `Birko.Data.Sync.Tenant.Tests/TenantSyncScopeTests.cs` are simplified if the fix allows it — they are
      the motivating call sites
- [ ] Tests covering the chosen form on at least the InMemory backend (the canonical test double)

## Out of scope

- Adding a `ReadAll()` alias for API symmetry with `DeleteAll()` — explicitly rejected, see above.
- Any change to what a null filter *means* on a read (read-everything is a documented API; [[TASK-109]]
  criterion 6 pins it).
- The destructive-path guards — that is [[TASK-109]].

## Human test plan

N/A — covered by automated tests, and the compiler is the primary oracle: if `ReadAsync()` compiles, the
ambiguity is gone.
