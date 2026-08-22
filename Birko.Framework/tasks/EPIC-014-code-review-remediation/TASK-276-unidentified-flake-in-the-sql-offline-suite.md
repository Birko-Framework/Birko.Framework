---
id: TASK-276
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-273, TASK-259]
findings: []
pr: null
github-issue: null
jira-key: null
---

# One test in `Birko.Data.SQL.Tests` fails about 10% of full-suite runs, and its identity was never captured

## Context — found at TASK-273's close gate, and plausibly caused by it

Running the six SQL suites for TASK-273's final tally, `Birko.Data.SQL.Tests` reported **1 failed of 619**.
It has now been chased as far as repeated running can take it, measured 2026-08-22:

| Configuration | Result |
|---|---|
| full suite (619 tests), ~19 runs | **2 failures**, each 1 of 619 |
| full suite, 8 runs with `--logger trx` to capture the name | **8 clean** — never caught |
| `--filter FullyQualifiedName~IndexPredicateTests` alone (22 tests), 6 runs | 6 clean |
| `--filter FullyQualifiedName!~IndexPredicateTests` (597 tests), 5 runs | 5 clean |

So it needs the new class **and** the rest of the suite in one run, which is what makes a cross-class
interaction the leading hypothesis rather than a timing-sensitive test in isolation.

**The leading hypothesis, stated as a hypothesis.** `DataBase` caches loaded tables in a process-wide static
(`LoadTable` → `ConcurrentDictionary`), xUnit runs test *collections* in parallel by default, and this project
has no `xunit.runner.json` and no `CollectionBehavior` attribute — so everything shares one cache across
parallel classes. TASK-273 added **four deliberately-invalid entity types** (`IpUnmapped`, `IpNotNull`,
`IpPrimary`, `IpContradiction`) whose `LoadTable` is *supposed* to throw `TableAttributeException`. If the
cache can observe a partially-built table — populated before `LoadIndexes` runs, or shared `Fields`
dictionaries mutated during a concurrent load (`LoadIndexes` writes `field.IsIndexed`) — then a concurrent
reader could see a state that no single-threaded run produces. **Unconfirmed:** nothing in the evidence above
localises it to those types, only to "the new class plus the suite".

⚠ **Do not close this by re-running until it is green.** The failure rate is roughly 1 in 10 full-suite runs,
so a handful of green runs is the expected outcome whether or not anything was fixed. Either capture it, or
demonstrate the mechanism cannot occur.

## Acceptance criteria

- [ ] The failing test is **identified by name**, with its assertion message. Suggested route, since a trx
      logger over 8 runs did not catch it: loop the suite until failure with the run's output retained
      (`dotnet test --logger "console;verbosity=detailed"` piped to a file per run, or
      `xunit.runner.json` → `"diagnosticMessages": true`), and keep the artefacts of the failing run.
- [ ] The mechanism is established, not guessed: either a concrete interleaving through `DataBase`'s static
      table cache, or a different cause entirely (a genuinely order-dependent pre-existing test that the new
      class merely perturbs by changing collection scheduling).
- [ ] Fixed at the mechanism. If it is the shared cache, the fix is in the framework or in the tests'
      isolation — **not** `"parallelizeTestCollections": false`, which would hide it here and leave every
      other suite exposed. If the cache can publish a table before its indexes are resolved, that is a
      production defect in its own right and gets its own task.
- [ ] A regression test that fails on the unfixed code. If the interleaving cannot be forced deterministically,
      say so and pin whatever *is* deterministic (e.g. that a failed `LoadTable` leaves nothing cached).
- [ ] ⚠ Re-measure the failure rate over **at least 30 full-suite runs** before and after, since ~10% cannot
      be distinguished from fixed by a short run.

## Out of scope

- TASK-273's feature behaviour — green and mutation-proven; this is about suite stability.
- The unreproducible single SQLite failure recorded at TASK-259's close (228/229, seen once, green on four
  subsequent runs). Different suite, different provider, no evidence they share a cause — but worth a look if
  a shared-static mechanism is confirmed here.

## Human test plan

- [ ] N/A — the verification is a repeated-run measurement.

## Implementation plan

_Populated by `/tasks plan TASK-276` — leave empty until then._
