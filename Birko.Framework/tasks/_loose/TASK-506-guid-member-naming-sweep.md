---
id: TASK-506
parent: null
feature: null
status: done  # 2026-09-29: merged to main (7c1bdc82) together with Symbio TASK-819/820
priority: P2
assignee: ai
created: 2026-09-28
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Guid-typed members named `…Id` — one word with the model base (`Guid`) and with Symbio's wire

## Context

Requested by consumer Symbio while it swept its own wire to one word (Symbio TASK-806 decision, TASK-819):
own key `Guid`, one reference `…Guid`, list `…Guids`. The framework's models already key on `AbstractModel.Guid`
and its foreign keys already read `…Guid` (`TenantGuid`, `EntityGuid`), but 43 Guid-typed members still said `…Id`.
The user decided to rename them here first: *"is not used anywhere for now and it can be fixed easily if I forgot
some consumer"*.

## Rule applied

- A Guid-typed member named `…Id` → `…Guid`; `…Ids` → `…Guids`.
- A plain model's own key `Guid Id` → `Guid Guid` (`JobDescriptor`, `OutboxEntry`, `QueueMessage`, `NfcTagMapping`).
- On a BASE type that consumers derive from, the own key keeps its qualifier (`EventBase.EventId` → `EventGuid`,
  `IWorkflowInstance.InstanceId` → `InstanceGuid`): a bare `Guid` there would collide with a derived event's own
  key (Symbio's `EventCreated(Guid Guid, …)` is the conference event, not the message).
- **Not** renamed: string-typed ids (`CorrelationIdMiddleware`'s header value, Azure `TenantId` config, JWT claim
  names) and `int` row numbers (`SqlSyncKnowledgeItem.Id`, an auto-increment column) — they are not Guid references.

## What moved (43 declarations, 388 + 19 use sites, compiler-driven over all 168 test projects)

`JobContext.JobId`, `JobDescriptor.Id`; `DomainEvent`/ES `IEvent` `EventId`/`AggregateId`/`UserId`; ES store
wrappers `CurrentUserId`; `IAuditContext.CurrentUserId`; `ConcurrentUpdateException.EntityId`;
`CosmosSyncKnowledgeItem.TenantId`; `EntityTag.TagId`/`EntityId`; `CrossTenantTagAccessException.TagId`;
`EventBase`/`EventContext`/`IEvent` `EventId`/`CorrelationId`; `DomainEventPublished.AggregateId`/`UserId`;
`EventEnvelope`, `OutboxEntry`, `OutboxEntryModel` `EventId`/`CorrelationId`; `QueueMessage.Id`;
`RoleAssignment.UserId`; `TokenServiceAdapter` records, `ICurrentUser.UserId` and both implementations;
`NfcAuthResult`/`NfcTagMapping` `UserId`; `IWorkflowInstance`/`WorkflowInstance`/`WorkflowException` `InstanceId`.

⚠ **Stored names moved with them — a consumer's existing data does not follow.** SQL column names pinned by
`NamedField`: `Id` → `Guid` on the job, outbox and workflow-instance tables, `EventId`/`CorrelationId` →
`EventGuid`/`CorrelationGuid` on the outbox; `EntityTags` columns `TagId`/`EntityId` → `TagGuid`/`EntityGuid`;
Cosmos sync-knowledge documents `TenantId` → `TenantGuid`; serialized events carry `eventGuid`/`correlationGuid`.
Symbio drops and reseeds every database (no deployment holds data). Any OTHER consumer with a database of these
tables needs DDL.
⚠ `RuleFilterBehavior`'s default rule context exposes `EventGuid`/`CorrelationGuid` (was `EventId`/`CorrelationId`)
— a stored rule expression that names the old variable no longer matches.
⚠ `CosmosSyncKnowledgeQueryTests` asserted `NotContain("TenantId")` — after the rename that passes vacuously, so it
was moved to `TenantGuid` with the rest (a guard that stops being able to fail is the TASK-066 failure).

## Consumers measured (2026-09-28, reads of the renamed members)

- **DraCode** — `.UserId` 13, `.AggregateId` 7, `.EventId` 3, `.CorrelationId` 2 (via `ICurrentUser`, `DomainEvent`,
  `EventBase`, `EventContext`). Compile-driven fix when it next builds.
- **Birko.Sandbox** — `EntityTag.TagId`/`EntityId` 3.
- WorkoutTracker uses `ICurrentUser` but reads none of the renamed members; BardStudio uses `JobContext` without `JobId`.

## Acceptance criteria

- [x] Every Guid-typed `…Id` member renamed; all 168 framework test projects build and pass.
- [x] Pinned column names and Cosmos document keys follow; docs (CLAUDE.md/README/docs, not CHANGELOG/audits) updated.
- [x] Merged to `main` together with Symbio TASK-819/820 (Symbio compiles this working tree): framework `7c1bdc82`, Symbio `9ab222f1`; Symbio unit suite on the merged mains 3392 passed.

## Human test plan

N/A — a rename asserted by the compiler and the framework test suite.
