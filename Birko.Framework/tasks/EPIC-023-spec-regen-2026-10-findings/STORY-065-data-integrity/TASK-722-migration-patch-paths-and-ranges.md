---
id: TASK-722
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
findings: [SH-T696-17]
pr: null
github-issue: null
jira-key: null
---

# Migrations: unescaped Cosmos patch paths, a delete range that stops at now, null-client NRE

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Cosmos `RenameField` and `UpdateDocuments` build patch paths `/{name}` without JSON-pointer escaping (`DropField` escapes). The InfluxDB 'delete everything' range stops at `UtcNow`, so future-dated points survive. `MongoMigrationRunner` reads `mongoClient.Database` in its base call, so a null client is an NRE, not `ArgumentNullException`. TimescaleDB introspection commands never set `command.Transaction`.

Evidence: CosmosDBSchemaBuilder.cs:176; CosmosDBDataMigrator.cs:87; InfluxDBDataMigrator.cs:40; MongoMigrationRunner.cs:34; TimescaleDBMigration.cs:612-631.

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

Live on the Cosmos DB emulator, InfluxDB 2.9 and TimescaleDB: `tests/Birko.Data.Migrations.CosmosDB.Tests/Task722PatchPathLiveTests.cs`,
`tests/Birko.Data.Migrations.InfluxDB.Tests/Task722DeleteRangeLiveTests.cs`,
`tests/Birko.Data.Migrations.TimescaleDB.Tests/Task722IntrospectionTransactionLiveTests.cs`; and
`tests/Birko.Data.Migrations.MongoDB.Tests/Task722RunnerNullClientTests.cs`.

| Claim | Result |
|---|---|
| Cosmos `RenameField` / `UpdateDocuments` build `/{name}` unescaped | **reproduced:** renaming `a/b` wrote the new field and kept `a/b` (its remove was refused and swallowed); updating `a/b` was refused with 400. **And the escaped form (`DropField`'s) is no fix:** the emulator stored `a~1b` literally. The service is not measured, so a name holding `/` or `~` is refused |
| The InfluxDB "delete everything" range stops at `UtcNow` | **reproduced:** points a month ahead and in 1900 survived |
| `MongoMigrationRunner(null)` is an NRE | **reproduced:** `NullReferenceException` |
| TimescaleDB introspection never sets `command.Transaction` | **true, harmless, recorded, not changed:** Npgsql runs every command in the connection's open transaction; `IsHypertable` / `GetChunkInterval` saw a hypertable created earlier in the migration's own transaction. The test pins that |

## Implementation plan

`CosmosDBDataMigrator.PatchPath`, one producer for the three patch paths, refusing `/` and `~` before any read or
write. The InfluxDB delete covers 1677-09-22 to 2262-04-11 UTC. The Mongo runner checks its client before the base call.

## Built (2026-11-04)

7 tests: 4 failing on the old code (2 Cosmos refusals, the InfluxDB range, the Mongo null client), a plain-name rename
guard, and the TimescaleDB pin (passed on the old code — the claim did not reproduce). Migrations.CosmosDB 44,
InfluxDB 46, MongoDB 20, TimescaleDB 89 green live. Spec `migrations.md` updated (scoped; `generated-at` left).
