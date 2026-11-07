---
id: TASK-834
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Measured against the Cosmos DB emulator (live suite per `live-tests.yml`): the largest Guid list a filtered
      read accepts, and the error past it.
- [ ] Either a list past the ceiling is split transparently, with the same rules as TASK-833 (IN only as an AND
      conjunct, non-string values, no sort or paging, one boundary for writes where Cosmos DB has one), or it is refused
      before the request with an exception naming the reason. The choice is the user's, recorded here.
- [ ] Tests pin whichever was chosen.

## Out of scope

- The SQL providers (TASK-833).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
