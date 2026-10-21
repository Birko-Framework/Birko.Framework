---
id: EPIC-023
# status — one of: planned, in-progress, done, cancelled
status: planned
created: 2026-10-21
owner: ai
affects: [Birko.Data.SQL, Birko.Data.SQL.View, Birko.Models.SQL, Birko.Redis, Birko.Data.MongoDB, Birko.Data.Tenant, Birko.Communication, Birko.EventBus.Outbox.SQL, Birko.MessageQueue.MQTT, Birko.Data.EventSourcing, Birko.Data.InMemory, Birko.AI, Birko.Data.Migrations, Birko.Data.CosmosDB, Birko.Data.InfluxDB, Birko.Data.JSON, Birko.Data.XML]
# kind: omit for a normal epic; `review-intake` marks the epic a review pass was filed into
kind: review-intake
# source: review-intake epics only — the pass that produced the findings, plus where they came from ("security-review <date>", "code-review <PR>")
source: specs regen (TASK-696) at 50828c68, committed 22a2bb77, 2026-10-21
---

# Spec regen 2026-10 findings

## Area of concern

TASK-696 regenerated 15 spec areas from code (`/specs regen`, one harvest agent per area) at `50828c68`. The harvests
reported suspected bugs: behaviour read from the code that looks wrong, specced as it is. **None is measured**, so every
task here starts by reproducing it (rule 54) and either fixes it with a regression test or closes it with the evidence.

Two harvest findings were real and already fixed during the run: TASK-704 and TASK-705.

23 findings → 19 tasks · 3 dropped (below) · 1 linked to an open task.

### Findings dropped at intake

| Finding | Claim | Why dropped |
|---|---|---|
| SH-T696-20 | Backends write part of a bulk create on XML separate-file, MongoDB, ElasticSearch, RavenDB (512-item chunks) and Cosmos DB | A stated divergence, specced in backend-store-implementations; not a defect of one store. Decide with the store contract, not here. |
| SH-T696-21 | RavenDB boundary's sync retry also catches a ConcurrencyException thrown by the write itself | Inside the boundary every write goes into the session and only SaveChanges can raise it, so the write cannot throw it. |
| SH-T696-22 | Tenant admin scope (WithAllTenants) reads every tenant while item writes still compare against the ambient tenant | Already recorded in the tenant-isolation spec as an open decision; needs a decision, not a fix. |

### Linked to an open task

- SH-T696-23 → TASK-193: Repositories destroy their store twice (Raven, Mongo, SQLite, PostgreSQL, MySQL, SQL Server, Cosmos, Influx model repositories; RavenDB's second database delete may throw).

## Success criteria

- Every task reproduced or refuted with evidence; every reproduced defect fixed with a regression test.

## Stories / tasks

- [[STORY-063]] Security & tenancy
- [[STORY-064]] Correctness & invariants
- [[STORY-065]] Data integrity
- [[STORY-066]] Reuse, dead code & consistency
