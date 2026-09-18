---
id: TASK-447
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-09-16
depends-on: []
blocks: []
# findings: field-found during TASK-322 rather than harvested, so it carries no SH-* id. It is in
# fix-next's pool via EPIC-014's `kind: review-intake` stamp, not via this list.
findings: []
pr: "Birko.Data.CosmosDB.Views d85ae23 · tests 54cad89"
github-issue: null
jira-key: null
---

# A Cosmos view filter's string value breaks out of its own quotes

## Context

Found while fixing [[TASK-322]] (`SH-H055`) in the same method family, and spawned rather than folded
in because the root cause is different: that finding is about a swallowed *translation failure*, this
is about *escaping*.

`CosmosFilterTranslator.TranslateValue` (`Birko.Data.CosmosDB.Views/CosmosViewStore.cs`) renders a
string operand by interpolating it into a single-quoted Cosmos SQL literal, escaping **only** the
quote:

```csharp
string s => $"'{s.Replace("'", "\\'")}'",
```

Cosmos SQL uses backslash as the escape character inside a string literal, so a backslash in the
**input** is not escaped and consumes whatever follows it — including the escape the code just added.

**Measured 2026-09-16** by rendering through `TranslateValue` directly (offline, no account needed):

| Input | Rendered | Result |
|---|---|---|
| `O'Brien` | `'O\'Brien'` | correct — this is the case the existing test covers |
| `foo\` | `'foo\'` | **unterminated literal** — the `\'` escapes the closing quote |
| `a\' OR 1=1 --` | `'a\\' OR 1=1 --'` | **injection** — the literal ends at `\\'`, and ` OR 1=1 --` is parsed as SQL, with `--` commenting out the dangling quote |
| `x` + newline + `Y` | literal newline passed through | unescaped control character in the statement |

Row 3 is the whole finding: a filter value reaches the WHERE clause as **executable SQL**. On an
aggregate view the emitted predicate is the only thing scoping the query, so `OR 1=1` widens it to
every document in the container — the same end state [[TASK-322]] just closed, reached through a
different door and *with attacker control over the predicate*.

**Why this is not covered by the existing test.** `CosmosViewTranslateValueTests` asserts
`Translate("O'Brien") == "'O\\'Brien'"` — the one input where escaping only the quote is correct. No
test supplies a backslash. CLAUDE.md § TASK-284 records this shape: *when a defect survives a
well-tested area, check whether a test is asserting it.* Here the test is not asserting the defect, it
is asserting the single case that hides it.

**Measured reach, 2026-09-16.** Latent but wired: `CosmosViewStore` is constructed by Symbio's
`ViewStoreFactory.CreateCosmosStore` (`src/Core/Symbio.DataAccess/Views/ViewStoreFactory.cs:166`) from
a `DataProvider.CosmosDB` switch case, and every Symbio environment is configured `"Default": "SQLite"`.
So no deployment reaches it today and a configuration change is all that stands between.

⚠ **Rated P0 anyway, deliberately.** STORY-051's calibration puts cross-tenant leakage at P0, and an
injection sink in the only clause that scopes an aggregate query is that, with attacker control on top.
CLAUDE.md § TASK-219/256: a latent defect's window *"closes the moment a consumer selects the backend"*,
and here that is one configuration value. Latency is a reason not to overstate urgency in a report, not
a reason to downgrade.

## Acceptance criteria

- [x] A string operand containing a backslash, a quote, or both round-trips as its **literal value** —
      asserted against the rendered SQL, with the four measured inputs above as the minimum set
- [x] The `OR 1=1 --` payload is asserted to produce a statement where the payload is **inside** the
      literal, not beside it. The assertion is the emitted SQL, never "no exception was thrown"
- [x] ⚠ Decide and record whether escaping is the right containment at all, or whether these values
      should be **parameterised**. `QueryDefinition.WithParameter` exists and both call sites already
      build a `QueryDefinition`, so unlike the identifier family in § Conventions this sink has a real
      parameter mechanism available — and CLAUDE.md is consistent that parameterising beats escaping
      where the grammar allows it. Escaping is the smaller diff; say which was chosen and why
- [x] Control characters (newline, tab, `\0`) are handled explicitly, whichever containment is chosen
- [x] The other `TranslateValue` arms are re-checked against the same question — in particular the
      `_ => value.ToString()!` fallback, which emits **unquoted** text for any type not listed and is
      the same sink for a custom type
- [x] ⚠ Check whether `CosmosViewManager` or the Cosmos **migration** emitters
      (`Birko.Data.Migrations.CosmosDB`) interpolate caller values the same way. CLAUDE.md § TASK-253
      records that `Birko.Data.Migrations.CosmosDB` is the **one** site deliberately left out of the
      `SqlLiteral.EscapeLiteral` convergence, so it is the obvious second instance
- [x] Each fix is red-verified: the payload test fails against the current escaping

## Out of scope

