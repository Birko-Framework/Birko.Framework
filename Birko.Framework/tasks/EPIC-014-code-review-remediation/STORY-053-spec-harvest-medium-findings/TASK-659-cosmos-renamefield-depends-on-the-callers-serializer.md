---
id: TASK-659
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] Measured: `RenameField` on a plain-client `Database` (fails today), and what value the patch writes for a string,
      a number, a nested object and an array once the read works
- [x] `RenameField` reads through `QueryDocuments` and writes a value both serializers produce identically; the
      document's value round-trips exactly, through both clients
- [x] The data migrator's queries checked for the same dependency (fixed or recorded)
- [x] A regression test fails on the current code; the CosmosDB rename scenario in `migrations.md` updated

## Out of scope

- `DropField` and `DropIndex` (TASK-655)

## Human test plan

N/A — a library contract, asserted by live tests.

## Resolution (2026-10-17)

**Measured live first** (`Task659SerializerIndependenceLiveTests`). Each test runs on a fresh database, once with a
plain `CosmosClient` (Newtonsoft) and once with Birko's `CosmosGuidIdSerializer`, over documents carrying a nested
object, an array and a decimal:

| Step | Newtonsoft client (HEAD) | System.Text.Json client (HEAD) |
|---|---|---|
| `RenameField` (a nested value) | throws `InvalidOperationException` | moved exactly |
| `UpdateDocuments` / `DeleteDocuments` | throw | correct |
| `CopyData` | throws | exact copy |

**3 red, all on the Newtonsoft client, and all loud:** the typed `JsonElement` read failed before anything was written,
so nothing was corrupted. The data migrator had the dependency too (the "check" criterion): UpdateDocuments,
DeleteDocuments and CopyData typed their reads `JsonElement`. `CopyData` also upserted the `JsonElement` itself, and
Newtonsoft does not write that as JSON.

**Fix:**
- Every Cosmos migration read goes through `CosmosDBDataMigrator.QueryDocuments`: a stream query parsed by
  System.Text.Json, moved there from TASK-655's `DropField`.
- `CopyData` upserts each document's raw JSON with `UpsertItemStreamAsync`, so it is exact by construction. A stream
  call answers instead of throwing, so a non-success status now throws, naming the document (rule 22).
- `RenameField` patches `ToPlain(oldValue)`: dictionaries, lists and primitives (`long`, else `decimal`, else
  `double`), which both serializers write identically. `ExtractValue`, which turned an object into its text, is not
  used on this path.

6/6 green on both serializers. Suite 41/41, the only importer of `Birko.Data.Migrations.CosmosDB.projitems`. Spec: the
TASK-655 scenario that pointed here is rewritten to cover all five steps on both serializers.
