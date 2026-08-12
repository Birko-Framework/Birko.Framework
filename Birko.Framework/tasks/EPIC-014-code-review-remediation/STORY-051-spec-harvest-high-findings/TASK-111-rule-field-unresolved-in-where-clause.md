---
id: TASK-111
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
pr: ed74331          # + tests: 2a50d49 (Birko.Data.SQL.Tests), a042697 (Birko.Data.SQL.SqLite.Tests)
github-issue: null
jira-key: null
findings: [SH-H023]
---

# `rule.Field` reaches the WHERE clause unresolved and unquoted

## Context

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:121` — **CONFIRMED**.

`ConvertLeaf` constructs `new Condition(rule.Field, values, …)`. `rule.Field` is an arbitrary string off the
rule, and it becomes the condition **name** with no property resolution and no quoting at this layer. All
five condition strategies then interpolate `Name` raw — `EqualConditionStrategy:26` is
`$"{condition.Name}{op}{value}"`.

Two consequences, and the benign one is more likely to be hit:

- **Wrong column.** No `LoadTable` / `GetFieldByPropertyName` lookup happens, so a `[NamedField]`-remapped
  property references a column that does not exist, and nothing qualifies the name with a table — making it
  ambiguous in a join.
- **Injection.** A rule tree is *configuration data*, and `docs/rules.md` advertises this path as producing
  a "direct WHERE clause". A `Field` of `1=1 OR 1=1 --` becomes executable SQL. Whether that is reachable by
  an attacker depends on whether a consumer lets users author rules — but the docs invite exactly that.

`DataBase.ResolveColumnName` exists and is not called here.

Rated P1 rather than P0 because reaching it requires a rule tree with a caller-influenced `Field`, whereas
[[TASK-110]]'s twin defect fires for any consumer with a remapped column and a sort. Same root cause, same
intended fix helper.

### Step-3 re-verification (2026-08-12) — holds, and the injection is worse than filed

Measured end-to-end against a real SQLite file (`RuleConditionConverter` → `connector.Select`), rules
carrying `ComparisonOperator.Equal` with value `999` against a 3-row table, so a *correct* filter returns 0:

| `rule.Field` | Emitted `WHERE` | Result |
|---|---|---|
| `Rank OR 1=1 --` | `WHERE Rank OR 1=1 -- = @p` | **3 rows of 3** — filter bypassed, no exception |
| `Rank = 1 OR 1=1 --` | `WHERE Rank = 1 OR 1=1 -- = @p` | **3 rows of 3** — same |
| `Rank; CREATE TABLE Pwned (x INTEGER); --` | `WHERE Rank; CREATE TABLE Pwned …; -- = @p` | **the `Pwned` table was created** |
| `(SELECT count(*) FROM sqlite_master)` | `WHERE (SELECT count(*) FROM sqlite_master) = @p` | subquery evaluated as the operand — a blind-boolean oracle |

The trailing ` = @param` the strategy appends is **not** a mitigation: `--` comments it out, and a bare
column name absorbs it. The parameter *name* is sanitised (`SqlBuilderContext:35`), which is what made this
look safe on a skim — the sanitisation is on the wrong string.

The finding is CONFIRMED with three corrections:

1. **DDL execution is reachable, not just a malformed statement.** The finding described injection as
   conditional on consumer-authored rules; that is still true, but its severity was understated — this is
   arbitrary statement execution, not only a widened predicate.
2. **The remapped-column half throws, it does not silently read the wrong column.** A
   `[NamedField("label_col")]` property emits `WHERE Label = @p` and SQLite answers *no such column: Label*
   — so a remapped property cannot be filtered **at all**. Same correction the twin recorded for SH-M022.
   The "wrong column" wording in the Context above is inaccurate; the defect is that the column does not
   exist. (An *ambiguous* name in a join is still possible, since nothing qualifies the identifier.)
3. **`ConvertLeaf`'s `isOr: true` branch (line 118) is dead** — the only call site passes `isOr: false`
   (line 24), and OR-ness is applied afterwards by `SetOr`. It carries the same defect and is fixed
   identically rather than left as a trap for whoever revives it.

**`RuleConditionConverter` has no callers anywhere in the Birko family** — it is a public API surface that
`docs/rules.md:255` advertises to consumers as producing "a direct WHERE clause". So there is no internal
funnel holding table metadata (which is how [[TASK-110]] resolved), and the entity type has to arrive
through the API. That is what shapes the fix below.

## Approach

Resolve `rule.Field` through the table metadata before constructing the `Condition`, and quote the resolved
column. An unresolvable field must **throw** — a rule referencing a non-existent property is a
configuration error, and today it produces either a SQL error or, worse, valid SQL that means something
else.

Check whether `Condition.Name` should hold a *resolved, quoted* value as an invariant rather than leaving
each of the five strategies to interpolate a raw string — if the type guaranteed it, this class of bug could
not recur in a sixth strategy.

## Acceptance criteria

_Amended at step 3, before any code was written — see the re-verification above. Two criteria were changed
and the reasoning is recorded here rather than in the Outcome, because changing a target after the fact is
how "done" gets silently redefined._

- [x] `rule.Field` is resolved against table metadata before it reaches any condition strategy, on a new
      **type-aware** overload — the entity type has to enter through the API because this converter has no
      internal call site to resolve at
- [x] ~~and quoted~~ → **deliberately NOT quoted.** [[TASK-110]] measured and rejected this for the twin
      sink: the codebase emits column identifiers bare everywhere (DDL, every condition strategy, the
      SELECT list) and quotes only table names, so quoting solely here would break a working filter on
      PostgreSQL, where the unquoted DDL identifier is folded to lower case. Quoting was never what closes
      the injection — **the resolution is the whitelist**. Emitting a *different* convention from the twin
      would also have split one rule across two sinks
- [x] ~~resolved via `DataBase.ResolveColumnName`~~ → that method is `private` and takes no table list.
      A public resolver is added mirroring `DataBase.ResolveOrderFields`, sharing one lookup order with the
      ORDER BY sink so the two cannot drift
- [x] A `[NamedField("col")]`-remapped property in a rule filters on the **right column**, asserted
      end-to-end. (Pre-fix this threw *no such column*, it did not read the wrong one — see correction 2)
- [x] A `Field` containing SQL (`1=1 OR 1=1 --`, a batch separator, a subquery, a quote-escape attempt)
      throws rather than reaching `CommandText` — on **both** the type-aware and the typeless overload
- [x] An unresolvable `Field` throws with a message naming the field and the entity type
- [x] Existing rule trees over normally-named properties are unaffected
- [x] The dead `isOr: true` branch carries the same guard rather than being left as a trap
- [x] Regression tests in `Birko.Data.SQL.Tests` (emitted SQL + rejection) and
      `Birko.Data.SQL.SqLite.Tests` (remapped column filters correctly end-to-end, payloads measured
      against a real database)
- [x] `/specs regen` for `filter-expression-translation`, spec diff reviewed

## Out of scope

- The ORDER BY sink ([[TASK-110]]) — shared root cause, separate file and fixture.
- `SH-H041`–`SH-H044`, the `RuleSpecification` match-all degradations. Those are the *in-memory /
  expression-tree* rule path; this is the SQL-text rule path. Tracked as [[TASK-116]].
- `Birko.Rules`' own `RuleExpressionConverter` — it is the reference implementation here, not a defect.

## Human test plan

N/A — covered by automated tests, and the injection half is asserted against a real SQLite database rather
than on emitted text.

## Outcome

A rule's `Field` used to become the SQL condition's **name** untouched, and every condition strategy
interpolates that name straight into `CommandText`. So configuration data — which `docs/rules.md`
advertises as a way to build "dynamic filtering from user-defined rules" — was executable SQL. It now
resolves against the entity's table metadata before it can reach a statement, and a field that resolves to
nothing is refused by name.

**Measured before the fix**, against a real SQLite file, 3 rows, rules whose value matched none (so a
correct filter returns 0):

| `rule.Field` | Result |
|---|---|
| `Rank OR 1=1 --` | 3 rows of 3, no exception |
| `Rank = 1 OR 1=1 --` | 3 rows of 3 |
| `Rank; CREATE TABLE Pwned (x INTEGER); --` | **the table was created** |
| `(SELECT count(*) FROM sqlite_master)` | subquery evaluated as the left operand |
| `Label` (a `[NamedField("label_col")]` property) | *no such column: Label* — a remapped property could not be filtered at all |

### Shape of the fix

`RuleConditionConverter` gained type-aware overloads (`ToConditions<T>` / `ToConditions(Type, …)`) that
resolve each field through the new `DataBase.ResolveRuleField`, emitting the resolved **table-qualified**
name. The pre-existing type-less overloads are kept and now require a bare column identifier
(`DataBase.ValidateRuleFieldIdentifier`). `ConvertLeaf` is the one place a `Field` becomes a
`Condition.Name`, so it is the one place the check lives.

The property-then-column lookup is now shared with the ORDER BY sink (`ResolveFieldNameIn`, called by both
`ResolveOrderFields` and `ResolveRuleField`): the two closed the same class of defect a fortnight apart,
and a consumer should not have to learn two rules for which field names are accepted.

### Judgement calls, and why the stricter option was rejected

- **Not quoted, though the finding prescribed it.** The stricter-looking option would have broken working
  filters. [[TASK-110]] measured this for the twin sink: the codebase emits column identifiers bare
  everywhere (DDL, SELECT list, every strategy) and quotes only table names, so quoting solely here would
  fail on PostgreSQL, where the unquoted DDL identifier folds to lower case. Quoting was never what closed
  the injection — the resolution is.
- **The type-less overloads were kept, not deleted.** Deleting them is the fail-closed option and is
  wrong here: a caller whose rule fields are already correct column names is doing nothing unsafe, and all
  20 pre-existing `RuleConditionConverterTests` are exactly that shape. Identifier validation refuses every
  measured payload while leaving those callers untouched. They are documented as the weaker path rather
  than marked `[Obsolete]`, which would spray build warnings across consumers for a call that is still
  legitimate.
- **`ArgumentException`, not a new exception type.** It matches what `ResolveOrderFields` already throws
  for the identical situation on the sibling sink.
- **`RuleSet` conversion was made eager.** It was lazy, so the refusal would have surfaced from inside the
  connector's statement builder and read as a database fault rather than a bad rule.
- **The dead `isOr: true` branch was fixed, not deleted.** It is unreachable today (the only call site
  passes `false`), but it carried the identical defect and would have reintroduced it silently for whoever
  revived it.

### Flagged, not fixed

- **SH-M128 — an OR rule group renders as AND.** Independently confirmed while writing the end-to-end
  suite: `RuleGroup.Or(Label == "a", Rank == 3)` returned 0 rows where 2 were expected. Same file, but a
  different root cause (`ConvertGroup` wraps with `AndSubCondition`, and `AppendSubConditionsTo` takes its
  separator from the parent's `IsOr`), so it was deliberately left out of scope. Already in the pool under
  [[TASK-153]]; no new task filed. The end-to-end group test uses an **AND** group and says why — asserting
  the OR result would have had to encode the broken behaviour to stay green, which blesses it.
- **`docs/specs/.map.yml` globbed `DataBase.cs` exactly**, so the `DataBase_*.cs` partials — including
  [[TASK-110]]'s `DataBase_OrderBy.cs`, which has been unspecced since it landed — were covered by no area.
  Widened to `DataBase*.cs` here because it directly affects the area being regenerated. The general sweep
  is [[TASK-142]]'s.
- **The `ResolveFieldSelectName` view-resolver delegate's output is not re-validated**, exactly as in the
  ORDER BY twin. It is a `public static` settable delegate, so in principle a consumer could register one
  that echoes its input and reopen the sink. Left as-is deliberately: the framework's own resolver
  (`Birko.Data.SQL.View/SQL/DataBase_View.cs:25`) derives every result from view metadata via
  `GetSelectName`, and that method legitimately returns **aggregate expressions** for view fields — so a
  bare-identifier check on its output would break working aggregate views. The trust boundary here is *who
  registers the resolver* (framework/app startup), not *who authors the rule* (potentially a user), and
  only the latter was the finding. Views are [[TASK-128]]'s area.
- **A `Recent Updates` entry and a § Conventions rule were added to `CLAUDE.md`** — the local
  `verify-conventions` check #9 (5+ files) and step 0b (register-on-introduce) both fired. The convention
  is the more important half: [[TASK-110]] closed the identical defect on the sibling sink twelve days
  earlier and recorded its reasoning only in a commit message and a doc comment, which is why this one had
  to be rediscovered rather than reused.
- **The `shaped-by` evidence pass cannot run from this aggregator** — every source glob in this area points
  into a sibling repo, so no `pr:` sha resolves under `git show` here. Recorded in the spec's frontmatter.
  This is the family-wide limitation CLAUDE.md already notes, not something specific to this area.

## Progress log

- step 2 — picked; ranked above TASK-117 (`RedisCache.ClearAsync` → `FLUSHDB`) because an injection sink
  outranks a destructive write on severity, and because TASK-117's acceptance requires a live-Redis
  foreign-key-survives assertion under the STORY-042 Docker tier, which does not exist yet and would stall
  mid-session. [[TASK-110]] (the twin ORDER BY sink) is `done`, so the resolve+quote helper this task needs
  is already in the tree.
- step 3 — verified: **holds**, and measured worse than filed (a `CREATE TABLE` payload actually executed;
  two payloads returned 3 of 3 rows for a filter that should return 0). Rescoped in three places, all
  recorded above the acceptance list: quoting dropped (twin's measured PostgreSQL reason), the prescribed
  `ResolveColumnName` is private so a public resolver is added instead, and the remapped-column half
  **throws** rather than reading a wrong column. Also found: the converter has no callers in the family, so
  the type must enter through the API; and `ConvertLeaf`'s `isOr: true` branch is dead but carries the same
  defect.
- step 4 — layer: local (`Birko.Data.SQL`). The defect is in this repo's own converter, not in a dependency;
  `Birko.Rules` is the reference implementation here and is not at fault.
- step 5 — fix in `SQL/Conditions/RuleConditionConverter.cs`, new `SQL/DataBase_RuleField.cs`,
  `SQL/DataBase_OrderBy.cs` (shared lookup), `Birko.Data.SQL.projitems`; tests in
  `Birko.Data.SQL.Tests/RuleFieldResolutionTests.cs` (31) and
  `Birko.Data.SQL.SqLite.Tests/RuleFieldResolutionEndToEndTests.cs` (11). 685/685 green across the 8
  SQL-touching suites (SQL 448, SqLite 146, Providers 8, Views 38, SqLite.View 9, ViewModel 18, Caching 7,
  View.Migrations 11).
- step 6 — reintroduced the defect surgically (`ConvertLeaf` takes `rule.Field` again, every signature
  intact): **34 of 42 failed** — unit 24/31, end-to-end 10/11. Restored, 594/594 green again.
  Contract pins (8, passed both ways, named below), not evidence. A *full* revert was rejected as the
  check: 29 of the 42 reference the new type-aware overloads and would not compile, so it would have
  reported 8 of 13 and hidden the rest behind a build error — the TASK-204 trap.
  Contract pins by name: `A_bare_identifier_still_passes_the_type_less_overload` (×5),
  `A_disabled_rule_carrying_a_payload_is_skipped_not_emitted`,
  `A_null_entity_type_is_rejected_rather_than_silently_skipping_resolution`,
  `An_unremapped_property_still_filters_correctly`.
- step 7 — merge gate: local `verify-conventions` (checks 0a/0b/1–10) — clean apart from two obligations it
  raised and this change met: a `Recent Updates` entry (check 9, 5+ files) and a § Conventions rule
  (step 0b, register-on-introduce). Nullable sweep clean on all four changed files. `security-review` could
  not run (it diffs against `origin/HEAD`, which a commit-to-main repo does not have) so the pass was done
  inline per close.md's fallback — it found the `$`-vs-`\z` anchor gap in this fix's own regex, now closed
  and pinned by two payload cases. **Blast radius measured against the consumer trees, not reasoned from
  the dispatch** (the TimeOnly lesson): `grep -rn "ToConditions"` across all 16 consumers returns nothing —
  three build rule trees but none reaches either converter. Zero consumer impact.
- step 8 — closed done; production `ed74331`, tests `2a50d49` + `a042697`, aggregator below.
