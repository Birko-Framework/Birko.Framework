---
id: TASK-142
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: human
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The spec map silently under-covers, and nothing detects it

## Context

Filed from [[TASK-109]]'s Outcome, where it was flagged and deliberately not filed pending this decision.

**Twice now, a fix's primary site has been reachable by no glob in any area of `docs/specs/.map.yml`:**

- [[TASK-110]] — the ORDER BY resolver. The map still carries the note *"added for TASK-110, whose fix
  landed in files no glob reached (silent under-coverage)"*.
- [[TASK-109]] — the four destructive connector funnels and `WholeTableWriteException`, plus both MongoDB
  stores. A `/specs regen` **could not have seen the behaviour change at all**.

Both were caught by a human noticing during step 7, not by a mechanism. Two instances of the same failure
in the same file is a property of the map, not an accident.

**Why it matters more than a missing glob.** A `/specs regen` diff is supposed to be a fix's evidence and
an unintended-change detector. Over an under-covered area it is neither, and it fails *silently* — a clean
diff reads as "nothing changed" when it means "nothing was looked at". That is the same shape as the
defects EPIC-014 is draining: a plausible answer with nothing behind it.

`/specs regen` step 6 already globs project sources matched by no area and no `ignore` entry, and reports
them. So a mechanism exists and is either not being run, or is being run and its output ignored, or does
not fire for a file inside an *already-mapped project* — which is the shape both misses had. Establishing
which is the first half of this task.

### Third instance, 2026-09-08 (from [[TASK-312]])

`SH-H040` was an authentication fail-open with **two** gates. The engine gate
(`Birko.Security/Authentication/AuthenticationService.cs`) is covered — `security-and-authorization`
globs `../Birko.Security/**/*.cs`, and its requirement *"Static-token authentication is disabled unless
enabled and populated"* documented the defect verbatim, including the words *"the service fails open"*, so
the fix produced a real spec diff.

The **second** gate is not covered by anything. Measured: **0** of `docs/specs/*.md` mention
`SseAuthenticationService`, and `docs/specs/.map.yml` has no glob reaching
`Birko.Communication.SSE` at all. So an auth-bypass fix in a transport middleware produced **no** spec
diff, and the spec layer cannot say whether SSE authenticates anything.

⚠ **The shape is worse than under-coverage of a helper.** The two previous instances were internal
resolvers; this one is a network-facing authentication boundary on a whole transport. And note what it
does to this epic's own evidence rule: a fix's spec diff is supposed to *be* the evidence, so for the SSE
half there was none to review — the regression tests had to carry it alone.

⚠ **Checked immediately rather than left as a question, and the answer is worse: ALL FOUR transports
that share this engine have zero globs.** Measured 2026-09-08 against `docs/specs/.map.yml`:

| Project | Globs reaching it |
|---|---|
| `Birko.Communication.SSE` | **0** |
| `Birko.Communication.WebSocket` | **0** |
| `Birko.Communication.REST.Server` | **0** |
| `Birko.Communication.SOAP` | **0** |

Each of those four contains an authentication middleware or service that gates network traffic — the
`RestAuthenticationMiddleware`, two WebSocket gates, and the SSE gate `SH-H040`'s fix had to touch. So the
spec layer describes the shared **engine** and none of the four **boundaries** that call it, which is
precisely the seam an auth defect hides in. That makes this task's subject a security-coverage gap rather
than a tidiness one, and it is the reason its priority is worth re-reading.

## Acceptance criteria

- [ ] Establish why the two known gaps were not caught — not run, not surfaced, or not detected for a file
      in an already-mapped project
- [ ] **Decide** how map coverage is audited and when: on every regen, at story close, as a periodic
      sweep, or as a CI gate — recording the reasoning, including why the rejected options were rejected
- [ ] Run the audit once against the current map and record what it finds; a full sweep has never been done
- [ ] Any gaps it finds are fixed in `.map.yml` with the reason noted inline, as the two existing notes do
- [ ] If the answer is a gate, it is stated where a gate can actually run — this family is a polyrepo and
      this aggregator cannot resolve sibling-repo shas, which is what defeated the `shaped-by` pass

## Sweep progress (2026-08-08)

**Criterion 1 answered — it is a BLIND SPOT, not an unrun check.** `/specs regen` step 6 globs "project
sources"; this aggregator has **0 `.cs` files of its own** (every source is in a sibling repo reached via
`../`), so the check globbed nothing, reported nothing, and passed silently on every run since it was
written. Fixed in `project-lifecycle-skills@e560f99`: it now derives its search roots from the map's own
`sources:` entries, and reports the count even at zero.

**Measured gap: 148 unmapped `.cs` inside the 97 projects the map already references** (~18%). Areas list
specific files rather than whole trees, so a sibling file added later matches nothing. TASK-109 and
TASK-110 were symptoms of this, not two unlucky files.

**Widened four areas** — the omission was systematic: each already listed its SQL and ElasticSearch
implementors and omitted the document backends, in areas whose whole point is cross-backend conformance:
`schema-index-and-ddl` (+Mongo/Raven/Cosmos `IndexManagement`), `unit-of-work-and-transactions`
(+Mongo/Raven/Cosmos/Influx `UnitOfWork`), `repository-contract` (+the five backends' `Repositories`),
`views-and-aggregation` (+Mongo/Raven/Cosmos `Aggregation`). **28 files newly covered.**

**A widening was proposed and REVERTED, which is the more useful result.** The sweep also proposed adding
`Birko.Data.SQL.View/**/*.cs` and the document backends' `Stores/*.cs` — both are named in this map's own
header as **deliberately out of scope** *"and are NOT silent under-coverage"*. A coverage number is not an
argument against a recorded decision; both were reverted with the reasoning left inline. **35 of the
remaining unmapped files are that deliberate exclusion**, so the raw 148 conflated two different things
and any future sweep must subtract them before quoting a number.

**Remaining genuine gap: 85 files**, concentrated and uniform in shape — `Stores/`, `Repositories/` and
`Extensions/` in `Birko.Data.SQL` and its four providers (10 each), plus ElasticSearch's
`Highlighting/`, and `Birko.Data.SQL`'s `Models/` + `Exceptions/` + remaining `SQL/Connectors/`. These are
**not** obviously in-scope: several are per-provider store bodies, which is exactly the category the
header excludes. Finishing the sweep therefore needs the same decision the reverted pair needed —
whether the out-of-scope rule for "store bodies" should hold now that per-backend divergence has produced
three defects (TASK-109, TASK-116, TASK-125) — rather than more globbing.

## Out of scope

- Per-sub-repo `docs/specs/` trees — [[TASK-131]].
- [[TASK-149]] is the same shape in the task tree — roadmap's DV12 rule exists and did not run, exactly as
  the unmapped-sources check here exists and did not. Different mechanism, so a separate task, but the two
  are probably one decision about which checks are actually wired to something that runs them.
- The `shaped-by` evidence pass being unrunnable here: a known, stamped limitation of every area in this
  repo, and a different problem from coverage.

## Human test plan

- [ ] After the audit mechanism exists, take one file changed by a recently closed task and confirm it is
      reported as covered/uncovered correctly. The failure mode to look for is a check that runs and
      answers wrongly — which would be worse than the current no-check, because it would be believed.

## Implementation plan

_Populated by `/tasks plan TASK-142` — leave empty until then._