- `SH-H055`'s swallowed translation failure — [[TASK-322]] owns it and it is fixed. This task inherits
  a translator that now throws rather than silently dropping the clause, which is what makes an
  assertion about the *emitted* clause meaningful at all.
- The non-aggregate LINQ path (`GetItemLinqQueryable().Where(filter)`), which hands the predicate to
  the driver and never reaches this string builder.
- The SQL-provider identifier family in CLAUDE.md § Conventions. That is about *identifiers*, which
  cannot be parameterised; this is a *value*, which can — do not import the "escape it" conclusion
  from there without re-deciding.

## Human test plan

**N/A - fully covered by automated tests.** The deliverable is a query builder; the assertions are the
emitted `QueryDefinition`, read back offline. No deployment selects `DataProvider.CosmosDB`, so there
is nothing a human could exercise today.

⚠ Two things a human owes this when a consumer does select Cosmos, both recorded rather than assumed:
run one real aggregate query with a string filter and confirm the parameter round-trips; and confirm a
`DateTime`/`Guid` filter still matches stored documents, since their wire format is now the SDK
serializer's rather than this code's and no offline test can observe it.

## Implementation plan

_Populated by `/tasks plan TASK-447` — leave empty until then._

## Progress log

- step 2 - picked; ranked above TASK-313 (entity-localization) on keys 1 and 2: an injection sink in
  the only clause that scopes an aggregate query is cross-tenant leakage **with attacker control**
  (band 2), against TASK-313's silent corruption of a default-culture column (band 3); and it is
  reachable from untrusted input, where TASK-313 needs ordinary API use. ⚠ Correcting my own call:
  the previous run named TASK-313 as next pick without re-ranking to include this task, which that
  same run had spawned.
- step 3 - re-verified my own filing with the same scepticism as a harvested one. The **emission** is
  measured (the four-row table above, re-confirmed); the **parse** is from Cosmos NoSQL's documented
  literal grammar (backslash is the escape character, `--` is a line comment) and is *not* executed,
  because that needs an account. Said plainly rather than implied: `'a\' OR 1=1 --'` lexes as the
  string `a\`, then ` OR 1=1 `, then a comment - so the predicate reduces to `... OR 1=1`.
- step 3b - **criterion 6 answered, and it found a second instance that is wrong a DIFFERENT way.**
  `CosmosViewManager` interpolates nothing. But
  `Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.FormatSqlValue:246` escapes a string
  with **SQL-standard `''` doubling** (`s.Replace("'", "''")`) where the view store uses backslash -
  two mutually incompatible escapers targeting the same dialect, so at most one can be right
  (§ TASK-274, *two doors onto one feature must give one answer*). Its output is interpolated into
  `SELECT ... WHERE {whereClause}` at three sites, each wrapped in a bare `new QueryDefinition(query)`.
  **Spawned rather than grouped** - separate repo, separate plumbing, and its own test file asserts the
  rendered string shape, so folding it in would double the session. § TASK-253 had already flagged this
  file as the one site deliberately left out of the `SqlLiteral.EscapeLiteral` convergence.
- step 4 - layer: **local**, in `Birko.Data.CosmosDB.Views`.
- step 4b - **containment decided: PARAMETERISE, not escape** (criterion 3). Both execution sites
  already do `new QueryDefinition(sql)`, so `WithParameter` is available and no public API moves.
  The framework's own recorded preference settles it: § TASK-308/SH-H028 is *prefer removing the
  grammar to escaping it* - escaping is a blacklist against a documented set that can grow, a bound
  parameter has no grammar at all. A second argument found while deciding: the SDK serializes a
  parameter with the **same serializer that wrote the document**, so CR-M086's hand-matching of enum
  and DateTime formats stops being a guess about System.Text.Json and becomes automatic.
  ⚠ The honest cost is recorded with it - see the Outcome.
- step 5 - fix in `Birko.Data.CosmosDB.Views/CosmosViewStore.cs`. `TranslateValue` (render a literal)
  became `EvaluateValue` (extract the CLR value) + `BindValue` (bind it as `@pN`); `Translate` and the
  recursion thread a parameter collector; `BuildAggregateSql` / `BuildCountAggregateSql` now return a
  **`QueryDefinition`** built by one shared `Bind` helper, and the two execution sites use it directly
  instead of wrapping a string. The hand-written literal switch is deleted. `OFFSET`/`LIMIT` stay
  interpolated **deliberately** - both are `int?` from this store's own API, so no caller text reaches
  them. Tests: new `CosmosViewFilterInjectionTests` (11), `CosmosViewTranslateValueTests` inverted (6),
  two helper lines updated in `CosmosViewFilterFailOpenTests` and `CosmosViewAggregateSqlTests`.
  Suite **35/35**, 0 warnings under `-warnaserror`.
- step 5b - **criterion 5 answered structurally, not by inspection.** The `_ => value.ToString()!`
  fallback - which emitted *unquoted* text for any unlisted type - no longer exists: there is no
  rendering layer at all, so every CLR value is bound. That is a stronger answer than fixing the arm.
- step 6 - three mutations, all reverted:
  **(A) strings rendered as escaped literals again** -> **19 of 35** red, both payload theories in full
  plus the ordering, `CONTAINS`, placeholder-name and verbatim-value tests.
  **(B) the enum -> numeric conversion removed** -> **1 of 35** red.
  **(C) `Bind` stops attaching the collected parameters** -> **17 of 35** red; this is the half that
  proves the statement and the values stay connected, which (A) alone does not.
  **⚠ Mutation B is the one worth recording, because its FIRST run failed nothing.** My enum test used
  the ordinary `v.State == Status.Published` shape - and measurement showed C# builds that as
  `Convert(v.State, Int32) == Convert(2, Int32)`, so the operand evaluates to a boxed **Int32** and
  never reaches the `is Enum` branch. The branch is reachable only through an operand whose static type
  is `object`. The test was retargeted to that shape and the mutation now reds; the ordinary shape is
  kept as a second test recording that the compiler, not this code, satisfies CR-M086 there. A mutation
  that fails nothing is a missing test (§ TASK-261) - here it was a test aimed at the wrong shape.

- step 7 - respecced `views-and-aggregation`. The requirement's value paragraph was rewritten
  (*"Literal values SHALL be inlined ..."* -> *"... SHALL be bound as a query parameter"*, plus the
  enum rule and the deliberate `OFFSET`/`LIMIT` exception); *Values are inlined, not parameterized* ->
  *Values are parameterized, not inlined*; three scenarios added (payload cannot widen, ordered
  binding, binds-nothing). ⚠ **Reviewing the diff caught a stale scenario the edit had missed** -
  *Enum and DateTime literals are emitted as valid SQL* still described the old rendering - which is
  what the step-7 diff review is for. Stamp and the `../Birko.Data.CosmosDB.Views` source-commit
  refreshed.
- step 7b - `CLAUDE.md` gains its `### Recent Updates` entry (convention gate check 9).
- step 8 - closed done; out-of-scope sweep: 3 boundary, 1 spawned ([[TASK-450]] P1), 0 declined.

