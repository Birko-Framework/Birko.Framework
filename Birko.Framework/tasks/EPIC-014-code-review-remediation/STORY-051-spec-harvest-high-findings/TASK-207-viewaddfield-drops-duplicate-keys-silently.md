---
id: TASK-207
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-129]
pr: null
github-issue: null
jira-key: null
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: []
---

# `View.AddField` still drops a duplicate field key silently — the general case behind TASK-129's second defect

## Context

Spawned from [[TASK-129]] (2026-08-14), which closed the **aggregate** instance of this and deliberately left
the general one.

`View.AddField` (`../Birko.Data.SQL.View/SQL/Tables/View.cs:73`) ends with:

```csharp
var fieldName = (!string.IsNullOrEmpty(name)) ? name : field.Name;
if (!table.Fields.ContainsKey(fieldName))
{
    field.Table = table;
    table.Fields.Add(fieldName, field);
}
```

A field whose key is already present is **skipped**: no column in the view, no exception, no log entry, and
the view property it belonged to reads back as `default(T)`. TASK-129 measured exactly this for aggregates —
two `Sum`s on one table both keyed `"SUM"`, the second gone — and closed it by keying aggregates on their
(unique by construction) view property. **The guard itself is unchanged**, so any other collision still
behaves the same way.

Two reachable shapes, and **the second is new** — introduced by TASK-129's own keying change, found by its
close-gate review rather than by it:

1. **Non-aggregate against non-aggregate.** `AddField` with no explicit `name` keys on `field.Name`, the
   **source column**, so two view properties selecting the same source column — e.g.
   `Select<Person>(p => p.Name, v => v.DisplayName)` and `Select<Person>(p => p.Name, v => v.SortName)` —
   collide and the second silently never populates. Confirm against `SqlViewTranslator`/`LoadView` before
   scoping: it may be rejected earlier by `ViewDefinitionBuilder.Build`, in which case it is latent and this
   is a fail-fast hardening rather than a defect fix.
2. **Aggregate against non-aggregate — NEW, and TASK-129 created it.** That task now keys aggregates by their
   **view property** while non-aggregates in the *same per-table dictionary* stay keyed by their **source
   column name**. Those are two different namespaces sharing one key space, so a view selecting
   `Order.Total → OrderTotal` (keyed `Total`, the source column) alongside `Sum(Order.Amount) → Total` (keyed
   `Total`, the view property) collides — and whichever is added second is silently dropped. Both builders'
   new comments claim the view property is "unique by construction", which is true **among view properties**
   and not against the source-column keys beside them. TASK-129 traded one collision class for a narrower
   one; that was still a large net win (its class was reachable from an ordinary two-`Sum` view, this one
   needs a name coincidence between a view property and an unrelated source column) but it is not zero, and
   the comments overstate it. **Correct those two comments as part of this task**, whichever option is
   chosen.

Option 2 below removes both shapes at once, which is an argument for it that the original scoping missed.

**Why TASK-129 did not just make it throw.** § Conventions' SH-H037 rule says a mapper that cannot express
something must refuse rather than drop it quietly — which argues for a throw. It also says fail-fast is only
legitimate once the blast radius is measured, and this `ContainsKey` guard is load-bearing for at least two
other callers: `LoadView`'s `_fieldsCache` reuse branch (`DataBase_View.cs:170`, which re-adds a cached field
by name) and the multi-`LoadField` loop that can yield several fields for one property. An unconditional
throw there is a behaviour change in every attribute-driven view, unmeasured. TASK-129 closed the case that
actually lost data and filed this rather than widening its own scope.

## Approach

Two candidate shapes; decide with the measurement, not from the rule:

1. **Throw `FieldAttributeException`** (the SH-H037 precedent) naming the view type, the two colliding
   properties and the shared key — but only when the incoming field is genuinely *different* from the one
   already stored, so the legitimate re-add paths above stay silent. A same-field re-add is idempotent, not
   a collision.
2. **Key non-aggregate fields by their view property too**, making TASK-129's rule uniform: every view field
   is identified by the property it populates. Cleaner and removes the collision class rather than reporting
   it — but the key is what `Table.GetSelectFields` emits as an alias for aggregates and what
   `GetPersistentViewSelectFields` returns for source columns, so this needs the same three-way agreement
   check TASK-129 did, on the non-aggregate side.

Option 1 is the smaller change and the one that cannot break a working view. Option 2 is the one that makes
the model consistent. Measure first: enumerate every `AddField` call site and whether it can present a
duplicate key with a *different* field.

## Acceptance criteria

- [ ] Every `View.AddField` call site is enumerated, with whether it can present a duplicate key carrying a
      **different** field — the legitimate re-add paths (`_fieldsCache` reuse, multi-`LoadField`) named
      explicitly and shown to remain silent
- [ ] **Both** collision shapes in § Context are covered, including the aggregate-vs-source-column one
      TASK-129 introduced, each with a test
- [ ] The two "unique by construction" comments TASK-129 left in `SqlViewTranslator` and `DataBase_View` are
      corrected — they are true among view properties only, not against the source-column keys sharing the
      dictionary
- [ ] A genuine collision no longer disappears: it either throws with the view type, both properties and the
      key in the message, or cannot occur because the key is now the view property
- [ ] Red-verified: reverting the change fails the test. Report the split as numbers and name any test that
      passes either way as a contract pin rather than as evidence
- [ ] Measured against every `Birko.Data.SQL*.View*` / `.Views` suite plus the attribute-driven view fixtures
      — this changes view *loading*, so a consumer view that currently loads is the thing at risk
- [ ] TASK-129's aggregate keying still holds (its `Two_aggregates_of_the_same_function_*` and
      `The_attribute_builder_also_keeps_both_same_function_aggregates` stay green), and its DDL alias still
      round-trips against the persistent read
- [ ] `/specs regen views-and-aggregation`, spec diff reviewed

## Out of scope

- **The aggregate collision** — closed by [[TASK-129]]; this task must not regress it.
- Widening the spec map to cover the view DDL emitters — [[TASK-208]].

## Human test plan

N/A — a view either loads or throws at load time, which an automated test observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-207` — leave empty until then._
