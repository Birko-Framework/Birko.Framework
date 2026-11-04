---
id: TASK-723
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.CosmosDB.Tests/Task723SerializerStreamTests.cs` and
`tests/Birko.Data.MongoDB.Tests/Models/Task723LogModelTests.cs`.

| Claim | Result |
|---|---|
| `CosmosGuidIdSerializer.FromStream<Stream>` returns a disposed stream | **reproduced:** `CanRead` false on what it returned |
| `MongoDBLogModel` is not `ITimestamped`, so the timestamp decorator never stamps it | **reproduced:** neither `ITimestamped` nor `ILogEntity`; `StoreWrapperBuilder` selects the decorator on `ITimestamped` |
| Its typed `CopyTo` drops timestamps between two Mongo log models | **reproduced:** a base-typed copy (and a load) carried the Guid only |
| It is referenced nowhere else (maybe dead) | **true:** no framework or consumer reference. Fixed rather than removed: it is public API, and the fix is the `AbstractLogModel` pattern |

## Implementation plan

The serializer hands a requested stream over before its `using`. `MongoDBLogModel` implements `ILogEntity` and overrides
`CopyTo(AbstractModel?)` / `LoadFrom(IGuidEntity)`, copying the timestamps to and from any `ILogEntity`.

## Built (2026-11-04)

5 tests: 4 failing on the old code, and a guard that a stream it reads is still disposed. MongoDB 179, CosmosDB 148
green (live). Spec `core-model-contracts.md` updated (scoped; `generated-at` left).
