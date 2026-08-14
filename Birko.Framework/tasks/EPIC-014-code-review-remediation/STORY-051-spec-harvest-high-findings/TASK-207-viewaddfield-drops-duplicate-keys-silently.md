---
id: TASK-207
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-129]
pr: c1d729a
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

- [x] Every `View.AddField` call site is enumerated, with whether it can present a duplicate key carrying a
      **different** field — the legitimate re-add paths (`_fieldsCache` reuse, multi-`LoadField`, **and the
      multi-`[View]`-attribute loop, which § Context missed**) named explicitly and shown to remain silent
- [x] **Both** collision shapes in § Context are covered, including the aggregate-vs-source-column one
      TASK-129 introduced, each with a test
- [x] The two "unique by construction" comments TASK-129 left in `SqlViewTranslator` and `DataBase_View` are
      corrected — they are true among view properties only, not against the source-column keys sharing the
      dictionary
- [x] A genuine collision no longer disappears: it either throws with the view type, both properties and the
      key in the message, or cannot occur because the key is now the view property
- [x] Red-verified: reverting the change fails the test. Report the split as numbers and name any test that
      passes either way as a contract pin rather than as evidence
- [x] Measured against every `Birko.Data.SQL*.View*` / `.Views` suite plus the attribute-driven view fixtures
      — this changes view *loading*, so a consumer view that currently loads is the thing at risk
- [x] TASK-129's aggregate keying still holds (its `Two_aggregates_of_the_same_function_*` and
      `The_attribute_builder_also_keeps_both_same_function_aggregates` stay green), and its DDL alias still
      round-trips against the persistent read
- [x] `/specs regen views-and-aggregation`, spec diff reviewed

## Out of scope

- **The aggregate collision** — closed by [[TASK-129]]; this task must not regress it.
- Widening the spec map to cover the view DDL emitters — [[TASK-208]].

## Outcome

**What was wrong.** A SQL view's columns live in a per-table `Dictionary<string, AbstractField>`, and
`View.AddField` ended with `if (!table.Fields.ContainsKey(fieldName))` — a key already present meant the
incoming field was thrown away. No column, no exception, no log entry; the view property it belonged to read
back as `default(T)`. [[TASK-129]] closed the aggregate instance by keying aggregates on their view property
and left the guard alone, so two shapes stayed live: **two view properties projecting one source column**
(both keyed on the source column), and — created by TASK-129's own change — **an aggregate whose view
property matches a neighbouring column's source name**, because aggregates were then keyed by view property
while non-aggregates beside them stayed keyed by source column, two namespaces in one key space.

**The fix.** Every view field is now keyed by the property it populates (`View.ViewFieldKey`), so both
collisions cannot occur rather than being reported — view properties are unique on a CLR type by
construction, and the two namespaces become one. Done **at the producer** rather than at the three call
sites, so a fourth caller is correct without being told: TASK-129's lesson applied to its own residue. A
genuinely different field arriving on a taken key now throws `FieldAttributeException` naming the table, both
properties and the key — the SH-H037 "refuse, don't drop quietly" rule — as a backstop for the paths the view
builders no longer produce (`AddField`'s explicit `name`, and `AddTable`, whose fields carry a source
property). Safe to re-key because nothing reads this key as a column name: the persistent read and the sort
key both go through the field, the aggregate alias uses it only as a fallback when `Property` is unset, and
`Table.GetField(string)` has no callers at all.

**Step-6 split: 7 of 9.** Named in the progress log above. The two contract pins
(`Re_adding_the_very_same_field_on_a_taken_key_stays_silent`,
`A_view_with_two_view_attributes_still_loads_and_keeps_each_field_once`) assert the *old* silent-skip
survives, which is what makes them blast-radius checks and not evidence. 815/815 green across 13 SQL suites.

**Judgement calls.**

- **§ Context's re-add inventory was incomplete, and that decided the design.** It named `_fieldsCache`
  reuse and multi-`LoadField`; it missed that `ViewAttribute` is `AllowMultiple = true`, so `LoadView` runs
  its whole per-property field loop **once per `[View]` attribute** — the ordinary way a three-table view
  declares its second join — re-presenting every field as a *fresh* `AbstractField`. § Approach's option 1
  ("throw, but only when the incoming field is genuinely different") would therefore have broken every
  multi-`[View]` view had "different" been read as reference inequality, which is the natural reading. The
  option-1-alone route was rejected for this; `IsSameField` compares by value, and the multi-`[View]` view
  has a test.
- **Option 2 over option 1, against the task's own recommendation.** § Approach called option 1 "the smaller
  change and the one that cannot break a working view". Measurement said otherwise: option 1 is the one with
  the re-add hazard, and it only *reports* a collision that option 2 makes impossible. Both were taken —
  option 2 as the fix, option 1 as a backstop with its own test, since "I keyed it so it can't collide" is
  construction, not evidence.
