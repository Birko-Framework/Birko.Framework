---
id: TASK-842
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Chunks are cut by serialized length: each chunk's query text plus its values stays under the limit with a
      margin. The value count per chunk is a ceiling, not the only rule.
- [ ] Live test: a list of long strings that one 1000-value chunk would overflow is read and counted equal to the C#
      oracle, for the base store and a view.
- [ ] A single value too large to fit in any chunk still refuses by name.

## Out of scope

- The SQL providers: their limit counts parameters, not characters.

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
