---
id: TASK-222
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-221, TASK-218]
pr: null
github-issue: null
jira-key: null
findings: []
---

# RavenDB diverges on 6 filter shapes — and one of them is a **silent wrong answer**

## Context

Found by [[TASK-221]], which made `Stores/RavenFilterMatrixLiveTests` runnable for the first time. That
suite is gated on `BIRKO_RAVEN_URL` **and** was broken in its own setup (`ReadAsync(x => true)`, which
Raven refuses), so it had never reported a single shape. First run ever, against RavenDB in Docker:
**21 of 27 OK**, and these six:

| shape | result | severity |
|---|---|---|
| `ternary` — `x => x.Amount > 4 ? x.Active : x.Score == null` | **DIVERGE oracle=1 actual=6** | **silently wrong** |
| `coalesceCmp` — `x => (x.Score ?? 0) > 15` | `InvalidOperationException: Could not understand how to translate '(x.Score ?? 0)'` | loud |
| `arithAdd` — `x => x.Amount + (x.Score ?? 0) > 10` | same shape of error | loud |
| `arithMul` — `x => x.Amount * 2 >= 10` | same shape of error | loud |
| `toLowerEq` — `x => x.Name.ToLower() == "beta"` | `NotSupportedException` | loud |
| `contains` — `x => x.Name.Contains("et")` | `NotSupportedException` — Raven refuses substring search **on purpose** and names `Search()` | arguably correct |

**`ternary` is the one that matters.** It returns **every** document where C# semantics say one. No
exception, no log — the shape most likely to be written by a consumer expressing "if A then B else C",
and it silently answers the wrong question. It outranks the five loud ones on its own.

## The fix probably already exists

`Birko.Data.Expressions.ExpressionNormalizer` was built to desugar exactly these constructs — boolean
ternary into `(c && t) || (!c && f)`, boolean `??` into boolean algebra, and to funcletize
parameter-free arithmetic — for parsers that cannot handle them. Its own doc comment says:

> Compiled-delegate backends (InMemory / JSON / XML) and native-LINQ backends (Mongo / Cosmos / Raven)
> never need it — they honour the raw constructs already

That claim is **measurably false for RavenDB**, and the four ternary/coalesce/arithmetic rows above are
precisely what the normalizer exists to fix. Running it for Raven is the obvious first candidate, and
would likely close 4 of the 6 in one line.

⚠ **Verify the claim for Mongo and Cosmos too while you are there.** The same sentence covers them, and
their matrix suites report `ternary`, `coalesceCmp`, `arithAdd`, `arithMul` as OK — so it holds there.
Do not widen the normalizer to them without measuring; the point is that the comment overgeneralised
from two backends to three.

## Approach

1. Run `ExpressionNormalizer.Normalize` on the filter in `RavenSetMembership.Rewrite` (or beside it) and
   re-run the matrix. Expect `ternary`, `coalesceCmp`, `arithAdd`, `arithMul` to move to OK.
2. `toLowerEq` needs Raven's own case handling — its analyzers, not an expression rewrite. Decide
   whether to translate or to refuse with a message naming the supported approach.
3. `contains` (substring) is Raven refusing deliberately with good guidance. Recommend **leaving it** and
   recording it as intended, not as a divergence — but say so explicitly rather than by omission.
4. Correct `ExpressionNormalizer`'s doc comment either way.

## Acceptance criteria

- [ ] `ternary` returns the same set as the compiled-delegate oracle — this is the only silently-wrong
      one and must not be closed by making the suite tolerate it
- [ ] The three computed-operand shapes translate, or refuse with a message naming what to write instead
- [ ] `toLowerEq` and `contains` are each resolved as translate / refuse-with-guidance / intended, with
      the decision recorded — no shape left unexplained
- [ ] `Stores/RavenFilterMatrixLiveTests` reports 27 of 27, **or** carries an explicit known-divergence
      ledger naming this task for each exception, so a divergence that later starts passing fails the run
      rather than silently masking a regression
- [ ] `ExpressionNormalizer`'s doc comment no longer claims native-LINQ backends never need it
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- [[TASK-221]]'s set-membership and all-rows rewrites, which are what let this suite run at all.

## Human test plan

- [ ] `docker run --rm -p 8080:8080 -e RAVEN_Setup_Mode=None -e RAVEN_License_Eula_Accepted=true
      -e RAVEN_Security_UnsecuredAccessAllowed=PublicNetwork ravendb/ravendb`, then
      `BIRKO_RAVEN_URL=http://127.0.0.1:8080` and run the suite. Read the whole 27-shape report, not the
      pass/fail — a fix that breaks a different shape shows up in the same output.

## Implementation plan

_Populated by `/tasks plan TASK-222` — leave empty until then._
