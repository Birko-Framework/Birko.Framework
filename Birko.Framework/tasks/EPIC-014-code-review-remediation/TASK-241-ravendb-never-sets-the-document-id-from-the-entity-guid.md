---
id: TASK-241
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-240, TASK-219]
findings: []
pr: "Birko.Data.RavenDB fd51546 · Birko.Data.RavenDB.Tests 500e94a"
github-issue: null
jira-key: null
affects: [Birko.Data.RavenDB]
---

# RavenDB never sets the document id from the entity Guid — delete is a silent no-op and update duplicates

## How this was found

Spawned from [[TASK-240]], while writing RavenDB's per-provider transaction proof. The transaction test
`A_read_inside_the_session_sees_the_sessions_own_uncommitted_writes` failed, and the cause turned out to
have nothing to do with transactions.

**Measured against a live RavenDB 7.2** (`ravendb/ravendb:latest`, unsecured, single node), outside any
transaction:

| probe | expected | measured |
|---|---|---|
| `CreateAsync(e)` then `ReadAsync(e.Guid)` | the entity | **`null`** |
| `CreateAsync(e)` then `DeleteAsync(e)` then count | 0 | **1 — the delete is a silent no-op** |
| `CreateAsync(e)` then `UpdateAsync(e')` then count | 1 | **2 — update INSERTS a second document** |

The document is stored: `ReadAsync()` returns it with the correct `Guid`. Only addressing it by id fails.

## The defect

`AsyncRavenDBStore` (and its sync twin) has **two answers for what a document's id is**, exactly the
family [[TASK-219]] fixed for MongoDB:

- The **write** path calls `session.StoreAsync(data)` with no id
  (`AsyncRavenDBStore.cs:174`, `:179`, `:263`, `:344`, `:438`), so Raven's default convention generates
  one from the collection name — `TxDocs/1-A`.
- Every **address-by-id** path asks for the raw Guid string:
  `LoadAsync<T>(guid.ToString())` (`:199`, `:203`, `:246`, `:256`, `:479`) and
  `Delete(data.Guid.Value.ToString())` (`:277`, `:282`, `:520`).

Those never match, so:

- `ReadAsync(Guid)` returns `null` for a document that exists.
- `DeleteAsync(entity)` / `DeleteAsync(items)` delete nothing and **report success**.
- `UpdateAsync(entity)` finds no existing document, so its `StoreAsync` creates a **duplicate** rather
  than replacing.

**Severity.** The silent no-op delete is the worst of the three by the standing rule that a backend which
silently drops what it was asked to do outranks any number of loud refusals: a caller deleting a customer
record gets no exception and no deletion. The duplicating update is a close second — it corrupts the
collection rather than merely failing.

## What it is NOT

Not a transaction defect, and not caused by TASK-240 — it reproduces with no unit of work anywhere in the
picture. TASK-240 only made it visible, and left one consequence recorded in the contract:
`RavenDbUnitOfWork.Capabilities.ReadsSeeUncommittedWrites` is declared **false**, because the only read
that could consult the session's identity map is Load-by-id, and Load-by-id does not work.

## Suggested shape (as filed — ⚠ its recommendation was WRONG, see § What shipped)

Make the entity `Guid` **be** the document id, the way TASK-219 made it be MongoDB's `_id` — one producer,
so the reader and the writer cannot disagree. Either register a Raven `DocumentStore.Conventions`
id-generation/`FindIdentityProperty` rule for `AbstractModel`, or pass the id explicitly at every
`StoreAsync(data, id, ct)` call. Prefer the convention: it is one registration at the funnel where the
`DocumentStore` is created, and it cannot be missed by a new write path.

**Kept because the way it was wrong is the lesson.** The one-producer goal was right and is what shipped;
the *mechanism* it recommended is unusable, and only measuring showed it. Raven freezes conventions on
`DocumentStore.Initialize()` and throws on any later change, while both stores accept an
externally-supplied, already-initialised `IDocumentStore` — so the "cannot be missed" property would have
been bought by silently skipping exactly the constructor a consumer with its own `DocumentStore` uses.

⚠ **Check for stored data before changing an id layout.** TASK-219 could change MongoDB's freely because
it had just proved no write had ever succeeded. That is NOT the case here — Raven writes *do* land, so any
existing deployment has documents under auto-generated ids that a convention change would orphan. Measure
whether a real consumer has such data before choosing between a convention change and a migration.

## Acceptance criteria

