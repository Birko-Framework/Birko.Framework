---
id: TASK-219
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-214, TASK-208]
pr: [Birko.Data.MongoDB@4f5e095, Birko.Data.MongoDB.Views@1a69f29, Birko.Data.MongoDB.Tests@dba7278, Birko.Data.MongoDB.Views.Tests@43d8c39]
github-issue: null
jira-key: null
findings: []
---

# `Birko.Data.MongoDB` has two contradictory answers for what `_id` is

## Context

Raised by the close-gate code review on [[TASK-214]] (2026-08-16) as two separate findings. They are
filed as one task because they are **one disagreement**, and fixing either half alone leaves the
framework still holding both positions — the "one producer" rule in CLAUDE.md § Conventions.

**Position A — `_id` is a driver-generated ObjectId; the canonical id is an ordinary `Guid` field.**
This is what `ChangeStreamDocumentKeyResolver`'s remarks state, what TASK-214's
`MongoSerialization` registration implements, and what `docs/specs/core-model-contracts.md` now specs.

**Position B — `_id` *is* the canonical `Guid`.** `Birko.Data.MongoDB.Views/MongoViewTranslator.cs:140`:

```csharp
/// Maps Guid property to _id.
private static string GetFieldName(string propertyName)
{
    if (propertyName == "Guid") return "_id";
    return propertyName;
}
```

Under position A — the one that actually ships — this is **wrong**: a Mongo view that filters on
`Guid` compares against the ObjectId and matches nothing, and a view projecting `Guid` returns the
ObjectId rather than the entity's id. Pre-existing, and it was unfalsifiable until TASK-214 made
writes work at all.

**The cost of position A, measured.** `MongoSerialization` has to set `IgnoreExtraElements(true)` +
`SetIgnoreExtraElementsIsInherited(true)` on the `AbstractModel` class map, purely so the unwanted
ObjectId does not break every read. Two consequences, both measured on driver 3.2.0:

- **Every Birko Mongo entity is now a silent-drop reader, framework-wide.** Rename a
  `[BsonElement("qty")]` to `"quantity"` and existing documents deserialize with `Quantity == 0`, no
  exception, no log. This is in direct tension with the standing convention *"a mapper that cannot
  express something refuses; it never drops it quietly"* (§ SH-H037).
- **A derived model cannot opt back into strictness.** `[BsonIgnoreExtraElements(false)]` on an
  entity is overridden by the base map's inherited flag — the driver's `Freeze()` copies the base
  value unconditionally. Measured: both a plain model and one carrying the attribute report
  `IgnoreExtraElements = True` and both accept an unexpected element. Pinned by
  `A_derived_model_cannot_opt_back_into_strict_extra_element_handling` so it is documented rather
  than hidden, **not** because it is desirable.

## Why position B is the likely answer — and the unusual reason it is cheap

Making the canonical `Guid` the `_id` removes the extra element entirely, which removes the need for
`IgnoreExtraElements`, which removes both costs above **and** makes `MongoViewTranslator` correct
without touching it. It also stops paying for a second index and a redundant 12-byte id per document.

The normal objection to changing an id layout is migration. **There is no data to migrate**: TASK-214
established that no write through either store has ever succeeded, so no consumer can hold
Birko-written MongoDB documents. That objection is void here, and it will not stay void — this gets
strictly more expensive the moment the fixed stores are in use, which is why it is P1 despite being a
design question rather than a live crash.

`ChangeStreamDocumentKeyResolver` already handles a string `_id` (its step 2 parses one back to a
Guid), so it needs no change either — only its remarks do.

## Decision (2026-08-16) — position B: the canonical `Guid` becomes `_id`

Agreed by the user at pick time. The framework will hold **one** answer: `_id` *is* the entity's
canonical `Guid`, stored as a string. `IgnoreExtraElements` goes away with it, and
`MongoViewTranslator.GetFieldName` becomes correct without being touched.

## Approach

1. ~~This is a decision — get it agreed before implementing.~~ **Done, see § Decision.**
2. If B: map the id on the `AbstractModel` class map (`cm.MapIdMember(guid)` with the existing
   string representation), then **remove** `SetIgnoreExtraElements` / `SetIgnoreExtraElementsIsInherited`
   and confirm reads still work — that removal is the point, not a side effect.
3. If A is kept instead: fix `MongoViewTranslator.GetFieldName` to stop rewriting `Guid` → `_id`, and
   record in `Birko.Data.MongoDB/CLAUDE.md` that silent extra-element dropping is a deliberate,
   accepted cost with no per-model opt-out.
4. Either way, re-run the live round-trip suite **and** `MongoFilterMatrixLiveTests` against a real
   server — `docker run --rm -p 27017:27017 mongo:7`, `BIRKO_MONGO_HOST=localhost`.

## Acceptance criteria

