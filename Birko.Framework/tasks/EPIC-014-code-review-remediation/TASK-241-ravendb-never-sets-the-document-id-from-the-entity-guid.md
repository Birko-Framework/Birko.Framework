---
id: TASK-241
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-240, TASK-219]
findings: []
pr: null
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

## Suggested shape

Make the entity `Guid` **be** the document id, the way TASK-219 made it be MongoDB's `_id` — one producer,
so the reader and the writer cannot disagree. Either register a Raven `DocumentStore.Conventions`
id-generation/`FindIdentityProperty` rule for `AbstractModel`, or pass the id explicitly at every
`StoreAsync(data, id, ct)` call. Prefer the convention: it is one registration at the funnel where the
`DocumentStore` is created, and it cannot be missed by a new write path.

⚠ **Check for stored data before changing an id layout.** TASK-219 could change MongoDB's freely because
it had just proved no write had ever succeeded. That is NOT the case here — Raven writes *do* land, so any
existing deployment has documents under auto-generated ids that a convention change would orphan. Measure
whether a real consumer has such data before choosing between a convention change and a migration.

## Acceptance criteria

- [ ] `CreateAsync(e)` then `ReadAsync(e.Guid)` returns the entity, against a live RavenDB.
- [ ] `DeleteAsync(entity)` actually deletes; a delete that matches nothing is not reported as success.
- [ ] `UpdateAsync(entity)` replaces rather than duplicating.
- [ ] The sync `RavenDBStore` gets the same treatment — guard the whole verb family or none of it.
- [ ] Revisit `RavenDbUnitOfWork.Capabilities.ReadsSeeUncommittedWrites`. TASK-240's
      `A_query_inside_the_session_does_not_see_the_sessions_unsaved_writes` deliberately pins today's
      behaviour, so it will fail once Load-by-id works and force the declaration to be reconsidered rather
      than left as a quiet lie.
- [ ] Migration decision recorded, per the warning above.
