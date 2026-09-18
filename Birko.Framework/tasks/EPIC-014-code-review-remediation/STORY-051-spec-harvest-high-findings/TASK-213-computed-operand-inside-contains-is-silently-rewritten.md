---
id: TASK-213
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-137]
pr: ea8db51
github-issue: null
jira-key: null
findings: []
---

# A COMPUTED operand inside `Contains` is silently discarded and replaced by a different predicate

## Context

Found by [[TASK-137]] (2026-08-14) — specifically by adding its shapes to `SqlExpressionParityTests`, the
compiled-delegate oracle suite. **This is pre-existing and independent of TASK-137**: it reproduces with
non-empty sets and with a non-negated `Contains`, neither of which that task touched.

`ids.Contains(<expr>)` translates correctly when `<expr>` is a plain mapped property. When `<expr>` is
**computed** — arithmetic, or a coalesce — the operand is **dropped** and the `In` condition is built with
`Name = null` plus a subcondition holding a fabricated `Equal`. Measured against real SQLite (seed
`Amount = 1, 5, 5, 9`, `Score = 10, null, 20, 30`; `SomeIds = {1, 5}`, `NoIds` empty), oracle =
`expr.Compile()` over the same rows:

| predicate | oracle | SQL | parsed condition |
|---|---|---|---|
| `SomeIds.Contains(x.Amount + 1)` | **0** | **1** | `Name=null Type=In Values=[1,5] Subs=1` → child `Amount Equal [1]` |
| `!SomeIds.Contains(x.Amount + 1)` | 4 | 3 | same, negated |
| `!SomeIds.Contains(x.Score ?? 0)` | 4 | 3 | `Name=null Type=In Values=[1,5] Subs=1` → child `Score Equal [0]` |
| `!NoIds.Contains(x.Score ?? 0)` | 4 | 3 | `Name=null Type=In Values=[] Subs=1` → child `Score Equal [0]` |
| `SomeIds.Contains(x.Amount)` (plain, control) | 3 | 3 | `Name=ProbeRows.Amount Type=In Values=[1,5]` ✔ |
| `(x.Score ?? 0) > 4` (coalesce OUTSIDE Contains, control) | 3 | 3 | `Name=COALESCE(ProbeRows.Score, 0) Type=Greather` ✔ |

Two things the table shows:

- **Wrong answers in both directions**, not just under-matching: the non-negated row returns a row that does
  not satisfy the predicate. `x.Amount + 1` appears to be consumed as though `+` were a comparison, leaving
  `Amount = 1`, which matches the `Amount = 1` row while the true answer is no rows at all.
- **The coalesce machinery works — it just isn't reached from the `Contains` operand path.** The last control
  row renders a proper `COALESCE(...)` as the condition's `Name`; inside `Contains` the same expression
  becomes a subcondition instead. So this is a wiring gap in the `In` operand resolution, not a missing
  translation.

Silent throughout: no exception, no log entry, and the malformed shape (`Name = null` with children) is
exactly the "non-empty collection that renders oddly" case `AddRequiredWhere`'s doc comment anticipates
without naming.

**Interaction with TASK-137 — nil, and verified so.** That task's reduction inspects `SubConditions` first, so
a malformed `In`-with-children is treated as a group, its child is not always-true, and the term renders
unchanged. `!NoIds.Contains(x.Amount) && !NoIds.Contains(x.Amount)` (plain operands) is 4/4 correct. The
malformed rows above answer the same before and after that fix.

## Approach

Start at `DataBase.ParseConditionExpression`'s `Contains` arm (`../Birko.Data.SQL/SQL/DataBase.cs`, the
`case "Contains":` around line 606) and establish **why** a computed operand becomes a subcondition — most
likely the operand is recursed into as though it were a nested predicate, so a `BinaryExpression` operand
takes the ordinary binary-comparison path and its right side is read as a value.

The fix is presumably to resolve the operand through the same expression-to-SQL-name path the working control
row uses (whatever produces `COALESCE(ProbeRows.Score, 0)` as a condition `Name`) and use its result as the
`In` condition's `Name`.

**Decide explicitly what happens to an operand that cannot be expressed** — the § SH-H037 rule applies: a
mapper that cannot express something refuses, it never drops it quietly. Today's silent rewrite is the worst
option available. Refusing needs a measured blast radius first, since consumer models may contain such
predicates that currently "work" (i.e. return wrong rows).

