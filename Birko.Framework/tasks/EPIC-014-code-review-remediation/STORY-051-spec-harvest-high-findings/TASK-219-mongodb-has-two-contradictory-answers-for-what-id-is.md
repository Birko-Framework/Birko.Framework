---
id: TASK-219
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-214, TASK-208]
pr: null
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

## Approach

1. **This is a decision, not just a fix — get it agreed before implementing.** Recommendation:
   adopt position B (`Guid` as `_id`), for the reasons above.
2. If B: map the id on the `AbstractModel` class map (`cm.MapIdMember(guid)` with the existing
   string representation), then **remove** `SetIgnoreExtraElements` / `SetIgnoreExtraElementsIsInherited`
   and confirm reads still work — that removal is the point, not a side effect.
3. If A is kept instead: fix `MongoViewTranslator.GetFieldName` to stop rewriting `Guid` → `_id`, and
   record in `Birko.Data.MongoDB/CLAUDE.md` that silent extra-element dropping is a deliberate,
   accepted cost with no per-model opt-out.
4. Either way, re-run the live round-trip suite **and** `MongoFilterMatrixLiveTests` against a real
   server — `docker run --rm -p 27017:27017 mongo:7`, `BIRKO_MONGO_HOST=localhost`.

## Acceptance criteria

- [ ] One answer for `_id` holds across `MongoSerialization`, `MongoViewTranslator`,
      `ChangeStreamDocumentKeyResolver` and `docs/specs/core-model-contracts.md` — verified by reading
      all four, not by fixing one
- [ ] A Mongo view that filters on `Guid` returns the matching entity, proven against a live server
      (today it matches nothing)
- [ ] If position B: `IgnoreExtraElements` is **gone** from the `AbstractModel` class map, and a model
      carrying `[BsonIgnoreExtraElements(false)]` once again rejects an unexpected element
- [ ] `A_derived_model_cannot_opt_back_into_strict_extra_element_handling` is updated or deleted to
      match the chosen answer — it currently pins the cost of position A and must not silently outlive it
- [ ] Red-verified with the split as numbers; contract pins named as pins
- [ ] `/specs regen core-model-contracts` run, and the requirement TASK-214 added is corrected

## Out of scope

- [[TASK-218]]'s array-`Contains` translation defect.
- The remaining `.map.yml` gap ([[TASK-208]]) — note that `Birko.Data.MongoDB.Views/**` and
  `ChangeStreams/*.cs` being unmapped is part of why position B went unnoticed.

## Human test plan

- [ ] With a live MongoDB, create a view over an entity, filter it by `Guid`, and confirm the entity
      comes back — this is the behaviour that is silently empty today, and "empty result" is
      indistinguishable from "no matches" in a log, so it is worth seeing once by hand.

## Implementation plan

_Populated by `/tasks plan TASK-219` — leave empty until then._
