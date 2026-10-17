---
id: TASK-659
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos `RenameField` fails on a `Database` whose client uses the SDK's default serializer

## Context

Found while fixing [[TASK-655]]. `CosmosDBSchemaBuilder.RenameField` queries the documents with
`GetItemQueryIterator<JsonElement>` and passes the old value, a `JsonElement`, to `PatchOperation.Set`. Both go through
the serializer of the client the caller built the `Database` with, and `CosmosMigrationRunner` takes that `Database` from
the caller.

**Measured live 2026-10-17** (Cosmos emulator, a plain `new CosmosClient(conn)`, whose default serializer is
Newtonsoft): `RenameField("Rows", "Old", "New")` threw `InvalidOperationException` ("Operation is not valid due to the
current state of the object") from `JsonElement.TryGetProperty`, because the typed result came back empty. The document
kept `Old` and the migration failed, so it fails loudly and corrupts nothing. With a client built by Birko's
`Settings.GetCosmosClientOptions()` (`CosmosGuidIdSerializer`, System.Text.Json) it works.

TASK-655's `DropField` avoids the read dependency with a stream query parsed by System.Text.Json
(`CosmosDBSchemaBuilder.QueryDocuments`). `RenameField` can use the same read. The write half has to be checked too:
the patch value has to serialize the same way through either serializer. `CosmosDBDataMigrator.ExtractValue` turns a
`JsonElement` into plain values. Check whether the data migrator has the same read path.

## Acceptance criteria

- [ ] Measured: `RenameField` on a plain-client `Database` (fails today), and what value the patch writes for a string,
      a number, a nested object and an array once the read works
- [ ] `RenameField` reads through `QueryDocuments` and writes a value both serializers produce identically; the
      document's value round-trips exactly, through both clients
- [ ] The data migrator's queries checked for the same dependency (fixed or recorded)
- [ ] A regression test fails on the current code; the CosmosDB rename scenario in `migrations.md` updated

## Out of scope

- `DropField` and `DropIndex` (TASK-655)

## Human test plan

N/A — a library contract, asserted by live tests.
