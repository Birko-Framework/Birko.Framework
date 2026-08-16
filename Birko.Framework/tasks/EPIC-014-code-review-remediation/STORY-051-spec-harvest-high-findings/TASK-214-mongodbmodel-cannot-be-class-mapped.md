---
id: TASK-214
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-212]
pr: [Birko.Data.MongoDB@667f9b2, Birko.Data.MongoDB.Tests@6b6b1ec]
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

## Verdict (2026-08-16, step 3) — CONFIRMED-WIDER, measured against a live server

Measured against **MongoDB 7** in a container (`docker run mongo:7 -p 37017:27017`), driver `3.2.0.0`,
through the **real store path** (`SetSettings` → `MongoDBClient` → `Collection.InsertOne`), not the offline
registry:

| probe | result |
|---|---|
| `MongoDBStore<MgDoc>.Create` (sync, `MgDoc : MongoDBModel`) | `BsonSerializationException` — duplicate element name `Guid` |
| `AsyncMongoDBStore<PlainDoc>.CreateAsync` (async, `PlainDoc : AbstractModel`) | `BsonSerializationException` — `GuidSerializer cannot serialize a Guid when GuidRepresentation is Unspecified` |
| read-back after both writes | **0 rows** — nothing was persisted |

**The finding holds and is wider than filed.** It was scoped to *"the sync store cannot serialize anything"*;
in fact **neither store can persist a single entity**. The async store escapes failure 1 (its constraint is
`AbstractModel`, so a model that avoids `MongoDBModel` class-maps fine) and then hits failure 2 unavoidably,
because `CreateCoreAsync` assigns `data.Guid ??= Guid.NewGuid()` — so *every* write carries a non-null `Guid`
and every write throws. Repositories inherit the same fate through the same constraints
(`MongoDBModelRepository<T> where T : MongoDBModel`, `AsyncMongoDBModelRepository<T> where T : AbstractModel`).

**So the whole `Birko.Data.MongoDB` persistence surface is non-functional out of the box**, for every
consumer that has not registered driver serialization itself. Grepped and confirmed nothing in the project
does: no `BsonClassMap.RegisterClassMap`, no `ConventionPack`, no `BsonSerializer.RegisterSerializer`, and
`MongoDBStore.InitCore` is an empty method with a comment saying MongoDB needs no initialization.

Two notes on the measurement, because both were live risks the task named:

- **The offline probe was not missing runtime configuration** — there is none. The live run reproduces both
  failures identically, and the sync one throws *before server selection*, so it never depended on a server
  at all.
- **`BsonDefaults.GuidRepresentation` does not exist in driver 3.x** (it was removed with the V2/V3
  representation modes). The representation is now carried by the registered `GuidSerializer`, which
  defaults to `Unspecified` and refuses to serialize. That is why "set `BsonDefaults.GuidRepresentation`",
  as the Approach suggests, is not an available fix.

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

- [x] Both failures are reproduced or refuted against a real MongoDB, with the result recorded here
      — **reproduced** on MongoDB 7, see § Verdict
- [x] A model deriving `MongoDBModel` round-trips through the sync store, and one deriving
      `AbstractModel` round-trips through the async store — **both halves, not just the sync one**
      (rescoped at step 3: the async store is equally broken, see § Verdict).
      `MongoStoreRoundTripLiveTests`, both green against MongoDB 7
- [x] The Guid representation is chosen **once** and does not contradict
      `ChangeStreamDocumentKeyResolver`'s existing `GuidRepresentation.Standard` assumption —
      `MongoSerialization.EnsureRegistered()`, called from the `MongoDBClient` constructor, the one
      funnel both stores' `SetSettings` pass through. `Standard` for un-attributed Guids (pinned by
      `An_unattributed_guid_property_serializes_as_standard_binary`), string for the canonical
      `AbstractModel.Guid` — the two are not in conflict, see § Outcome
- [x] A **non-gated** test covers class-mapping and BSON round-trip, so this cannot regress silently again —
      neither call needs a server. `Serialization/MongoSerializationTests`, 7 tests, no env gate
- [x] Red-verified with the split as numbers; contract pins named as pins — see steps 6 and 8a in the
      progress log (8a is the re-run after the close-gate review added tests)
- [x] If refuted: what actually configures serialization is written up here, and the offline probe's
      limitation is recorded so the next person does not re-file this — **not refuted**; the probe was
      accurate and its limitation was that it under-stated the blast radius, recorded in § Verdict

