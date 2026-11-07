---
id: TASK-842
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-841]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB chunks are sized by serialized length, not by a fixed count of 1000 values

## Context

Found by TASK-841 (2026-11-07). `CosmosInListChunking` splits a list into chunks of `InListChunkSize` (1000) values.
- That size suits Guids: about 40 characters each, so about 40 KB per chunk, against a 524,288-character limit that
  counts the query text plus the serialized values (measured at TASK-834 and TASK-839).
- Since TASK-841, string lists are split too. 1000 long strings (over about 500 characters each) put a single chunk
  past the limit, and the planner then refuses the read by name rather than splitting it smaller.
- The failure is loud, and a refusal that names its reason is not a wrong answer; but the list could be answered.

## Acceptance criteria

- [x] Chunks are cut by serialized length: each chunk's query text plus its values stays under the limit with a
      margin. The value count per chunk is a ceiling, not the only rule.
      → Reproduced first (emulator, 2026-11-07):
        - 1,200 distinct 600-character strings made a 1000-value chunk of 604,052 characters. It was refused with a
          **wrong reason** ("even one chunk is over the limit: the rest of the filter … is too long").
        - A 2-value list holding one 600,000-character string was not refused at all; Cosmos DB answered 400 SC3020.
        - Probe: the limit counts characters, not UTF-8 bytes. 524,253 characters passed and 524,453 failed.
      → `CosmosInListChunking`:
        - A chunk fills while its estimated length (each value's JSON plus a separator, plus the rest of the query
          measured from a one-value render) stays within budget, or until 1000 values.
        - Budget = limit − `MarginFor` (max/16, 32,768 by default). The margin covers the production service, which is
          unmeasured; on the emulator the local measurement matched the server exactly.
        - Every chunk is then measured with the store's own length function and halved until it fits.
        - A list under 1000 values is now split when its query is over budget.
- [x] Live test: a list of long strings that one 1000-value chunk would overflow is read and counted equal to the C#
      oracle, for the base store and a view.
      → `Task842LongStringChunkingTests` (live read and count, sync and async; 6 offline planner tests) and
        `Task842ViewLongStringChunkingTests`.
      → `Task834InListChunkingTests`' ceiling proof now sends the query straight through the container's LINQ, since a
        store with a huge chunk size now splits by length.
      → Red proof: with the old planner, 8 of the 10 new tests failed.
- [x] A single value too large to fit in any chunk still refuses by name.
      → Refused before any request: "one value of its IN list … serializes to about N characters … would be L
        characters, over … 524288". The value itself never appears.
      → Suites live: CosmosDB 200 of 200. CosmosDB.Views 93 of 95; the 2 failures are TASK-843's own tests, still in
        progress at the time.

## Out of scope

- The SQL providers: their limit counts parameters, not characters.

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