- **The stricter option rejected: making the backstop unconditional.** Throwing on any duplicate key,
  ignoring whether the field is the same, is simpler and would be a behaviour change in every
  attribute-driven view — unmeasured, and § Conventions' SH-H037 rule says fail-fast is legitimate only once
  the blast radius is measured and an opt-out is checked first. The idempotent-re-add path *is* that opt-out.

**⚠ Post-close correction — the close-gate review found a real gap in this fix.** The backgrounded
`code-review` returned after the commits landed and was right. Measured on SQLite with this task's own
`VkCollidingView` under `Persistent`:

```
DDL: SELECT VkPersons.Name, VkOrders.Total, SUM(VkOrders.Amount) AS "Total" FROM …
GetPersistentViewSelectFields() → 0=Name  1=Total  2=Total
```

Keying the field dictionary by view property is only three quarters of the "one producer" rule.
`GetPersistentViewSelectFields()` still returns `field.Name` — the **source column** — for non-aggregates,
and `ViewSelectSqlBuilder` still projects them unaliased. So on the persistent path **the collision was
relocated from the dictionary to the view DDL, not closed**: two output columns named `Total`, and because
the persistent read selects *by name*, the aggregate binds to the non-aggregate's column and reads `70`
where `5` is correct. On MSSql and PostgreSQL `CREATE VIEW` rejects a duplicated output name, so such a view
cannot be created at all.

**Honest severity.** No previously-*correct* view is broken — only views that already had a collision, and
those were already silently dropping a column. Shape 1 persistent actually improves on SQLite (both columns
now read `alice`). But shape 2 persistent goes from `Total = 0` to `Total = 70` — a plausible wrong answer
replacing an obvious one, which is *worse* on this project's own severity ladder — and on MSSql such a view
now fails to create where it previously created-but-wrong. That is a genuine regression, narrow (needs the
name coincidence **and** `Persistent`/`Auto`) but real.

**Why it shipped: the excluded test was excluded on a false premise.** The comment justifying no Persistent
variant claimed such a test would *pass* on SQLite and so would bless broken behaviour. It fails. The one
variant that would have caught this was skipped on a misdiagnosis — the precise failure mode CLAUDE.md
records twice already (a guard whose own test cannot fail). The comment is corrected in the suite and the
reasoning recorded here; **re-check the claim in a "why I did not test X" comment as carefully as an
assertion**, because nothing executes it.

Corrected in this pass: the test comment, the spec (the read-back scenario is now qualified to `OnTheFly`
and a new scenario documents the persistent duplicate as shipped behaviour), and the test-count arithmetic
(**815**, not 813 — the per-suite figures were right, the sum was not).

**Not corrected: the duplicate itself — it needs a decision.** The fix is ~4 lines (alias non-aggregates by
view property in `ViewSelectSqlBuilder`; return `Property.Name` uniformly from
`GetPersistentViewSelectFields`) and would also close [[TASK-209]]'s quoting mismatch. It is deliberately
**not** taken here because it changes the DDL of **every** persistent view: a view created by an older
build has columns named by source column, and after the change the read asks for the view-property name, so
**already-deployed persistent views must be recreated**. That is a migration decision, it belongs to
TASK-209 (which also requires the PostgreSQL verification this box cannot run), and taking it silently
inside a P2 would be exactly the scope escalation the skill forbids.

**Flagged, not fixed.**

- **The production change is in a file no spec area covers.** `View.cs` sits in the 90% of
  `Birko.Data.SQL.View` the map deliberately excludes; the map's own comment predicted this, named TASK-129,
  and asked for a DECISION — [[TASK-208]], still open, `assignee: human`. The two new spec scenarios are
  grounded in `SqlViewTranslator.cs` instead, and the backstop throw is left unspecced rather than
  quietly widening the map.
- **`View.AddField` can still mutate a cached source table.** If a caller constructs
  `new View(new[] { DataBase.LoadTable(typeof(X)) })`, `AddField` finds that table by name and adds to the
  *shared* `LoadTable` instance, also setting `field.Table` on cached fields. Pre-existing, unchanged by this
  task, and unreachable from any framework code path — both view builders start from `new View()` and
  `View.AddTable` has no callers. Not filed: no reachable defect, and it is the same public-surface hazard
  as `AddField` itself.

## Human test plan