## Out of scope

- [[TASK-212]]'s unbounded-filter guard. It refuses before any driver call, so it neither depends on nor
  masks this.
- Auditing every other env-gated suite in the family for the same "green because it never ran" shape. Worth
  doing; not here.
- **[[TASK-218]]** — spawned from this task's own live run. An array's `.Contains` in a filter binds to
  `MemoryExtensions.Contains` on .NET 9+ and the MongoDB driver rejects it. Different root cause
  (expression binding, not serialization), so `MongoFilterMatrixLiveTests` stays red on that one row
  rather than being made to pass by deleting the shape.
- The remaining `.map.yml` gap in this project — `ChangeStreams/*.cs` (3 files) and
  `Models/MongoDBLogModel.cs` are still reachable by no glob. Belongs to [[TASK-208]]'s backlog.

## Human test plan

- [x] Start a local MongoDB, set `BIRKO_MONGO_HOST=localhost`, and run `Birko.Data.MongoDB.Tests` — the
      gated matrix suite is the one that has never run in this environment, and its result is the whole
      question. **Done during this task** (`docker run --rm -p 27017:27017 mongo:7`): the suite ran for
      the first time and reported 26 of 27 filter shapes correct. The 27th is [[TASK-218]].

## Outcome

**What was broken.** Nothing could be saved to MongoDB. Every write through either store threw, so a
consumer of `Birko.Data.MongoDB` got an exception on the first `Create` and zero rows in the database —
measured against MongoDB 7, not inferred.

Three failures, one root cause: the project registered **no** MongoDB driver serialization at all, and the
one thing it did do to compensate was itself broken.

1. `MongoDBModel` re-declared `public override Guid? Guid` just to hang
   `[BsonRepresentation(BsonType.String)]` on it. `BsonClassMap` maps *declared* members per class in the
   hierarchy, so that override and `AbstractModel.Guid` both claimed element name `Guid` and the map
   refused to freeze — the sync store's entire type constraint was unserializable.
2. Driver 3.x removed `BsonDefaults.GuidRepresentation`; its default `GuidSerializer` carries
   `Unspecified`, which throws rather than choosing. The async store (constraint `AbstractModel`) escaped
   failure 1 and hit this on every write, because `CreateCoreAsync` always assigns a `Guid`.
3. **Found only by running against a live server** — with the writes finally landing, every *read* threw
   `FormatException: Element '_id' does not match any field or property`. No Birko model declares `_id`,
   by design, so the driver auto-generates an ObjectId the class map then refuses.

