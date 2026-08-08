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

## Acceptance criteria

- [ ] Establish why the two known gaps were not caught — not run, not surfaced, or not detected for a file
      in an already-mapped project
- [ ] **Decide** how map coverage is audited and when: on every regen, at story close, as a periodic
      sweep, or as a CI gate — recording the reasoning, including why the rejected options were rejected
- [ ] Run the audit once against the current map and record what it finds; a full sweep has never been done
- [ ] Any gaps it finds are fixed in `.map.yml` with the reason noted inline, as the two existing notes do
- [ ] If the answer is a gate, it is stated where a gate can actually run — this family is a polyrepo and
      this aggregator cannot resolve sibling-repo shas, which is what defeated the `shaped-by` pass

## Out of scope

- Per-sub-repo `docs/specs/` trees — [[TASK-131]].
- The `shaped-by` evidence pass being unrunnable here: a known, stamped limitation of every area in this
  repo, and a different problem from coverage.

## Human test plan

- [ ] After the audit mechanism exists, take one file changed by a recently closed task and confirm it is
      reported as covered/uncovered correctly. The failure mode to look for is a check that runs and
      answers wrongly — which would be worse than the current no-check, because it would be believed.

## Implementation plan

_Populated by `/tasks plan TASK-142` — leave empty until then._