## Acceptance criteria

- [x] `ids.Contains(<computed expr>)` either translates correctly or **refuses**; it never silently
      substitutes a different predicate. Whichever is chosen is recorded with its reason
- [x] Every row of the § Context table agrees with its oracle (or refuses), including the two controls, which
      must not regress
- [x] The negated and non-negated forms are both covered, and the empty-set forms too — TASK-137 owns the
      *reduction* of an empty negated set, not the operand resolution, and the two must compose
- [x] The malformed shape (`In` condition with `Name = null` and subconditions) is no longer producible from
      the parser; if it remains representable, what the renderer does with it is asserted rather than left to
      `BuildSingleCondition`'s empty-string fallback
- [x] New cases live in `Birko.Data.SQL.SqLite.Tests/SqlExpressionParityTests.cs` — the compiled-delegate
      oracle is what found this and is the right home; hand-computed expectations are not
- [x] Red-verified with the split reported as numbers; contract pins named as pins
- [x] Blast radius measured before any refusal ships — if a computed operand starts throwing, a consumer
      model carrying one breaks at parse time (the TASK-112 precedent: cleared against 19 suites)
- [x] `/specs regen filter-expression-translation` — the area globs `DataBase*.cs`, so the diff is real
      evidence. Note that its § *SQL IN translation and empty sets* requirement currently says nothing about
      operand resolution, so this will likely ADD a requirement rather than change one

## Out of scope

- TASK-137's always-true reduction. Verified not to interact (§ Context).
- `String.Contains` (the `LIKE` path) — a different arm with a different operand story. Check it while you are
  there, and file separately if it shares the defect.
- Non-SQL backends. ElasticSearch and MongoDB translate `Contains` themselves; whether they mishandle a
  computed operand is the same question in a different translator and needs its own measurement.

## Human test plan