N/A — a view either loads or throws at load time, which an automated test observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-207` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-209 because that task *throws* on the failing provider (bottom of the
  severity ladder, and the silence key prefers a plausible wrong answer) and its first acceptance criterion
  mandates a real PostgreSQL reproduction that this box cannot produce (Docker daemon not running), so it
  cannot finish inside one session. TASK-207 loses a column silently.
- step 3 — verified: **held, and shape 1's open question is answered — it is reachable, not latent.**
  `ViewDefinitionBuilder.Build` does *not* reject two `Select()`s over one source column, so both shapes
  reproduce off the public fluent API and off the attribute builder: 6 of 7 new tests fail against
  unmodified production code. **One correction to § Context:** it names two legitimate re-add paths
  (`_fieldsCache` reuse, multi-`LoadField`) and misses the load-bearing third — `ViewAttribute` is
  `AllowMultiple = true`, so `LoadView` runs its whole field loop once per `[View]` attribute and re-adds
  every field as a *fresh* `AbstractField` instance. An unconditional throw would break every multi-`[View]`
  view; reference equality cannot distinguish that re-add from a collision. Criteria below amended.
- step 4 — layer: local (`Birko.Data.SQL.View/SQL/Tables/View.cs`, with comment corrections in
  `Birko.Data.SQL.Views/SqlViewTranslator.cs` and `Birko.Data.SQL.View/SQL/DataBase_View.cs`)
- step 5 — fix in `View.cs` (`ViewFieldKey` + backstop throw), comments in `SqlViewTranslator.cs` +
  `DataBase_View.cs`; tests in `Birko.Data.SQL.Views.Tests/ViewFieldKeyCollisionTests.cs`;
  **815/815 green** across 13 SQL suites (Views 59, SQL 459, SqLite 146, SqLite.View 9, View.Migrations 14,
  MSSql.View 19, MSSql 26, MySQL.View 7, PostgreSQL.View 7, Data.Views 36, SQL.ViewModel 18, Caching 7,
  Providers 8)
- step 6 — reverted fix surgically (key back to `field.Name`, throw short-circuited): **7 of 9 failed**.
  Fix-dependent = `Two_view_properties_over_one_source_column_both_survive_translation`,
  `Two_view_properties_over_one_source_column_both_read_back_their_value`,
  `An_aggregate_whose_view_property_matches_another_columns_source_name_survives`,
  `The_colliding_aggregate_and_non_aggregate_read_back_their_own_values`,
  `The_attribute_builder_keeps_two_view_properties_over_one_source_column`,
  `The_attribute_builder_keeps_a_colliding_aggregate_and_non_aggregate`,
  `A_genuinely_different_field_on_a_taken_key_is_refused_rather_than_dropped`.
  Contract pins (pass either way, **not** evidence) = `Re_adding_the_very_same_field_on_a_taken_key_stays_silent`
  and `A_view_with_two_view_attributes_still_loads_and_keeps_each_field_once` — both assert the *old*
  silent-skip behaviour survives, which is exactly what makes them blast-radius checks rather than proof.
  TASK-129's 12 `AggregateViewDdlTests` also pass either way here: this revert touches only TASK-207's
  change, so they pin that its aggregate keying is not regressed.
- step 7 — respecced `views-and-aggregation`. Requirements changed: the *Two aggregates of the same
  function* scenario dropped its now-false clause ("`View.AddField` skips a key it already holds"); two
  scenarios added under *SQL view translation* — *Two view properties projecting one source column both
  survive translation* and *An aggregate whose view property matches a neighbouring column's source name
  survives*. Diff reviewed: nothing in it beyond this task's acceptance. **Coverage note:** the production
  change is in `Birko.Data.SQL.View/SQL/Tables/View.cs`, which no area's globs reach — the map says so in
  a comment naming TASK-129 and asking for a DECISION, which is [[TASK-208]]. The two new scenarios are
  grounded in `SqlViewTranslator.cs` (in scope) rather than in `View.cs`, and the backstop throw is left
  unspecced for the same reason. Left excluded rather than silently widened.
- step 8 — merge gate. `verify-conventions` (project-local shadow): check 1 clean (**0 warnings**, no
  CS86xx; the `NU1903` advisory on `SQLitePCLRaw` via `Microsoft.Data.Sqlite 9.0.0` is pre-existing and
  untouched by this diff). Checks 2–8, 10 N/A. **Step 0b fired** — § Conventions' "one producer" rule
  described *aggregates* only and is now inaccurate; updated in the same change, plus a `Recent Updates`
  entry (check 9). `code-review` was launched and returned no readable result, so the correctness pass ran
  **inline** per the TASK-117 rule — it added two test comments (why equal read-back values are sound for
  the same-source-column shape, and why no Persistent variant is asserted). **Security: conditional pass
  run** — the diff touches SQL identifier emission, and this family has shipped two injection findings. The
  key reaches SQL only via `AggregateAlias`'s fallback, which fires when `Property == null`, where
  `ViewFieldKey` returns `field.Name` exactly as before; the emitted SQL is unchanged, which the exact
  projection assertions on four providers confirm rather than assume. No new surface.
  `integration: single-branch`, so no branch or merge. Closed `done`; `pr: c1d729a`.
  `tasks/README.md` deliberately **not** regenerated — same call as TASK-129's close, and for the same
  reason: ~50 task files from an uncommitted `/tasks intake` run would land in the dashboard as links to
  files no other checkout has. A `/tasks triage` is owed once that intake commits.
