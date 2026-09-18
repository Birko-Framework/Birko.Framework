---
id: TASK-220
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-218, TASK-221]
pr: [Birko.Data.CosmosDB@e59e9dc, Birko.Data.CosmosDB.Tests@c9b55f1]
github-issue: null
jira-key: null
findings: []
---

# CosmosDB has the same array-`Contains` defect as MongoDB — audit the rest of the family

## Context

[[TASK-218]] fixed the .NET 9+ span-binding defect on MongoDB and closed with an explicit gap: CosmosDB
and RavenDB also forward raw expressions and were **not measured**, because they were assumed to need
live services. This task closes that gap, and the assumption turned out to be wrong — both are
measurable offline.

## Audit — which backends are even at risk

Classified by what each does with the caller's `Expression<Func<T, bool>>`:

| backend | handling | at risk? |
|---|---|---|
| InMemory, JSON, XML, **InfluxDB** | `filter.Compile()` | **No** — the delegate runs on the real runtime, which knows `MemoryExtensions.Contains` |
| SQL (4 providers), ElasticSearch | hand-rolled parser | **No** — measured in TASK-218; both evaluate the operand themselves |
| MongoDB | raw expression to driver | fixed in TASK-218 |
| **CosmosDB** | `GetItemLinqQueryable().Where(filter)` | **YES — this task** |
| **RavenDB** | `session.Query<T>().Where(filter)` | **No, but worse** — see [[TASK-221]] |

That is the whole store family; nothing else translates a filter expression.

## Measurement (2026-08-16)

Both offline. No emulator, no server — a `CosmosClient` renders SQL locally via `ToQueryDefinition()`,
and RavenDB's `DocumentStore.Initialize()` plus query *building* need no database.

**CosmosDB — affected, identically to MongoDB:**

| spelling | rendered |
|---|---|
| `int[] arr; x => arr.Contains(x.Amount)` | **`NotSupportedException: Specified method is not supported.`** |
| `List<int>` | `SELECT VALUE root FROM root WHERE (root["Amount"] IN (1, 5))` |
| `Enumerable.Contains(arr, x.Amount)` | same, correct |

**RavenDB — NOT the same defect:**

