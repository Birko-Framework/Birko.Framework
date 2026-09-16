---
id: TASK-448
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-16
depends-on: []
blocks: []
# findings: field-found while closing TASK-322, so no SH-* id. In fix-next's pool via EPIC-014's
# `kind: review-intake` stamp.
findings: []
pr: null
github-issue: null
jira-key: null
---

# Two § Conventions entries disagree about what a rendered expression tree leaks, and one sink acts on the wrong one

## Context

Grouped from two things found while closing [[TASK-322]], because they are one subject: what
`Expression.ToString()` actually exposes, and the one place the framework interpolates it.

### 1. The rulebook contradicts itself

`CLAUDE.md` § Conventions carries two statements about the same mechanism:

- **§ TASK-308** — *"⚠ The refusal message carries the node's TYPE, never `expr.ToString()`. **A
  rendered expression tree interpolates the values a closure captured**, and such a message travels
  into logs and error responses."*
- **§ TASK-310** — *"A closure-captured local renders as `value(<>c__DisplayClass…).field` —
  **measured, byte-identical for every value**."*

Both cannot be right as written, and a reader reaching for either will reason about a security
boundary from it.

**Measured 2026-09-16** (offline, a plain `Expression<Func<V,bool>>` rendered with `ToString()`):

| Filter | Rendered |
|---|---|
| captured local `secret` | `v => (v.Name == value(…ZZProbe2+<>c__DisplayClass3_0).secret)` |
| inline literal | `v => (v.Name == "hunter2-INLINE-LITERAL")` |

So **TASK-310 is right for a raw tree** — a captured value is *not* rendered — and TASK-308's wording
holds only for a **normalized** tree, where `ExpressionNormalizer`'s funcletization has already folded
the capture to its value (TASK-310 records exactly that folding). The real exposure of a raw tree is
**inline literals**, plus type and member names.

That distinction matters in both directions: the current wording *overstates* the risk for a raw tree
(and could get a useful diagnostic removed), and *understates* it for any sink that normalizes first.

### 2. A sink that interpolates the expression

`Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:333`:

```csharp
return query ?? throw new NotSupportedException(
    $"The filter expression could not be translated to an ElasticSearch query: {filter}.");
```

Per the measurement this leaks inline literal values and the view's type/member names into whatever
logs the exception — not captured values. Low severity, and worth deciding deliberately rather than
leaving as the one place the framework does the thing § TASK-308 says not to do.

⚠ **Its own comment says the branch is unreachable** (*"the helper throws instead of returning null;
kept as a belt-and-braces assertion"*), so this may be a message that never renders. **Verify that
before spending anything on it** — § TASK-261's *defensive, not witnessed* distinction, and if it is
genuinely unreachable the honest fix may be to say so rather than to reword it.

## Acceptance criteria

- [ ] `CLAUDE.md` § Conventions states, once and correctly, what a rendered expression tree exposes:
      a **raw** tree renders a captured local as a display-class field reference and an inline literal
      verbatim; a **normalized** tree renders the folded value. Both TASK-308's and TASK-310's entries
      point at that one statement instead of each making their own claim
- [ ] The correction is anchored to the measurement (the table above), not to a re-reading of either
      entry — this task exists because two prose claims were trusted over a measurement
- [ ] `ElasticSearchViewStore.cs:333` is either left as-is with a recorded reason, or changed to carry
      the shape rather than the rendered expression. **Establish first whether the branch is
      reachable**; if it is not, prefer saying so over rewording a string nothing emits
- [ ] Any *other* site interpolating an `Expression` into a message, a log or a cache key is swept for
      and listed — the sweep's result is the deliverable even if it is empty
- [ ] If a behavioural change lands, it is red-verified; if the outcome is documentation only, that is
      stated plainly rather than dressed as a fix

## Out of scope

- `Birko.Data.Caching`'s key construction, which [[TASK-310]] already settled: a rendering that cannot
  describe its values is refused rather than cached. This task does not revisit that decision.
- The `SH-H055` fail-open — [[TASK-322]] owns it and it is fixed. Its refusal message already carries
  `NodeType` only, which is correct under either reading, so nothing here changes that code.
- Whether expression trees should reach diagnostics at all. That is a bigger API question; this task
  only reconciles a contradiction and checks one sink.

## Human test plan

_Resolve before `/tasks close`. Expected `N/A` — a documentation correction plus at most a one-line
message change, both assertable offline._

## Implementation plan

_Populated by `/tasks plan TASK-448` — leave empty until then._
