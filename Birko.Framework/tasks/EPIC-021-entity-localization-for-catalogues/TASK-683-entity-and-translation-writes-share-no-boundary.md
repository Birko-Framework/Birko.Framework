---
id: TASK-683
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-19
depends-on: [TASK-680]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-M083]
pr: null
github-issue: null
jira-key: null
---

# The entity write and its translation write share no boundary, so a failure desynchronises them

## Context

Spawned from [[TASK-168]] (SH-M083, confirmed 2026-10-19). Every write in `Birko.Data.Localization` is two
independent store calls: the inner store's create/update/delete, then the translation rows
(`LocalizedTranslationRows.Save`, `DeleteTranslations`). The bulk paths loop per entity after one inner bulk
call. Nothing joins them:

- a throw in the second half leaves the entity written with no translation, or **deleted with its rows orphaned
  forever** (`DeleteTranslations` is the only deletion site, so nothing cleans them up);
- a bulk call that fails part-way reports nothing about the entities already processed.
- **found at TASK-168's close review:** when the action throws part-way through a filter-based update, the two
  wrappers differ. The sync wrapper writes its pending translation rows only after `_innerStore.Update` returns,
  so entities the inner store already persisted earlier in the call lose their translation change; the async
  wrapper writes each entity's rows right after its own update, so it keeps them. Flushing the sync list in a
  `finally` is not the fix: it would also write rows for an entity whose inner write is the one that failed
  (SH-M075). Decide it with the boundary.
- **also from that review:** the inner bulk store skips a row deleted concurrently (`EntityNotFoundException`),
  but the sync filter update has already queued that row's translations and writes them, leaving orphans; the
  async filter update calls `UpdateAsync` per row and throws on such a row after earlier rows were written,
  where the default-culture path skips it and finishes.

TASK-168 fixed one ordering case (SH-M075: a filter-based update wrote its rows *before* the inner write, so they
outlived a failed update). The rows are now written after the inner write on every path, so the remaining gap is
a failure **after** the inner write succeeds.

Too large for the triage: it needs a decision about how the wrappers take part in a boundary. The framework has
`ITransactionalStore` (`Birko.Data.Stores`), `IUnitOfWork` (`Birko.Data.Patterns`), and TASK-669's
`AmbientSqlTransaction` for SQL stores sharing a connection. Rule 6 applies: a write that opens its own
connection cannot be inside anybody's transaction, and a boundary is only as wide as its narrowest participant.
It depends on TASK-680, which decides the translation model and may put both tables on one SQL store.

## Acceptance criteria

- [ ] Decide and write down how the wrappers join a boundary: participate in an ambient transaction when both
      stores are on one SQL connection, and what they do otherwise (refuse, compensate, or document as best effort)
- [ ] On a SQL provider with both tables on one connection, a translation write that throws after the entity
      write rolls back the entity write, asserted on a real database and proven by removing the participation
- [ ] A delete whose translation cleanup fails leaves no orphaned rows (rolled back, or reported and retried)
- [ ] The README states which store pairings are atomic and which are not

## Out of scope

- The unique key and model shape: [[TASK-680]].
- Batching the reads: [[TASK-679]].

## Human test plan

N/A — library contract, covered by automated tests against a real database.

## Implementation plan

_Populated by `/tasks plan TASK-683` — leave empty until then._