| spelling | rendered |
|---|---|
| `x => x.Amount > 3` (baseline, proves the probe) | `from 'Docs' where Amount > $p0` |
| `int[]` / `List<int>` / `Enumerable.Contains` | **all three: `NotSupportedException: Expression type not supported: TypedParameterExpression`** |
| `x => x.Amount.In(arr)` (Raven's own operator) | `from 'Docs' where Amount in ($p0)` |

Every spelling fails equally on Raven, so this rewrite would have turned one failure into an identical
one and looked like progress. Filed separately as [[TASK-221]] — Raven needs `.In()`, which is a
different fix and a real design question.

**This is the payoff of the "measure before wiring" rule**, and both halves of it fired in one pass: it
caught a backend that genuinely needed the fix *and* stopped the fix going into one that would not have
benefited.

## Acceptance criteria

- [x] Every store that translates rather than compiles a filter expression is classified, and the list is
      recorded here — not just the two TASK-218 happened to name
- [x] CosmosDB's array-backed `IN` renders the same SQL as the `List<T>` spelling
- [x] RavenDB is measured and the result acted on — wired, or excluded **with the reason recorded**.
      Excluded; [[TASK-221]] filed
- [x] A **non-gated** test pins the behaviour, and separately pins that the STORES apply it — the first
      version of these tests called the helper directly and so passed with every entry point unwired
- [x] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- [[TASK-221]]'s RavenDB set-membership gap.
- The gated `CosmosFilterMatrixLiveTests`, which still needs `BIRKO_COSMOS_CONNECTION` and remains unrun.

## Human test plan

- [ ] N/A — fully covered by automated tests, and unusually so for this family: the whole defect is a
      translation failure, and both the rendering and the store's use of it are asserted without a
      server. There is nothing a human could see that the tests do not.

## Outcome

**What was broken.** `x => arr.Contains(x.Amount)` over a C# array threw
`NotSupportedException: Specified method is not supported` on CosmosDB — the same .NET 9+
`MemoryExtensions.Contains` binding TASK-218 fixed for MongoDB, in the second of the two backends that
task left unmeasured.

**The fix.** `SpanContains.Rewrite` wired into the six CosmosDB entry points where a caller filter
arrives (three sync, three async). No filter-based `Delete`/`Update` overrides exist on these stores, so
the base class's read-then-loop is covered by the same six.

**The test gap I nearly shipped.** My first three tests called `SpanContains.Rewrite` inside their own
render helper — so they pinned the *helper*, and unwiring all six entry points broke **nothing**. Caught
by running the revert rather than trusting the green. Fixed by adding
`The_store_itself_gets_past_translation`, which drives the real store and distinguishes the two failure
*phases*: unwired it throws `NotSupportedException` at translation, before any I/O; wired it gets through
translation and fails reaching the network. Both stores, no server.

**Judgement calls.**

- **Excluded RavenDB despite it being the obvious symmetric move.** Measured first: every `Contains`
  spelling fails there, so the rewrite would have changed nothing while appearing to.
- **The Cosmos client's network is replaced with an always-failing `HttpMessageHandler`**, not merely
  pointed at a dead port. A dead port cost **~25s** of SDK retries for two tests; the handler makes it
  ~2s, deterministic, and incapable of reaching anything real. `RequestTimeout` and the retry options do
  not control that storm — measured before settling on the handler.
- **`OpenTcpConnectionTimeout` was tried and rejected** — the SDK throws
  `ArgumentException: requires ConnectionMode to be set to Direct`, which the Gateway mode this needs
  precludes.

**Flagged, not fixed.**

- **[[TASK-221]]** — RavenDB cannot translate any set-membership filter, and has no filter-matrix suite
  at all, unlike MongoDB and Cosmos. The missing suite is arguably the larger finding.
- `CosmosFilterMatrixLiveTests` is still gated on `BIRKO_COSMOS_CONNECTION` and has still never run —
  the same shape that hid this defect. Not addressed here; an emulator run is its own piece of work.

## Implementation plan

_Not drafted separately — the shape was settled by TASK-218; this task is its measured extension._

## Progress log

- step 2 — picked; user-directed follow-up on TASK-218's two flagged items, which are one investigation: the audit is what determines whether Cosmos and Raven are affected.
- step 3 — verified. Both measured OFFLINE, contradicting TASK-218's assumption that they needed live services. Cosmos affected identically; Raven affected by something else entirely. Full classification of the store family recorded above.
- step 4 — layer: local. The helper already exists in Birko.Data.Core (TASK-218); this is wiring in Birko.Data.CosmosDB only.
- step 5 — fix in Birko.Data.CosmosDB/Stores/{CosmosDBStore.cs, AsyncCosmosDBStore.cs} (6 entry points); tests in Birko.Data.CosmosDB.Tests/CosmosSpanContainsTests.cs (new, 5, non-gated, ~2s).
- step 6 — revert of the six wirings: 2 of 49 failed = The_store_itself_gets_past_translation(sync) and (async). Contract pins, passing either way: the three render tests (they call the helper directly and pin the HELPER — the first version of this suite had ONLY those, and the revert passed 47/47, which is how the gap was found), plus all 36 RavenDB, 59 Core, 85 MongoDB, 500 SQL, 129 ElasticSearch and 69 InMemory tests — the non-Cosmos suites being the evidence the wiring stayed where it was measured to belong.
- step 7 — respecced filter-expression-translation: the TASK-218 requirement's wiring sentence now names CosmosDB too and records the audit's classification, including why Raven is excluded.
- step 8 — closed done; e59e9dc (production) / c9b55f1 (tests). Merge gate: builds warning-clean; no new cross-cutting pattern introduced (this reuses TASK-218's, whose § Conventions rule already says "wire it where it actually breaks" — the audit is that rule being exercised, and the CLAUDE.md entry was extended with the classification rather than duplicated). security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface. Human test plan resolved as N/A with the reason, not left blank.