## Outcome

**What was wrong.** `CosmosFilterTranslator` rendered a string filter value into the statement as a
single-quoted literal, escaping only the quote. Cosmos NoSQL uses **backslash** as the escape
character inside a literal, so a backslash in the *input* consumed the escape the code had just
added - measured, `a\' OR 1=1 --` rendered as `'a\\' OR 1=1 --'`, where the literal ends early and
the remainder is parsed as SQL. On an aggregate view the predicate is the only thing scoping the
query, so a caller-supplied value could widen it to every document.

**What was done.** Values are **bound as query parameters** rather than rendered.
`TranslateValue` split into `EvaluateValue` (extract the CLR value) and `BindValue` (bind `@pN`); the
recursion threads a collector; both builders return a `QueryDefinition` produced by one shared `Bind`
helper. The hand-written literal formatter - including the `_ => value.ToString()` fallback that
emitted *unquoted* text for any unlisted type - is deleted.

**Step-6 split.** Three mutations: strings rendered as literals again -> **19 of 35** red; the
enum conversion removed -> **1 of 35**; `Bind` not attaching parameters -> **17 of 35**. The third
matters on its own: it is what proves the statement and the values stay connected, which the first
does not.

**Judgement calls, and why the stricter option lost.**

- **Parameterise, not escape** (the task's criterion 3 asked for this decision on the record). Escaping
  Cosmos's literal grammar correctly is achievable, and it is the smaller diff. It was rejected because
  it keeps a blacklist against a grammar that can grow, and because the parameter mechanism was already
  present at both call sites and simply unused.
- **The enum conversion is kept, the rest of the formatting is not.** Delegating everything reads
  cleaner. It is wrong for the enum specifically: Birko's own Cosmos store writes enums numerically
  while the driver would use its own converter, so binding a bare enum could silently match nothing.
  That is CR-M086's finding, preserved deliberately while its siblings were retired.
- **`OFFSET`/`LIMIT` stay interpolated.** Parameterising them would be consistent and would also be an
  unverifiable change to a path with no caller text in it.
- **The migrator was spawned, not grouped.** Separate repo, separate plumbing, its own test file
  pinning the rendered string, and - decisively - a *different* escaping scheme whose behaviour is not
  yet measured. Folding it in would have doubled the session and imported a conclusion that has not
  been established there.

**Flagged, and spawned.** [[TASK-450]] (P1) - `Birko.Data.Migrations.CosmosDB`'s `FormatSqlValue`
escapes with SQL-standard `''` doubling, which Cosmos does not use. Two incompatible escapers for one
dialect, so at most one can be right. Its severity is deliberately left open until someone measures
what Cosmos does with `'O''Brien'`.
