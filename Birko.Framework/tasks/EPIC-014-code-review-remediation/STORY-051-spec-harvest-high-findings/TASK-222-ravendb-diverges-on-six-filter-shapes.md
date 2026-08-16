---
id: TASK-222
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-221, TASK-218]
pr: [Birko.Data.Core@8697c0d, Birko.Data.RavenDB@eedaa09, Birko.Data.Core.Tests@8875a5b, Birko.Data.RavenDB.Tests@d1a731b]
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

## Measurement (2026-08-16, step 3) — the filed hypothesis was wrong, and the defect is worse

The task guessed `ExpressionNormalizer` would close **4 of the 6**. Measured: it closes **1** — and that
one is the only one that mattered. Each shape rendered raw and normalized, against RavenDB in Docker:

| shape | raw | normalized |
|---|---|---|
| `ternary` | **`from 'Docs'`** — no `where` at all | `where (Amount > $p0 and Active = $p1) or (...)` ✅ |
| `coalesceCmp` / `arithAdd` / `arithMul` | throws | **unchanged** — the normalizer keeps non-boolean coalesce/arithmetic intact *by design*, for a value parser Raven does not have |
| `toLowerEq` / `contains` | throws | unchanged |

**The `ternary` row is not a mis-translation, it is a dropped predicate.** RavenDB emits no `where`
clause and returns every document. The task described it as "6 rows where C# says 1"; the mechanism is
that the filter vanished.

**Probing the silent class specifically found three more, none of them filed:**

| shape | raw RQL |
|---|---|
| `x.Active && (c ? a : b)` | **`from 'Docs' where Active = $p0 and`** — malformed, trailing `and` |
| nested ternary | `from 'Docs'` — no `where` |
| `!(c ? a : b)` | **`from 'Docs' where (true and)`** — malformed |
| `c ? true : false` | `from 'Docs'` — no `where` |

So the real defect is **every boolean ternary**, producing either a silently-absent predicate or
malformed RQL — never a refusal. The five loud shapes are the visible minority.

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

- [x] `ternary` returns the same set as the compiled-delegate oracle — this is the only silently-wrong
      one and must not be closed by making the suite tolerate it. **Fixed, not tolerated**, along with
      three more silent/malformed ternary shapes the task had not found
- [x] The three computed-operand shapes translate, or refuse with a message naming what to write instead
      — **accepted as refusals.** They need a Raven *static index*, which no expression rewrite can
      produce; Raven's own message already names the untranslatable operand
      (`Could not understand how to translate '(x.Score ?? 0)'`). Recorded in the ledger
- [x] `toLowerEq` and `contains` are each resolved as translate / refuse-with-guidance / intended, with
      the decision recorded — no shape left unexplained. `contains` = **intended** (Raven refuses
      substring search deliberately and names `Search()`); `toLowerEq` = refused, case handling belongs
      to a Raven analyzer
- [x] `Stores/RavenFilterMatrixLiveTests` reports 27 of 27, **or** carries an explicit known-divergence
      ledger naming this task for each exception, so a divergence that later starts passing fails the run
      rather than silently masking a regression. **Ledger**, 5 entries, each with its reason; the suite
      fails both on an unlisted divergence and on a listed one that starts passing. Now **29 shapes**
      (two added for the malformed-RQL cases), 24 OK + 5 accepted
- [x] `ExpressionNormalizer`'s doc comment no longer claims native-LINQ backends never need it — and
      does not simply invert the claim: MongoDB is recorded as **measured** to honour the constructs,
      CosmosDB as **unverified** (its matrix is gated and has never run)
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6

## Out of scope

- [[TASK-221]]'s set-membership and all-rows rewrites, which are what let this suite run at all.

## Human test plan

- [x] `docker run --rm -p 8080:8080 -e RAVEN_Setup_Mode=None -e RAVEN_License_Eula_Accepted=true
      -e RAVEN_Security_UnsecuredAccessAllowed=PublicNetwork ravendb/ravendb`, then
      `BIRKO_RAVEN_URL=http://127.0.0.1:8080` and run the suite. Read the whole 27-shape report, not the
      pass/fail — a fix that breaks a different shape shows up in the same output. **Done**; the full
      report was read at every step, which is how the three unfiled silent shapes were noticed.

## Outcome

**What was broken.** RavenDB does not *reject* a boolean ternary — it silently emits **no `where` clause
at all**, so `x => c ? a : b` matched every document, or emits malformed RQL (`where Active = $p0 and`).
Four such shapes, all silent or malformed, none of which throws. Five further shapes throw loudly.

**The fix, in two parts.**

1. **`ExpressionNormalizer` now runs for RavenDB.** It already existed to desugar exactly boolean ternary
   and `??` into AND/OR/NOT, for parsers that cannot handle them — and its own doc comment excluded the
   native-LINQ backends, which was measured false for Raven. One line in `RavenFilterRewriter`.