- [x] One answer for `_id` holds across `MongoSerialization`, `MongoViewTranslator`,
      `ChangeStreamDocumentKeyResolver` and `docs/specs/core-model-contracts.md` — verified by reading
      all four, not by fixing one. The canonical `Guid` IS `_id`, as a string. The translator needed no
      change (it was already right); the resolver's step 2 already parses a string `_id`, so only its
      remarks moved; both specs corrected
- [x] A Mongo view that filters on `Guid` returns the matching entity, proven against a live server
      (today it matches nothing) — `MongoViewIdentityLiveTests`, MongoDB 7. Also pinned the negative:
      a genuinely absent id still returns 0, so the fix did not simply widen the match
- [x] If position B: `IgnoreExtraElements` is **gone** from the `AbstractModel` class map, and a model
      carrying `[BsonIgnoreExtraElements(false)]` once again rejects an unexpected element —
      `A_derived_model_can_still_choose_strict_extra_element_handling`, plus
      `An_unexpected_element_is_not_silently_dropped_by_default` for the framework-wide default
- [x] `A_derived_model_cannot_opt_back_into_strict_extra_element_handling` is updated or deleted to
      match the chosen answer — it currently pins the cost of position A and must not silently outlive it.
      **Replaced** by `A_derived_model_can_still_choose_strict_extra_element_handling`, which asserts the
      opposite and carries a comment saying so
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6 in the progress log
- [x] `/specs regen core-model-contracts` run, and the requirement TASK-214 added is corrected — plus
      `views-and-aggregation`, which the new `MongoViewSerialization.cs` falls under and which already
      specced the translator's `Guid` → `_id` rule (that scenario is now true rather than contradicted)

## Out of scope

- [[TASK-218]]'s array-`Contains` translation defect.
- The remaining `.map.yml` gap ([[TASK-208]]) — note that `Birko.Data.MongoDB.Views/**` and
  `ChangeStreams/*.cs` being unmapped is part of why position B went unnoticed.

## Human test plan

- [x] With a live MongoDB, create a view over an entity, filter it by `Guid`, and confirm the entity
      comes back — this is the behaviour that is silently empty today, and "empty result" is
      indistinguishable from "no matches" in a log, so it is worth seeing once by hand.
      **Done during this task** (`docker run --rm -p 27017:27017 mongo:7`), first by hand through a probe
      that printed the stored document and the emitted pipeline at each step, then as
      `MongoViewIdentityLiveTests`. The by-hand run is what caught that fixing the entity half left the
      filter still returning 0.

## Outcome

**What was broken.** Every MongoDB view was wrong, and mostly wrong *silently*. The framework held two
answers for what a document's `_id` is: [[TASK-214]]'s serialization left it to the driver as an
auto-generated ObjectId with the canonical `Guid` in a field beside it, while
`MongoViewTranslator.GetFieldName` had always rewritten the `Guid` property to `_id`. Each layer was
internally consistent. Measured on MongoDB 7:

| | before | after |
|---|---|---|
| stored document | `{ "_id": ObjectId(…), "Guid": "3c79…", "Name": "acme" }` | `{ "_id": "3c79…", "Name": "acme" }` |
| view projecting the canonical id into `Guid?` | **throws** `Cannot deserialize a 'Guid' from BsonType 'ObjectId'` | returns the entity |
| view filtering on it | **0 rows** for a document that exists | 1 row |

**The fix.** The canonical `Guid` **is** `_id` (`cm.SetIdMember`), stored as a string — one identity per
document. `MongoViewTranslator` needed no change; it was already right. Two things fell out: the
`IgnoreExtraElements(IsInherited)` that TASK-214 needed purely to tolerate the second id is **deleted**,
so no Birko entity is a silent-drop reader any more and `[BsonIgnoreExtraElements(false)]` works again.

**It took two registrations, not one — and the probe is what said so.** After `SetIdMember` the
projection worked but the filter *still* returned 0. `MongoViewStore` renders its `$match` through the
**view type's** class map, where a `Guid?` property still used the framework's global binary serializer
and so compared BinData against the projected string. `MongoViewSerialization.EnsureRegistered<TView>`
now maps each view type to mirror its projection, and `MongoViewStore` calls it at construction.

**Judgement calls, and the stricter option rejected in each case.**

- **Resolved the contradiction rather than patching the louder side.** Fixing only the translator was the
  smaller edit and would have satisfied the filed finding — while leaving two ids per document and the
  framework-wide silent-drop reader that tolerating them required.
- **A view has *no* id member, rather than mapping its id property to `_id`.** The projection emits
  `"_id": 0` and then each field under its own property name, so binding anything to `_id` is wrong. The
  narrower option — only fixing the Guid representation — would have left a view property named `Id`
  throwing `Element 'Id' does not match any field or property`, which is exactly how my first probe
  failed before I noticed it was failing for the *wrong* reason.
