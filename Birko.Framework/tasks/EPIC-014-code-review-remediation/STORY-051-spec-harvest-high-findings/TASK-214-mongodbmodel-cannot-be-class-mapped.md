---
id: TASK-214
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-212]
pr: null
github-issue: null
jira-key: null
findings: []
---

# A model deriving `MongoDBModel` cannot be serialized by the driver at all

## Context

Found by [[TASK-212]] (2026-08-15) while building an offline probe that renders driver filters — the probe
could not even obtain a serializer for its own document type. Nothing to do with that task's defect; filed
rather than folded in.

Two distinct failures, both measured against `MongoDB.Driver` 3.2.0 with **no** connection, by asking
`BsonSerializer.SerializerRegistry.GetSerializer<T>()` / calling `.ToBsonDocument()`:

| model | result |
|---|---|
| `class MgDoc : MongoDBModel` | `BsonSerializationException: The property 'Guid' of type 'Birko.Data.MongoDB.Models.MongoDBModel' cannot use element name 'Guid' because it is already being used by property 'Guid' of type 'Birko.Data.Models.AbstractModel'.` |
| `class Doc : AbstractModel` | `BsonSerializationException: An error occurred while serializing the Guid property of class Birko.Data.Models.AbstractModel: GuidSerializer cannot serialize a Guid when GuidRepresentation is Unspecified.` |

**Failure 1 — the `MongoDBModel` override.** `AbstractModel.Guid` is `public virtual Guid?` and
`MongoDBModel` re-declares it as `public override Guid?` carrying `[BsonRepresentation(BsonType.String)]`.
The driver's `BsonClassMap` maps *declared* members per class in the hierarchy, so an override appears twice
under one element name and the map refuses to freeze. This is the constraint type of the **sync** store
(`MongoDBStore<T> where T : MongoDBModel`), so on the face of it that store cannot serialize anything.

**Failure 2 — the Guid representation.** Driver 3.x defaults `GuidRepresentation` to `Unspecified` and
requires it to be chosen explicitly. Nothing in `Birko.Data.MongoDB` sets `BsonDefaults.GuidRepresentation`,
registers a `ConventionPack`, or calls `BsonClassMap.RegisterClassMap` — grepped for all three, plus
`BsonSerializer.RegisterSerializer`. `MongoDBStore.InitCore` is the only init hook and does not do it.

**Why this may have gone unnoticed, and why that is the alarming part.** The one suite that would exercise
serialization end-to-end, `MongoFilterMatrixLiveTests`, is **gated on `BIRKO_MONGO_HOST`** and no-ops when it
is absent — which it is in this environment. `MongoNullFilterGuardTests` passes precisely because
`RequireFilter` throws *before* any driver call. So every MongoDB test that runs here avoids serialization
entirely. That is the [[TASK-209]] pattern exactly: a capability that may not work at all, under a green
suite, because the only test that would notice is gated off.

**⚠ Do not conclude the store is broken until this is measured against a server.** The offline registry may
not be what the store uses at runtime, and a consumer may register maps itself. What is established is only:
*with the driver's default conventions and nothing else configured, these two calls throw.* Confirming or
refuting "the sync MongoDB store cannot round-trip an entity" is this task's first job.

## Approach

1. **Measure against a real MongoDB first** (`BIRKO_MONGO_HOST=localhost`, e.g. a container). Round-trip an
   entity through `MongoDBStore<T>` and through `AsyncMongoDBStore<T>`. Either it works — in which case find
   what configures serialization and the offline probe was simply missing it, and record that — or it does
   not, and the sync store has never worked.
2. If it is broken, the two failures need different fixes:
   - **The override** — either drop the `override` in favour of the base property plus a class-map/attribute
     that sets the representation, or `BsonClassMap.RegisterClassMap<MongoDBModel>` unmapping the duplicate.
     Prefer whichever keeps `[BsonRepresentation(BsonType.String)]`'s intent (Guid stored as a string).
   - **The representation** — set it once, in the store's init or a module initializer, not per model.
     Note `ChangeStreamDocumentKeyResolver` already assumes `GuidRepresentation.Standard` in its tests, so
     picking a different one would contradict code that already ships.
3. **Whatever the outcome, the gating is the deeper problem.** A capability whose only real test is
   env-gated is untested by default. Consider a non-gated serialization test — class-mapping and
   round-tripping through `ToBsonDocument()` need no server.

## Acceptance criteria

- [ ] Both failures are reproduced or refuted against a real MongoDB, with the result recorded here
- [ ] If real: a model deriving `MongoDBModel` round-trips through the sync store, and one deriving
      `AbstractModel` round-trips through the async store
- [ ] The Guid representation is chosen **once** and does not contradict
      `ChangeStreamDocumentKeyResolver`'s existing `GuidRepresentation.Standard` assumption
- [ ] A **non-gated** test covers class-mapping and BSON round-trip, so this cannot regress silently again —
      neither call needs a server
- [ ] Red-verified with the split as numbers; contract pins named as pins
- [ ] If refuted: what actually configures serialization is written up here, and the offline probe's
      limitation is recorded so the next person does not re-file this

## Out of scope

- [[TASK-212]]'s unbounded-filter guard. It refuses before any driver call, so it neither depends on nor
  masks this.
- Auditing every other env-gated suite in the family for the same "green because it never ran" shape. Worth
  doing; not here.

## Human test plan

- [ ] Start a local MongoDB, set `BIRKO_MONGO_HOST=localhost`, and run `Birko.Data.MongoDB.Tests` — the
      gated matrix suite is the one that has never run in this environment, and its result is the whole
      question.

## Implementation plan

_Populated by `/tasks plan TASK-214` — leave empty until then._
