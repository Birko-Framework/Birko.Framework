---
id: TASK-447
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P0
assignee: ai
created: 2026-09-16
depends-on: []
blocks: []
# findings: field-found during TASK-322 rather than harvested, so it carries no SH-* id. It is in
# fix-next's pool via EPIC-014's `kind: review-intake` stamp, not via this list.
findings: []
pr: null
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

- [ ] A string operand containing a backslash, a quote, or both round-trips as its **literal value** —
      asserted against the rendered SQL, with the four measured inputs above as the minimum set
- [ ] The `OR 1=1 --` payload is asserted to produce a statement where the payload is **inside** the
      literal, not beside it. The assertion is the emitted SQL, never "no exception was thrown"
- [ ] ⚠ Decide and record whether escaping is the right containment at all, or whether these values
      should be **parameterised**. `QueryDefinition.WithParameter` exists and both call sites already
      build a `QueryDefinition`, so unlike the identifier family in § Conventions this sink has a real
      parameter mechanism available — and CLAUDE.md is consistent that parameterising beats escaping
      where the grammar allows it. Escaping is the smaller diff; say which was chosen and why
- [ ] Control characters (newline, tab, `\0`) are handled explicitly, whichever containment is chosen
- [ ] The other `TranslateValue` arms are re-checked against the same question — in particular the
      `_ => value.ToString()!` fallback, which emits **unquoted** text for any type not listed and is
      the same sink for a custom type
- [ ] ⚠ Check whether `CosmosViewManager` or the Cosmos **migration** emitters
      (`Birko.Data.Migrations.CosmosDB`) interpolate caller values the same way. CLAUDE.md § TASK-253
      records that `Birko.Data.Migrations.CosmosDB` is the **one** site deliberately left out of the
      `SqlLiteral.EscapeLiteral` convergence, so it is the obvious second instance
- [ ] Each fix is red-verified: the payload test fails against the current escaping

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

_Resolve before `/tasks close`. Expected `N/A` — the emitted SQL is assertable offline — but if the
chosen containment is parameterisation, a live Cosmos round-trip may be worth one recorded step._

## Implementation plan

_Populated by `/tasks plan TASK-447` — leave empty until then._
