---
id: TASK-834
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB: an `ids.Contains(x.Col)` filter has a query-text ceiling

## Context

Found by TASK-833's survey (2026-11-07), from reading the code; not measured. The user chose to file it separately.

- Cosmos DB's LINQ provider writes the list into an `IN (...)` in the **query text**. It is not a parameter.
- So the ceiling is Cosmos DB's 512 KB query-text limit, about 13k Guids.
- Past it the query fails loudly, so this is not a silent wrong answer.
- TASK-833 chunks the SQL providers; this is the Cosmos DB counterpart.

## Decision (the user, 2026-11-07)

**Split, like TASK-833.**
- Split only when the IN is a plain AND conjunct over non-string values, with no sort or paging. Refuse otherwise,
  with `NotSupportedException` naming the reason, before any request.
- Count sums the chunks.
- **Writes run chunk by chunk.** Cosmos DB has no multi-statement transaction outside one partition, so a chunk that
  fails is reported, not rolled back: the exception names how many chunks applied. This is accepted, and stated in the
  docs.

## Acceptance criteria

- [x] Measured against the Cosmos DB emulator (live suite per `live-tests.yml`): the largest Guid list a filtered
      read accepts, and the error past it.
      → Measured 2026-11-07 on vnext-preview (Gateway):
        - 13,105 Guids pass and 13,106 fail. The query text is 52 + 40 characters per Guid: 524,252 passes, 524,292
          fails.
        - The error is `CosmosException` 400 BadRequest, SC3020: "The SQL query text exceeded the maximum limit of
          524288 characters."
- [x] Either a list past the ceiling is split transparently, with the same rules as TASK-833 (IN only as an AND
      conjunct, non-string values, no sort or paging, one boundary for writes where Cosmos DB has one), or it is refused
      before the request with an exception naming the reason. The choice is the user's, recorded here.
      → Split (the decision above). `CosmosInListChunking` (new) plans it from the raw expression, since Cosmos DB has
        no translator of its own and the SDK's LINQ provider renders the text.
      → What is chunked, sync and async: single read, bulk read and count. Filtered update and delete are overridden
        to run chunk by chunk; each first calls `RequireBoundedFilter`, and each chunk goes through the base path,
        which is guarded again.
      → Chunk size 1000 (about 40 KB of text). `InListChunkSize` and `MaxQueryTextLength` are `protected virtual`.
      → Count is the sum of the chunks.
      → A shape that cannot be split still runs as one query while its text fits. Past the ceiling it gets
        `NotSupportedException` before any request; the query is rendered locally with `ToQueryDefinition`. The
        message names the operation, count, text length, limit and reason, never the values.
      → A write that fails at chunk 2 or later throws `CosmosChunkedWriteException` (`ChunksApplied`, `ChunkCount`,
        the original as inner). Nothing is rolled back, as decided.
      → **Deviation, accepted:** a failure in the first chunk, and a cancellation, are rethrown unwrapped, so the
        exception type does not change where wrapping adds nothing (rule 9). The failing chunk may be partly applied,
        and the message says so.
- [x] Tests pin whichever was chosen.
      → `Task834InListChunkingTests`: 33 live tests, including read, count and delete with 20,000 ids.
      → Every guard was mutated, run red and restored: dedupe, split, refusal, AND-conjunct check, sort/paging,
        chunks-applied wrapping, first-chunk text check, and each of the 4 write overrides.
      → Live suites green: CosmosDB 192, ViewModel 5, Views 40, Migrations 44, Sync 4, BackgroundJobs 28, Workflow 8.

## Out of scope

- The SQL providers (TASK-833).
- `Birko.Data.CosmosDB.Views/CosmosViewStore.cs` builds its own LINQ query and is not chunked. It goes to the views
  task filed with TASK-833, which leaves SQL views unwired the same way.

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