`N/A — fully covered by automated tests.` The compiled-delegate oracle compares SQL results against C#
semantics over a real database, which is a stronger check than any manual step, and there is no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-213` — leave empty until then._

## Outcome

`ids.Contains(x.Amount + 1)` did not translate to `(Amount + 1) IN (…)`. The `Contains` arm looped **every**
argument through `ParseConditionExpression`, so a *computed* operand was parsed as though it were a nested
**predicate**: it took the binary-comparison path and fabricated a **subcondition** (`Amount = 1`) on the very
condition being built. The renderer branches on `SubConditions` before it consults `Type`, so the `In` and its
values were then discarded and **a different predicate was emitted** — silently. Measured on SQLite:
`Ids.Contains(x.Amount + 1)` returned **1** row where C# returns **0**, and the negated form **3** where C#
returns **4**. Wrong in both directions, no exception, no log entry.

The operand is now resolved the way a comparison already resolves its column side (`BuildValueComparison`):
the parameter-referencing argument becomes the condition's `Name` as a raw SQL fragment via
`RenderValueFragment`, which covers arithmetic, `COALESCE`, `CASE`, `.Value` unwrap and inlined constants —
and which **throws** for anything it cannot express, so an untranslatable operand now fails loud instead of
becoming a different predicate.

### Step-6 split — 18 of 21 (full revert)

A **full** revert was possible here, unlike TASK-137: no test names any new API (`IsPlainColumnOperand` is
private), so the pre-fix tree compiles against the whole new suite and the split is honest without surgery.

| suite | new | failed | pins |
|---|---|---|---|
| `ComputedContainsOperandTests` (new) | 10 | **9** | 1 |
| `SqlExpressionParityTests` (11 new oracle cases) | 11 | **9** | 2 |

**Fix-dependent (18).** All of `ComputedContainsOperandTests` except the plain-column control — the two
`*_BecomesTheConditionName_*`, `NoComputedOperandShape_ProducesTheMalformedConditionAnyMore`, both
`AnUntranslatableOperand_*`, both `AnEmptyNegatedContains_*`, and both `ABoundedComputedContains_*`. In the
parity suite: `containsArith`, `containsArithNot`, `containsArithMatches`, `containsMultiply`,
`containsCoalesceNot`, `containsCoalesceHits`, `containsArithInOr`, `containsArithEmptyNot`,
`containsArithEmptyIn`.

**Contract pins — pins, NOT evidence (3),** each with the reason it cannot witness the fix:

- `APlainColumnOperand_IsUnchanged` — a pin **by design**. The fix is gated on "not a plain resolvable
  column" precisely so working shapes don't move; this asserts they didn't.
- `containsCoalesce` (`Ids.Contains(x.Score ?? 0)`) and `containsArithInAnd`
  (`x.Amount > 4 && Ids.Contains(x.Amount + 1)`) — **coincidental agreement on this seed**: both the correct
  answer and the fabricated predicate's answer are 0 rows. They pin C# parity and would be evidence on
  different data. The positive-match coverage for those same shapes (`containsCoalesceHits`,
  `containsArithMatches`) does fail on revert, so neither shape rests on a coincidence.

### The step-6 pass found a test that passed against the defect, and it was fixed mid-step

`AnEmptyNegatedContains_OverAComputedOperand_StillMatchesEveryRow` **passed on the first revert run**: the
seed had no NULL `Score`, and over non-null scores the fabricated `NOT (Score = 0)` happens to return exactly
the right rows. The seed now carries a NULL-`Score` row, which is load-bearing rather than filler — SQL's
three-valued logic excludes it (`NULL = 0` is UNKNOWN, `NOT UNKNOWN` is UNKNOWN) while C# counts it. That
took the split from 17 to 18 of 21. Same lesson as TASK-113's loop test: a test written to pin a fix is not
automatically evidence of it, and only the revert tells you which it is.

### Judgement calls, and the stricter option rejected

- **Translate, don't refuse — because the translator already existed.** § Approach framed this as an open
  "translate or refuse" decision and warned that refusing needs a measured blast radius. Neither was needed:
  `RenderValueFragment` already renders every shape in the § Context table and already throws for the rest,
  and `BuildValueComparison` was already doing exactly this for comparisons. So the fix is a **reuse** and the
  refusal is the pre-existing fallback, not a new policy. The written approach's cost model was pessimistic
  in the same direction TASK-112's was.
- **Gated on "not a plain resolvable column" rather than routing every operand through the fragment
  renderer.** The cleaner-looking option (one path for all operands) was rejected: it would move every
  working `set.Contains(x.Col)` in every consumer onto a new code path to fix shapes that were never broken.
  The two agree on plain columns anyway — both resolve through `ResolveColumnName(exprType, name, true)`, and
  the control test asserts the resulting name — so the narrow gate costs nothing and risks nothing.
- **The refusal is a real behavioural change and is recorded as one.** `ids.Contains(x.Name.Length)`
  previously returned rows selected by a substituted predicate; it now throws `NotSupportedException`. That is
  the § SH-H037 position (a mapper that cannot express something refuses), and the blast radius was measured
  across **22** SQL-touching suites with no failures — but a consumer with such a predicate will now see a
  parse-time throw where it previously got wrong rows. Noted in CLAUDE.md for consumers.
- **The malformed shape is asserted absent across eight shapes, not one.** The defect's signature was an `In`
  condition carrying `SubConditions`; a single "Name is not null" check on one input would be satisfied by
  almost any change.

### Flagged and not fixed

- **`String.Contains` (the `LIKE` arm) does not reach the changed code** — verified, and stated no wider than
  that. It takes the `isStringPatternMethod` branch, which parses only `Arguments[0]` and never enters the
  argument loop this fix modifies, so nothing here can have altered it. What was **not** established is that
  that branch is itself free of the same class of defect: `x.Title.Contains(x.OtherColumn)` puts a
  parameter-referencing expression in `Arguments[0]`, which is the shape that misbehaved on the `In` side.
  Unexamined, because a column-to-column `LIKE` may not be a supported translation at all and answering that
  needs its own measurement. § Out of scope's "check it while you are there" is discharged as *not affected by
  this change*, not as *checked and clean*.
- **ElasticSearch and MongoDB remain unexamined for the same class of defect** — both translate `Contains`
  themselves, so the question needs their own measurement. Left in § Out of scope; the MongoDB destructive
  half is already tracked as [[TASK-212]].
- **One more shape now throws where it previously produced nonsense: an UNMAPPED collection property as the
  collection argument.** `Enumerable.Contains(x.Tags, item)` — the extension form the compiler emits when
  `Tags` is an array/`IEnumerable<T>` rather than a `List<T>` — puts a parameter-referencing argument in the
  loop, so it now reaches `RenderValueFragment` and refuses. There is no working case being broken: an
  unmapped collection property is not a SQL column, so this could only ever have emitted wrong SQL. The
  *instance* form (`List<T>.Contains`) is untouched, because there the collection is
  `methodExpression.Object`, which is parsed after the loop and outside this change. A mapped `byte[]`
  property is also untouched — it resolves as a plain column and stays on the old path. Established by
  reading the two call shapes, and consistent with the 22-suite sweep.
- **Security check on the new sink, and a pre-existing portability wart it reaches.** The fragment is
  interpolated into `CommandText`, so it was reviewed as an injection surface: column names come from
  `ResolveColumnName` (metadata), and constants go through `InlineConstant`, which doubles `'`, handles
  bool/enum/numeric/string and *throws* for anything else. That is the same mechanism comparisons have shipped
  with, so this is a second door to an existing, parameter-free-but-escaped path rather than a new class of
  exposure. Noted while there: `RenderValueFragment` renders `ExpressionType.Add` as `+`, which for **string**
  operands is MSSQL-only syntax (`||` elsewhere) — pre-existing and reachable today through comparisons; this
  change adds a second way to reach it. Not filed, because it is neither introduced nor worsened here, but it
  is on the record.
