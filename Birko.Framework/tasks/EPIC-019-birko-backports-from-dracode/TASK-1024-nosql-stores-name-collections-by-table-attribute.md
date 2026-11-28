---
id: TASK-1024
parent: EPIC-019
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-1013, TASK-990]
findings: []
pr: null
github-issue: null
jira-key: null
---

# MongoDB, Cosmos DB and Elasticsearch name a type's collection by its `[Table]` attribute, as SQL does

## Context

DraCode observed at its TASK-988/990 human test that one model gets different names per backend: SQL uses the `[Table]`
name, MongoDB the class name. Checked 2026-11-28: **no NoSQL backend reads `[Table]`**; only SQL does
(`Birko.Data.SQL/SQL/DataBase_Table.cs`, which accepts `Birko.Data.SQL.Attributes.Table` and the BCL
`System.ComponentModel.DataAnnotations.Schema.TableAttribute`).

| Backend | Name today | Where |
|---|---|---|
| MongoDB | class name | `MongoDBClient.GetCollection` (`collectionName ?? typeof(T).Name`) |
| Cosmos DB | container name from settings, else class name | `AsyncCosmosDBStore.EnsureDatabaseAndContainerExistAsync` |
| Elasticsearch | `{settings.Name}_{IndexSettings name for the type, else class name}`, sanitized | `ElasticSearchStoreHelper.ResolveIndexName` |
| RavenDB | Raven's convention (pluralized class name), also part of every document id | `Conventions.GetCollectionName`, `RavenDocumentId` |

TASK-1013's OAuth models carry `[Table]` (the SQLite OAuth package's table names), so on a store factory the same OAuth
data is `oauth_clients` on SQL and `OAuthClientEntity` on MongoDB.

The user approved filing this on 2026-11-28, as recommended: the BCL attribute (the NoSQL packages do not reference
Birko.Data.SQL), an explicit name in settings keeps winning, RavenDB left out.

## Acceptance criteria

- [ ] MongoDB, Cosmos DB and Elasticsearch use the BCL `TableAttribute`'s name for a type that carries it, when no
  explicit name is configured (Mongo `collectionName` argument, Cosmos container name, ES `IndexSettings`); a type without
  it keeps the class name. One resolver, not one per backend (rule 16: one producer for a name).
- [ ] Decide, with the user, whether this is on by default or opt-in: on by default **renames** the collection/index of
  every type that already carries `[Table]`, so existing data under the class name reads as empty. Whichever is chosen,
  the change is listed as breaking in the Recent Updates entry and each backend's README.
- [ ] RavenDB stays as it is: its collection name is part of every document id, so renaming orphans existing documents.
  Recorded in the RavenDB README as a known difference.
- [ ] Tests per backend (live where the backend is live): a `[Table]` type lands under that name; an explicit settings
  name wins over it; a type without it keeps the class name.
- [ ] The TASK-1013 OAuth contract still passes on all nine backends.

## Human test plan

1. On a scratch MongoDB database, run DraCode's store factory against MongoDB and issue one OAuth client.
2. List the collections (`db.getCollectionNames()`): the client is in `oauth_clients`, as on SQL.

## Out of scope

- RavenDB collection names (see above).
- Migrating data already stored under class-named collections.
