---
id: TASK-723
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-18]
pr: null
github-issue: null
jira-key: null
---

# Cosmos serializer returns a disposed stream; MongoDBLogModel never gets timestamps

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `CosmosGuidIdSerializer.FromStream<Stream>` returns the stream from inside `using (stream)`, so the caller gets a disposed stream. `MongoDBLogModel` declares timestamps but not `ITimestamped`, so the timestamp decorator never stamps it; its typed `CopyTo` drops timestamps between two Mongo log models; it is referenced nowhere else (maybe dead).

Evidence: Birko.Data.CosmosDB/Serialization/CosmosGuidIdSerializer.cs:31-36; Birko.Data.MongoDB/Models/MongoDBLogModel.cs:7,15.

## Acceptance criteria

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-723` — leave empty until then._