- [x] `CreateAsync(e)` then `ReadAsync(e.Guid)` returns the entity, against a live RavenDB 7.2.
- [x] `DeleteAsync(entity)` actually deletes, and deleting one entity leaves the others alone.
- [x] `UpdateAsync(entity)` replaces rather than duplicating; `SaveAsync` upserts.
- [x] The sync `RavenDBStore` got the same treatment **and its own end-to-end test** -- a parallel fix
      nothing exercises is the trap this framework has been bitten by before.
- [x] The bulk paths too (`BulkInsert` is a different API and had the identical defect).
- [x] `RavenDbUnitOfWork.Capabilities.ReadsSeeUncommittedWrites` revisited -- see below.
- [x] Migration decision recorded, per the warning above.

## What shipped

`RavenDocumentId.For(store, entityType, guid)` is the **single producer** of a document id, and all
nineteen id-addressing sites across both stores route through it via a per-store `IdOf(guid)`. The id is
`{collection}/{guid}`.

### Two design choices, both forced by a measurement

- **Explicit id at every write site, NOT a Raven id convention.** The task file suggested the convention
  (one registration at a funnel, cannot be missed by a new write path) and that reasoning is right in
  general. It is unusable here: conventions **freeze** on `DocumentStore.Initialize()` and Raven *throws*
  on any later change -- measured, `InvalidOperationException: Conventions has frozen after
  'DocumentStore.Initialize()'` -- and both stores accept an externally-supplied, already-initialised
  `IDocumentStore`. A convention-based fix would have had to be skipped for exactly the constructor a
  consumer with its own `DocumentStore` uses, which is the silent half-fix this defect is made of.
  The cost is recorded honestly: unlike a convention this cannot enforce itself, so a new write path must
  remember to pass the id, and the round-trip tests are what would catch a miss.
- **`{collection}/{guid}`, not the bare guid** the reads already used. Two entity types deliberately
  sharing one identity -- a `User` and its `UserProfile` keyed by the same `Guid` -- is an ordinary
  modelling pattern, and a bare-guid id would let the second **silently overwrite** the first: the same
  class of silent data loss this task is about. The prefix costs nothing and matches what Raven's own
  conventions produce. It is not a hypothetical: dropping it fails
  `Two_types_sharing_one_Guid_do_not_overwrite_each_other`.

### The migration objection, measured away

Changing an id layout normally costs a migration, and this one could not lean on TASK-219's escape (there,
no write had ever succeeded). Measured instead: **no consumer uses RavenDB.** Symbio is the only repo that
references these stores, and its configured provider is SQLite with no module overrides. So there is no
stored data to orphan. **That window closes the moment a consumer stores data on Raven** -- recorded in
`Birko.Data.RavenDB/CLAUDE.md` so the next person checks rather than assumes.

### The capability, revisited as TASK-240 required

`ReadsSeeUncommittedWrites` **stays `false`**, and the reasoning changed rather than the value:

- `ReadAsync(Guid)` -- Load-by-id -- **now does** see the session's unsaved writes, because the identity
  map can finally find the document. Pinned by
  `Load_by_id_inside_a_session_now_sees_the_sessions_unsaved_write`.
- Every **query-based** read still does not: `ReadAsync(filter)`, `Read()`, `ReadFirst` and `Count` are
  answered by the server from indexes, which know nothing about an unsaved session. That is a RavenDB
  property, not a defect, and is not fixable from this side.

A single bool cannot say "id yes, query no", so it states the answer a caller is unsafe to get wrong --
query reads being the common case. TASK-240's interlock test correctly did **not** fire, because what it
pinned was the query half.

## Results

**Green:** 111 tests across 7 Raven-related suites against a live RavenDB 7.2, and Symbio **1972/1972**.
New: `RavenDocumentIdLiveTests` (11 -- 10 async, 1 sync).

**Mutation tests (revert -> red -> restore by reversing the exact substitution -> green):**

| revert | what it undoes | split |
|---|---|---|
| A | collection prefix dropped (bare guid id) | **2 of 66** |
| B | id unwired from the nine write sites (the filed defect) | **8 of 66** |

Revert B reproduces all three filed symptoms plus the bulk, upsert and in-session variants, which is the
evidence that the fix covers the whole verb family rather than the three methods the finding named.

## Found on the way, and fixed here

`SaveAsync` threw `DatabaseDoesNotExistException` when it was the first operation on a store: it overrides
the **public** wrapper and so must run the lazy-init gate itself, which it did not. Identical to the slip
CR-H077 fixed for `ReadAsync(Guid)` in the same class, unrelated to document ids, and one line inside a
method this change already touched -- so fixed rather than filed. It was found because the upsert
regression test needed it; working around it would have hidden a real defect.