2. **Boolean-constant reduction added to the normalizer.** Its expansions leave `X && true` /
   `X && false`; a literal-branch ternary (`c ? true : false`) yields nothing else. RavenDB renders the
   unreduced form as the malformed `where (Score = $p0 and)`. `X && true` → `X`, `X && false` → `false`,
   and the Or duals. This is the part that touches SQL and ElasticSearch too, which have consumed the
   normalizer all along — cleared against both, plus SQLite end-to-end.

`RavenSetMembership` was **renamed `RavenFilterRewriter`**: it now does three things (set membership,
all-rows, normalization) and a class named for one of them misleads.

**Judgement calls.**

- **Fixed the silent class; accepted the loud one.** The five throwing shapes need a Raven static index
  or an analyzer — neither is an expression rewrite — and Raven's messages already name the
  untranslatable operand. Accepting them is recorded per-shape in a ledger, not by deleting the shapes.
- **The ledger fails in both directions.** An unlisted divergence fails the run, *and* so does a listed
  one that starts passing — otherwise an entry silently becomes a blanket and masks the next regression
  in that shape.
- **`contains` is recorded as INTENDED, not as a defect.** Raven refuses substring search deliberately
  and names `Search()`; that is better guidance than any rewrite would emit.
- **Added two shapes to the matrix rather than relying on the Core unit tests.** Reverting the reduction
  failed 2 of 70 in Core but left Raven green at 51/51 — the live suite had no literal-branch ternary, so
  it did not pin the very reduction that fixes malformed RQL. `ternLiteral` and `ternConj` close that.
- **Did not invert the corrected doc comment into a new overclaim.** MongoDB is recorded as measured;
  CosmosDB as unverified, because its matrix is gated and has never run. Replacing one unsupported
  generalisation with another would have been the easy mistake.

**Flagged, not fixed.**

- **`CosmosFilterMatrixLiveTests` has still never run** (gated on `BIRKO_COSMOS_CONNECTION`). Every
  defect in this five-task thread was found by running a suite that had never run; this is the last one
  in the family still dark, and the Cosmos half of the normalizer claim rests on it.
- The five accepted Raven refusals remain refusals. If a consumer needs computed operands on RavenDB,
  that is a static-index feature and its own piece of work.

## Implementation plan

_Populated by `/tasks plan TASK-222` — leave empty until then._

## Progress log

- step 2 — picked; user-directed. Ranks on its own anyway: `ternary` is the only SILENT wrong answer left in the family, and a silent wrong answer outranks the five loud refusals beside it.
- step 3 — verified, and it CORRECTED the task's own hypothesis: ExpressionNormalizer closes 1 of the 6, not 4. Probing the silent class found three more unfiled shapes (ternary as a conjunct, nested ternary, negated ternary) plus a literal-branch ternary, all producing absent or malformed RQL rather than a refusal.
- step 4 — layer: Core for the boolean-constant reduction (it fixes a normalizer output shape, and SQL/ElasticSearch consume the same normalizer), Birko.Data.RavenDB for the wiring.
- step 5 — fix in Birko.Data.Core/Expressions/ExpressionNormalizer.cs (reduction + corrected doc comment) and Birko.Data.RavenDB/Expressions/RavenFilterRewriter.cs (renamed from RavenSetMembership, now also normalizes); tests in Birko.Data.Core.Tests/BooleanConstantSimplificationTests.cs (new, 11, non-gated) and two shapes added to Stores/RavenFilterMatrixLiveTests plus its Accepted ledger.
- step 6 — two isolating reverts. Revert A (drop normalization from the Raven path): 1 of 51 failed — the matrix suite, fix-dependent. Revert B (drop boolean-constant reduction from the normalizer): 2 of 70 failed in Core = A_ternary_with_literal_branches_collapses_to_its_test and A_ternary_with_inverted_literal_branches_collapses_to_the_negated_test; Raven stayed 51/51, which EXPOSED that the live suite did not pin the reduction — fixed by adding ternLiteral and ternConj to the matrix. Contract pins, passing either way: the meaning-preserving theory (8 cases, oracle = the ORIGINAL lambda compiled, so it pins that the reduction is a pure shape change), A_predicate_with_real_branches_still_expands_to_boolean_algebra (pins that the reduction did not swallow the expansion SQL/ES depend on), and all 500 SQL + 129 ElasticSearch + 194 SQLite tests — those three being the evidence the shared-normalizer change was safe. Fixed state: 1,147 tests green across 8 suites.
- step 7 — respecced filter-expression-translation: the normalizer requirement gains boolean-constant reduction, and RavenDB's requirement gains the pre-pass and the accepted-refusal list.
- step 8 — closed done; 8697c0d + eedaa09 (production, two repos) / 8875a5b + d1a731b (tests, two repos). Merge gate: my files build warning-clean (two pre-existing CS8625/CS8618 in untouched SQL/ES test files are not mine and were left); no new cross-cutting pattern — this extends the existing normalizer and the TASK-221 rewriter, and CLAUDE.md gains what the silent-vs-loud distinction taught. security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; the change makes a previously-absent WHERE clause present, i.e. strictly narrows result sets rather than widening them.