- **A latent asymmetry noticed while reading the renderer, deliberately not touched:** `AppendConditionTo`
  prefers `SubConditions` over `Type` unconditionally, which is *why* this defect emitted a different
  predicate rather than a broken one. Making a condition that carries both a `Type` and children an outright
  error would catch the next such producer at the source. Not done here — it is a framework-wide invariant
  change needing its own blast-radius measurement, and this fix removes the only known producer. Worth a task
  if a second one ever appears.

## Progress log

- step 2 — picked; ranked above [[TASK-212]] which beats it on severity key 1 (an unbounded destructive
  write outranks wrong results), but which fails keys 4 and 5 **in this environment**: no MongoDB is
  reachable (port 27017 closed, `BIRKO_MONGO_HOST` unset), so its central measurement cannot run and a guard
  would ship on an unmeasured premise — against that task's own first acceptance criterion — while its
  "where does the portable predicate helper live" question is still open. This defect is already measured in
  both directions, sits in one parser arm, and its oracle harness exists.
- step 3 — verified: **holds exactly as filed, and the root cause is now named.** `DataBase.cs:662-680` loops
  **every** argument of a non-string `Contains` through `ParseConditionExpression(arg, condition, exprType)`.
  The collection argument sets `Values`; a *computed* value operand is then parsed as though it were a
  nested **predicate**, so `x.Amount + 1` takes the binary path and fabricates a subcondition
  `Amount Equal [1]` on the same condition. The renderer branches on `SubConditions` **first**
  (`AppendConditionTo`), so the `In` type and its `Values` are ignored entirely and only the fabricated
  equality is emitted — which is why the answer is a *different* predicate rather than a broken one.
  § Approach guessed this mechanism correctly.
  **The remedy is also better than § Approach assumed:** `RenderValueFragment` (`DataBase.cs:1200`) already
  renders arithmetic, `COALESCE`, `CASE`, `.Value` unwrap, columns and inlined constants, and already
  **throws `NotSupportedException`** for anything it cannot faithfully translate — the § SH-H037 discipline,
  pre-built. `BuildValueComparison` (`DataBase.cs:1329`) is the precedent: for a comparison, the
  parameter side *becomes the condition `Name`* as a raw fragment. So this is a **reuse**, not a new
  translator, and the "translate or refuse" decision the acceptance asked for is answered as **translate,
  with the existing throw as the fallback for the untranslatable**.
- step 4 — layer: **local**. Both the defective arm and the machinery that fixes it are in
  `Birko.Data.SQL/SQL/DataBase.cs`.
- step 5 — fix in `../Birko.Data.SQL/SQL/DataBase.cs` (the `In` arm resolves a parameter-referencing,
  non-plain-column operand through `RenderValueFragment` into `condition.Name`; new private
  `IsPlainColumnOperand` gate). Tests in `Birko.Data.SQL.SqLite.Tests/ComputedContainsOperandTests.cs` (new,
  10) and `SqlExpressionParityTests.cs` (11 new oracle cases). Suites green: **194/194** and **484/484**.
  Blast radius cleared against **22** SQL-touching suites — all pass.
- step 6 — full revert (possible here: no test names new API, unlike TASK-137): **18 of 21 failed**. One
  test was strengthened mid-step because it *passed* against the defect — see § Outcome. Fix restored, both
  suites re-verified green.
