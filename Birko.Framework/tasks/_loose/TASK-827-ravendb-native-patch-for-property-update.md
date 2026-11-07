---
id: TASK-827
parent: null
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-07
depends-on: [TASK-826]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: translate `PropertyUpdate<T>` to a server-side patch instead of read-modify-save

Split from [[TASK-826]] on the user's word (2026-11-07): investigate first, then decide.

## Context

`Birko.Data.RavenDB` has no native `Update(filter, PropertyUpdate<T>)`: it falls back to `PropertyUpdate.ApplyTo`
(read each match, apply in C#, save), which is **not atomic**. That covers `Increment` (TASK-498) and, after
TASK-826, a computed set. RavenDB has a set-based patch (`PatchByQueryOperation`, RQL `from X where … update { … }`)
that runs JavaScript on the server, atomically per document.

## The question to measure before building

- The patch runs in RavenDB's JavaScript engine (Jint), where a number is a **double**. A constant `Set` and an
  integer `Increment` translate safely; a `decimal` increment or a computed set (`gross / 1.2`, rounded half to even)
  would not reliably equal C#'s `decimal` result to the cent.
- Does RavenDB 6's engine support `BigInt` (scaled-integer exact arithmetic)? And how does a `decimal` round-trip
  through the stored JSON number (`LazyNumberValue`) and back to the C# client?
- Is the filter expressible as RQL by the store's existing query translation, so the patch scopes exactly what a
  read would (tenant, soft-delete)?

## Acceptance criteria

- [x] The three questions above answered by measurement on the live container (`birko-live-raven`, RavenDB 6.0),
      recorded here.
- [x] Then, per the answer: a native patch for the kinds it can compute exactly, the C# fallback kept for the rest,
      and the TASK-826 cross-provider contract still passing on RavenDB; or a recorded decision to stay on the fallback.

## Human test plan

N/A — a library change verified by the live RavenDB suites.

## Measurements (2026-11-07, live RavenDB 6.0 container, client 7.2)

Probe: `PatchByQueryOperation` with RQL `from 'PDocs' … update { … }` on documents a `RavenDBStore` wrote.

| Question | Answer |
|---|---|
| `BigInt` in the patch engine? | **Yes.** `typeof BigInt` is `function`; `BigInt(15) * BigInt(10)` gives `150`. |
| A stored `decimal` in patch JS | **A double.** `0.15` reads `0.15` and `/ 1.2` gives `0.125`. `98765432109876543.21` reads `98765432109876544`; `0.1 + 0.2` gives `0.30000000000000004`. |
| The exact stored text in JS? | **Not reachable.** `JSON.stringify(this)` shows the double too. |
| A decimal the patch writes | **Goes through a double.** `this.Out = this.Wide` (a plain copy) stored `98765432109876540`. |
| A field the patch does not touch | **Stays exact.** Patching `Note` left `Wide` at `98765432109876543.21`. |
| A JS string written into a `decimal` field | The client reads `'0.12'` back as `0.12m`, but the stored JSON is then a string, which breaks range queries and sorting on the field. Not usable. |
| The LINQ filter as RQL | **Yes.** `Query<T>().Where(x => x.Code == "x" && x.Price > 0.1m)` gives `from 'PDocs' where Code = $p0 and Price > $p1`. The parameters come from the document query's `IndexQuery`. |

Consequences:

- **Exactness.** A patch computes a `decimal` exactly only while every value it reads or writes fits a double's
  ~15 significant digits (`String(x)` → BigInt scaled integers → back). A wider value is silently wrong, and the patch
  cannot detect it, because it never sees the stored text. A `decimal` `Set` constant goes through a double the same way.
  Strings, Guids, dates, `int` / `short` increments and `long` increments below 2^53 are safe.
- **Unit of work** (from the client's design, not measured). `PatchByQueryOperation` is a server operation outside the
  session, so it cannot join a `RavenDbUnitOfWork`. A native path would have to fall back to the session while one is
  active.
- **Atomicity.** Per document. Across documents it is a background operation, like Elasticsearch's UpdateByQuery.
  `QueryOperationOptions.AllowStale = false` makes it wait for the index first.

## Outcome (2026-11-07)

**Decision (the user, 2026-11-07): native for the safe kinds.**

- New `RavenPropertyUpdatePatch.TryBuild` builds one patch script when every assignment is exact in a double:
  - a `Set` of a string, bool, Guid, `DateTime` / `DateTimeOffset` / `TimeSpan`, char, enum, or an integer
    (`long` within 2^53);
  - an `Increment` of `short`, `int` or `long`, range-checked in the script. On failure it throws a marker, which the
    store reports as `OverflowException`.
- Anything else returns `null`, and the store keeps the exact C# path: a decimal / double / float value, a computed set,
  a non-primitive `Set`, or a session given to the store (a unit of work).
- Both stores override `Update(filter, PropertyUpdate)` and `UpdateAll(PropertyUpdate)`. They read the matching ids,
  defer one `PatchCommandData` per document into a fresh session (with the TASK-828 index wait), and `SaveChanges` once.

**Why deferred patches, not `PatchByQueryOperation`** (the mechanism probed above): deferred patches are one
transaction across all documents, join the session machinery (index wait), and are as exact. A patch-by-query is a
background operation outside the session and atomic only per document. The matching ids come from a read just before,
as on the C# path; the increment itself runs on the server, so no concurrent increment is lost.

**Measured.** `Task827PatchTests`, live, 8 tests. With `TryBuild` forced to `null` (everything on the C# path), 4 turn
red:

- 8×10 parallel increments, sync and async, lost some (read-modify-save);
- an overflow on one of two documents left the other incremented (the C# path writes per document; the patch batch
  writes nothing);
- a `long` at 2^53+1 incremented silently instead of being refused.

The other 4 are pins that hold on either path: sets of Guid / `DateTime` / enum and a `long` increment read back exactly,
a decimal increment and a wide decimal stay exact, a unit of work's rollback discards the update, and `UpdateAll`.
With decimals let through to the patch, the decimal test turns red: `98765432109876540.0` instead of
`…543.51`.

**Regression.** All 8 RavenDB importer suites are green (RavenDB 167).

**Accepted limitation (the user's option).** A `long` counter past 2^53 cannot be incremented on RavenDB
(`OverflowException`), where the C# path would have done it.