**The fix.** `MongoDBModel` loses the override and becomes a pure marker (it stays, because it is the sync
store's type constraint). One new `Serialization/MongoSerialization.EnsureRegistered()` does all three
registrations once per process, called from the `MongoDBClient` constructor — the single point both
stores' `SetSettings` pass through, so no store has to remember.

**Judgement calls, and the stricter option rejected in each case.**

- **The string representation moved to `AbstractModel`, not `MongoDBModel`.** The narrower option — keep
  it scoped to the sync store's base — does not work: with the override gone, `MongoDBModel` declares
  nothing, and `BsonClassMap<MongoDBModel>.AutoMap()` does not include inherited members, so there is no
  member map to attach the serializer to. Putting it on the declaring type also makes the async store
  consistent with the sync one, which it never was.
- **Two Guid representations coexist, deliberately.** The canonical `AbstractModel.Guid` is a **string**;
  any other `Guid` member is **standard binary**. These are not in conflict: a per-member serializer
  overrides the global one. Both intents are pre-existing — `MongoDBModel`'s attribute wanted the string,
  and `ChangeStreamDocumentKeyResolver` already assumes `GuidRepresentation.Standard` when reading a
  binary `_id`. Collapsing them to one representation would have contradicted shipped code.
- **Registration hangs off the client constructor, not a `[ModuleInitializer]`.** A module initializer is
  stricter — it cannot be missed — but shared projects compile into the *consumer's* assembly, so it would
  run before consumer code and, combined with `TryRegister*`, the framework would always win. The
  constructor runs when a store is given its settings, i.e. after start-up, so a consumer who configures
  their own Guid serializer keeps it. Precedence beat coverage here.
- **`IgnoreExtraElements` is set with `IsInherited`.** Without the inherited flag it would apply only to
  `AbstractModel` itself, and every real entity is a derived type with its own automapped class map — the
  flag is not inherited by default. The narrower call would have looked correct and fixed nothing.

**Flagged, not fixed.**

- **[[TASK-218]]** — with writes working, the gated matrix suite ran for the first time and found one real
  translation divergence: `x => arr.Contains(x.Member)` over a C# **array** throws
  `NotSupportedException` on the driver, while `List<T>` / `IEnumerable<T>` / `Enumerable.Contains(arr, …)`
  all render `$in` correctly. On .NET 9+ an array's `.Contains` binds to `MemoryExtensions.Contains`.
  Characterised offline in that task. Not this task's root cause — expression binding, not serialization —
  so it was spawned rather than folded in. **The matrix suite is therefore still red on that one row**, and
  deliberately so: making it pass would have meant deleting the shape.
- **`.map.yml` under-coverage, fifth instance.** None of the four files this fix changed was reachable by
  any glob, so the harvest never specced the defect and this fix's own regen would have been blind to it.
  Added `Models/MongoDBModel.cs` + `Serialization/*.cs` to `core-model-contracts`. Still unmapped in this
  project after the fix: `ChangeStreams/*.cs` (3 files, no area covers change streams at all) and
  `Models/MongoDBLogModel.cs` — 4 of 18. Left for the existing backlog ([[TASK-208]]) rather than widened
  here.
- **The gating pattern the task called "the deeper problem" is only half-addressed.** The new non-gated
  suite closes it for serialization, but auditing every other env-gated suite in the family for the same
  "green because it never ran" shape was out of scope and remains undone.
- **[[TASK-219]]** — the close-gate review's two remaining findings, filed as one task because they are
  one disagreement: the framework holds **two contradictory answers for what `_id` is**. This fix
  implements "`_id` is a driver-generated ObjectId", which forces the `IgnoreExtraElements` cost below;
  `MongoViewTranslator.GetFieldName` implements "`_id` *is* the Guid", which makes every view filtering
  on `Guid` match nothing. Recommendation recorded there: adopt the latter, which deletes the cost and
  fixes the translator at once — and it is unusually cheap, because TASK-214 proves no consumer can hold
  Birko-written MongoDB data to migrate.

**What the close-gate review changed, and one thing it got wrong.**

The review raised five findings; three were fixed here, two became TASK-219.

- **Fixed — the silent-success hole (its finding 1, and the sharpest one).** Both `TryRegister*` results
  were discarded and `_registered` set unconditionally, so if anything resolved `Guid` BSON before the
  first store was built, the framework would record success while every write threw the *exact* error
  this task exists to remove — minus the diagnostic. Now the results are observed and the two causes are
  separated: a consumer's own serializer is honoured silently (documented first-wins precedence), the
  driver's throwing default is refused with a message naming the remedy. This repo's § SH-H037 rule,
  applied to my own code.
- **Fixed — `GetMemberMap` null deref** (finding 3): an opaque `NullReferenceException` out of a
  constructor became a message naming `AbstractModel.Guid` and the likely cause.
- **Fixed — `volatile` on the DCL flag** (finding 4). **Its stated justification was wrong**: it claimed
  the codebase uses volatile-backed double-checked locking, but `AbstractStore.cs:15` and
  `AbstractAsyncStore.cs:17` both use a plain `bool`. Taken anyway on its own merit — unlike those two,
  this one publishes state a caller reads, and the doc comment claims thread safety.
- **The new guard's own test immediately caught a false refusal in it.** `new GuidSerializer(BsonType.String)`
  also reports `GuidRepresentation.Unspecified` and works perfectly well, because the representation only
  selects among the *binary* encodings — so keying on `GuidRepresentation` alone would have refused a
  working consumer configuration. CLAUDE.md's warning that a false refusal is worse than the hole, caught
  by writing the negative cases into the theory data rather than only the positive one.

## Implementation plan

_Populated by `/tasks plan TASK-214` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-165 (15 medium security-and-authorization findings) because that task is fifteen unverified harvester claims needing a full triage pass (fails self-containment and verified-over-unverified), while this is a single already-measured mechanism and the last todo under the high-findings story
- step 3 — verified against live MongoDB 7 (container, driver 3.2.0.0): CONFIRMED-WIDER. Both failures reproduce through the real store path; read-back = 0 rows. Wider than filed: the ASYNC store is equally unable to write (CreateCoreAsync always assigns a Guid, so failure 2 fires on every write), so the whole persistence surface is non-functional, not just the sync store. Context + acceptance criteria corrected before any code.
- step 4 — layer: local. Root cause is Birko.Data.MongoDB's own model shape + its total absence of driver serialization registration; the driver and consumers are fine.
- step 5 — fix in Birko.Data.MongoDB/{Models/MongoDBModel.cs, Serialization/MongoSerialization.cs (new), MongoDB/MongoDBClient.cs, ChangeStreams/ChangeStreamDocumentKeyResolver.cs (doc), .projitems}; tests in Birko.Data.MongoDB.Tests/{Serialization/MongoSerializationTests.cs (new, non-gated), Stores/MongoStoreRoundTripLiveTests.cs (new, gated)}. Live 77/78, ungated 78/78, 6 dependent suites 39/39.
- step 6 — two isolating reverts. Revert A (restore the duplicate Guid override): 7 of 78 failed, 6 fix-dependent = MongoDBModel_declares_no_members_of_its_own, A_model_deriving_MongoDBModel_can_be_class_mapped, A_model_deriving_MongoDBModel_round_trips_through_bson, A_null_canonical_guid_round_trips_as_null, An_unattributed_guid_property_serializes_as_standard_binary, Sync_store_round_trips_a_MongoDBModel. Revert B (no-op EnsureRegistered): 6 of 78 failed, 5 fix-dependent = A_model_deriving_AbstractModel_round_trips_through_bson, A_model_deriving_MongoDBModel_round_trips_through_bson, An_unattributed_guid_property_serializes_as_standard_binary, Sync_store_round_trips_a_MongoDBModel, Async_store_round_trips_an_AbstractModel. Contract pins (pass either way): EnsureRegistered_is_idempotent, and all 71 pre-existing tests. The 7th/6th failure in both reverts is MongoFilterMatrixLiveTests, which fails in the FIXED state too for an unrelated reason — spawned, not counted as evidence.
- step 7 — respecced core-model-contracts. NONE of the four changed files was reachable by any .map.yml glob (fifth instance of the under-coverage pattern), so Models/MongoDBModel.cs + Serialization/*.cs were added to the area first. Requirements added: "The MongoDB wire contract for the canonical identity is registered once, centrally" (6 scenarios) and "MongoDBModel declares no state of its own" (1 scenario). Diff purely additive — no existing wording changed, nothing unexplained. Unmapped in Birko.Data.MongoDB after the addition: 4 of 18 (ChangeStreams/*.cs x3, Models/MongoDBLogModel.cs) — reported, left to TASK-208.
- step 7b — spawned TASK-218 (array .Contains binds to MemoryExtensions.Contains on .NET 9+, driver throws NotSupportedException). Separate root cause; the matrix suite stays red on that one row deliberately.
- step 8 — close gate. verify-conventions: build warning-clean (no CS86xx), xUnit+FluentAssertions in the parallel test tree, new file registered in .projitems; register-on-introduce applied — the new "driver has no usable default" pattern recorded in BOTH Birko.Data.MongoDB/CLAUDE.md (§ Components › Serialization, § Data Types corrected: the old `Guid -> BinData(3)` line was wrong) and the aggregator CLAUDE.md (§ Conventions + § Recent Updates). security-review NOT triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; IgnoreExtraElements drops unknown fields rather than honouring them, so extra document data cannot influence a model. code-review (medium): 5 findings — 3 fixed here (silent TryRegister* success, GetMemberMap null deref, volatile), 2 spawned as TASK-219. Its volatile justification was factually wrong (AbstractStore/AbstractAsyncStore both use a plain bool) — taken on independent merit, not on the stated precedent.
- step 8a — re-verified red after the review fixes. Revert A (restore the duplicate Guid override): 8 of 83 failed, 7 fix-dependent (the six from step 6 plus A_derived_model_cannot_opt_back_into_strict_extra_element_handling). Revert B (no-op EnsureRegistered): 7 of 83 failed, 6 fix-dependent (the five from step 6 plus the same new test). Contract pins, unchanged: EnsureRegistered_is_idempotent, the IsBrokenDefaultGuidSerializer theory (pure function, independent of registration state), and the 71 pre-existing tests. Fixed state: ungated 83/83, live 82/83 (the 83rd is TASK-218), 6 dependent suites 39/39.
- step 8b — closed done; 667f9b2 (production) / 6b6b1ec (tests). Spawned TASK-218 and TASK-219.
