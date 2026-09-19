---
id: TASK-469
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: human
created: 2026-09-19
depends-on: []
blocks: []
related: [EPIC-002, EPIC-003]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Decide whether Birko gets a Meilisearch backend — and if so, at which layer

Raised as an open question 2026-09-19. **Nothing is committed to; this task is the decision, not the
implementation.** Measured before filing: **0 references to Meilisearch** anywhere in the framework,
`Birko.Web`, or any of the 16 consumer repositories. (One apparent hit in `Symbio/.git/objects/` is a
false positive — a byte match inside a compressed object, not content.) So this is greenfield, with no
consumer currently asking for it.

## The real question is the LAYER, not the vendor

`Birko.Data.*` backends are **stores**: they implement `IStore` / `IAsyncStore` / `IBulkStore` /
`IAsyncBulkStore` and act as a system of record. A search engine is usually not that — it is an index
you mirror into and can rebuild. Three shapes are available and they differ by an order of magnitude in
cost:

| Shape | Precedent | Rough cost |
|---|---|---|
| **a. `Birko.Data.Meilisearch`** — a full store, peer of ElasticSearch | `Birko.Data.ElasticSearch` | the ES family is **7 production + 7 test** projects (store, `.ViewModel`, `.Views`, `Migrations.`, `Sync.`, `BackgroundJobs.`, `Workflow.`) |
| **b. `Birko.Data.Sync.Meilisearch`** — a sync *target* only; the record stays in SQL/Mongo | `Birko.Data.Sync.ElasticSearch` already exists | 1 + 1 projects |
| **c. A provider-neutral `Birko.Search`** with ElasticSearch and Meilisearch behind it | none — see below | largest, and the most valuable independent of Meilisearch |

## ⚠ There is no search abstraction today, and that is the finding behind the question

Measured: `SearchResult` and `HighlightOptions` exist **only** in
`Birko.Data.ElasticSearch/Highlighting/`. Nothing in `Birko.Data.Core`, `.Stores` or `.Patterns`
declares a search contract. So full-text search is not a framework capability with one provider — it is
an **ElasticSearch feature**, and a consumer that wants search is coupled to ES by construction.

That means a second search backend cannot be added cheaply in shape (a): it either duplicates that
surface under a second vendor's namespace — the "one rule, two implementations" shape § Conventions
keeps recording as the cause of defects — or it forces the extraction in shape (c). **Shape (c) is
worth doing on its own merits whether or not Meilisearch is ever added**, and if it is done first, (a)
and (b) both get cheaper. Consider splitting that out rather than letting it ride on a vendor decision.

## ⚠ This repo does not drain "add a backend" epics — check before opening a third

`EPIC-002` (Birko.Data.Redis) and `EPIC-003` (Birko.Caching.NCache) were both created **2026-05-28**
and are both still `planned`, each with a single `todo` P2 task — **~3.7 months untouched**. Filing a
Meilisearch epic before anyone needs it would make it the third. That is the reason this is a decision
task at P3 rather than an `EPIC-0NN` with an `Implement Birko.Data.Meilisearch` task under it.

## ⚠ And the demand signal currently points the wrong way

ElasticSearch's consumers are **Affiliate** (2 projects) and **Symbio**; `Birko.Sandbox` imports it as
part of importing everything, so it is not evidence of demand. **Affiliate is being retired onto Symbio
as a backend** (see `docs/consumers.md` § Lifecycle), so the live ES consumer base is shrinking toward
one. A second search backend is harder to justify while the first has one real consumer — unless the
argument is *replacement* rather than *addition*, which is a different and much bigger question.

## To verify before deciding — do NOT take these from memory

None of the following was measured; they are the claims the decision rests on and each needs checking
against current Meilisearch documentation and a spike:

- Is the official .NET SDK maintained, and does it target `net10.0`? What is its licence, and
  Meilisearch's own?
- Can it express the `IBulkStore` surface Birko requires — filter-based `Update(filter, …)` and
  `Delete(filter)`, ordering, limit/offset, and a bounded-filter guard (§ Conventions' scope-guard
  family applies to any new backend that can be asked to write "everything")?
- Does its filter language translate from a C# expression tree at all, and what does it do with the
  shapes that have bitten every other backend here — an empty `Contains`, `x => true`, a null-valued
  comparison? § TASK-137 / TASK-218 are the checklist.
- Can it serve as a system of record (durability, backup, transactional semantics), or is it
  index-only? If index-only, shape (a) is off the table regardless of anything else.

## Acceptance criteria

- [ ] A recorded decision: **yes at shape (a) / (b) / (c), or no**, with the reason — not left as
      "maybe later", which is what the two dormant epics already are
- [ ] If **no**: this task is `cancelled` with the reason, so the question is answered rather than
      reopened every few months
- [ ] If **yes**: the epic/story is created at the chosen layer, and `docs/consumers.md` or
      `CLAUDE-projects.md` gains the entry — plus a named consumer that will use it, since that is the
      thing EPIC-002 and EPIC-003 lack
- [ ] Either way, decide separately whether the `Birko.Search` extraction (shape c) is worth filing on
      its own, since the ES-only `SearchResult`/`HighlightOptions` coupling exists today and is not
      contingent on Meilisearch

## Out of scope

- Implementing anything. A spike to answer the verification list is in scope; a backend is not.
- Replacing ElasticSearch. If that is the actual intent, say so — it inverts the cost analysis above
  and is its own decision.
- `EPIC-002` / `EPIC-003`. They are cited as precedent for how these age, not reopened here.
