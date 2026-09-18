---
id: TASK-452
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
related: [TASK-313, TASK-316]
findings: []
pr: null
affects: [Birko.Data.Core, Birko.Data.ViewModel, Birko.Data.Localization]
---

# The "detach before writing to a store-owned object" rule now has three implementations

## Context

[[TASK-313]] established the rule — *a decorator never writes to an object the inner store returned; hand
it a detached copy* — and implemented `Object.MemberwiseClone`-by-reflection in
`Birko.Data.Localization/Decorators/LocalizedEntityFields.cs`, `internal` to that project.

[[TASK-316]] hit the identical hazard in the ViewModel repositories' update merge and could not reuse it:
a shared project's `internal` is only visible when both are imported into the same consumer assembly, and
a consumer may import `Birko.Data.ViewModel` without `Birko.Data.Localization`. So it wrote the helper
twice more — once per sync/async repository tree.

**Three implementations of one rule is the shape § Conventions keeps recording as the cause of drift.**
Nothing is broken today; all three are byte-equivalent.

## Acceptance criteria

- [ ] One producer, in `Birko.Data.Core`, reachable from every project that needs it
- [ ] All three call sites forward to it; the two in `Birko.Data.ViewModel` and the one in
      `Birko.Data.Localization` keep their current behaviour, measured by their existing suites
- [ ] The `MemberwiseCloneMethod == null` branch stays **defensive, not witnessed**, and is still labelled
      as such — do not "simplify" it into a throw
- [ ] A test asserts the copy is faithful for a field with **no public setter**, which is the specific
      thing that rules out a property-wise copy

## Human test plan

`N/A — fully covered by automated tests.`