- **Element names pinned to property names, on the whole view map.** Broader than the canonical-id
  member alone, and deliberately so: the driver's conventions are entity-shaped and a projection is not
  an entity. Scoping it to the id property would leave the next convention to be discovered in
  production.
- **`MongoViewSerialization` is a second class rather than an extension of `MongoSerialization`.** It
  lives in `Birko.Data.MongoDB.Views`, which is the project that owns the projection; putting view
  knowledge in the base MongoDB project would invert the dependency.

**Flagged, not fixed.**

- **[[TASK-218]]** is untouched and still red on its one row of `MongoFilterMatrixLiveTests` — the array
  `.Contains` translation defect, a different root cause.
- **The `.map.yml` gap that hid this.** `views-and-aggregation` reaches `Birko.Data.MongoDB.Views/**`,
  so the new file is covered — but `Birko.Data.MongoDB/ChangeStreams/*.cs` and `Models/MongoDBLogModel.cs`
  are still reachable by no glob ([[TASK-208]]). The change-stream resolver is one of the three places
  that had to agree about `_id` here, and no spec covers it.
- **The window this fix used is now closed.** The migration objection was void only because no write had
  ever succeeded. Any further change to the id layout will need a real migration story.

## Implementation plan

_Populated by `/tasks plan TASK-219` — leave empty until then._

## Progress log

- step 2 — picked; top of the pool on blast radius (a view filtering on `Guid` returns a silent empty result, outranking TASK-218's loud throw) and on a closing window (migration-free only until the TASK-214-fixed stores are in use). Decision taken at pick: position B.
- step 3 — verified against live MongoDB 7. Finding HOLDS and the projection half is LOUDER than filed: it throws (Cannot deserialize a 'Guid' from BsonType 'ObjectId') rather than returning the ObjectId; the filter half is exactly as filed — CountAsync on the canonical Guid returns 0 for a document that exists. Probe printed the stored document and emitted pipeline at each step.
- step 4 — layer: local, two projects. Birko.Data.MongoDB owns the entity identity; Birko.Data.MongoDB.Views owns the projection's class map. Neither belongs in the other.
- step 5 — fix in Birko.Data.MongoDB/{Serialization/MongoSerialization.cs, CLAUDE.md} + Birko.Data.MongoDB.Views/{MongoViewSerialization.cs (new), MongoViewStore.cs, .projitems}; tests in Birko.Data.MongoDB.Tests/Serialization/MongoSerializationTests.cs (updated: 2 tests replaced/added) and Birko.Data.MongoDB.Views.Tests/{MongoViewSerializationTests.cs, MongoViewIdentityLiveTests.cs} (both new).
- step 6 — two isolating reverts. Revert A (restore position A in MongoSerialization): MongoDB.Tests 6 of 84 failed, 5 fix-dependent = A_derived_model_can_still_choose_strict_extra_element_handling, An_unexpected_element_is_not_silently_dropped_by_default, A_model_deriving_MongoDBModel_round_trips_through_bson, A_model_deriving_AbstractModel_round_trips_through_bson, A_null_canonical_guid_round_trips_as_null; Views.Tests 1 of 12 = A_view_projects_and_filters_on_the_canonical_guid. Revert B (drop MongoViewStore's EnsureRegistered call): MongoDB.Tests unaffected (matrix only), Views.Tests 1 of 12 = the same live test — which is the point: it isolates the WIRING, since the two non-gated view tests call the helper directly and pass either way. Contract pins: those two helper tests under Revert B, EnsureRegistered_is_idempotent, the IsBrokenDefaultGuidSerializer theory, and MongoViewTranslatorTests' Simple_projection_emits_only_project_with_guid_mapped_to_id — that last one passes either way and is worth naming, because it pins that the translator was never the thing that changed. Fixed state: ungated 84/84 + 12/12, live 83/84 (the 84th is TASK-218) + 12/12, 7 suites green.
- step 7 — respecced core-model-contracts (requirement statement + 3 scenarios corrected, 1 replaced, 1 added) and views-and-aggregation (new requirement "A view type is class-mapped to mirror its projection" + the Guid->_id scenario now reads as agreeing with storage). No .map.yml change needed: views-and-aggregation already globs Birko.Data.MongoDB.Views/**.
- step 8 — closed done; 4f5e095 + 1a69f29 (production, two repos) / dba7278 + 43d8c39 (tests, two repos). Merge gate: build warning-clean on both test projects; register-on-introduce applied to Birko.Data.MongoDB/CLAUDE.md and the aggregator CLAUDE.md (§ Conventions rule reworked — TASK-214's IgnoreExtraElements bullet marked superseded rather than deleted, since its "check a global flag's inheritance semantics" half still generalises). security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; the change makes reads STRICTER, not looser.
