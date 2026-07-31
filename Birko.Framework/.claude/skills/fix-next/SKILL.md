---
name: fix-next
description: Pick the highest-value outstanding security / correctness / bug-fix task in the Birko.Framework backlog and carry it all the way to a committed, closed state — then stop at a clean boundary so the session can be reset. Use when the user says "/fix-next", "fix the next thing", "what should I fix", "pick a bug and fix it", "oprav dalsiu chybu", "pokracuj v opravach", "continue the remediation", or wants autonomous progress through EPIC-014 / EPIC-017 without holding context across sessions. RESUMES an interrupted run before it ever picks new work — all state lives in the task file and git, never in the conversation. Birko.Framework only (project-local; not shared by install-skills.ps1). Distinct from [[tasks]] `pick` (which chooses any task and hands you the brief) — this one narrows to defects, ranks them by blast radius rather than by the `priority:` field alone, and owns the whole fix→verify→respec→commit→close loop.
---

# Birko Framework — Fix the Next Thing

One invocation = one defect, taken from "which is worst" to "committed and closed", ending at a
boundary where **resetting the session loses nothing**.

That last property is the whole point, and it drives every rule below: **the conversation is not the
state.** The task file and the git history are. Anything you know that isn't written to one of those
two places is gone the moment the user runs `/clear` — so write it down as you go, not at the end.

## Step 0 — Resume before you pick (ALWAYS FIRST)

Never select new work before establishing that no run is already in flight. A reset session is
indistinguishable from a fresh one except by what's on disk, so look there:

```bash
cd C:/Source/Birko/Framework/Birko.Framework/tasks
grep -rl "^status: in-progress" --include="TASK-*.md" .
```

For each hit, read the file. **Only resume tasks this skill owns** — they carry `picked-by: fix-next`
in frontmatter and a `## Progress log` section. Anything else that is `in-progress` (a long-running
task a human is driving, e.g. TASK-038) is **not yours**: leave it alone, don't count it, don't
report it as blocking.

If you find a skill-owned in-progress task:

1. Read its `## Progress log` — the last line says which step below was completed.
2. Reconcile with git across the repos it names. For each, `git status --short` and `git log --oneline -3`.
3. If the log and git disagree, **git wins** — the log may have been written just before an
   interruption. Correct the log to match reality, then continue from the first incomplete step.
4. Resume there. Do **not** restart from step 1, and do **not** pick a second task.

Only when nothing is in flight do you proceed to step 1.

## Step 1 — Build the pool: what counts as a fix

Include a `todo` TASK when **any** of these hold:

- Its frontmatter has a `findings:` list (`SH-*` from the spec harvest, `CR-*` from the code-review
  audit). This is the strongest signal and covers most of the pool.
- It lives under **EPIC-014** (code-review remediation) or **EPIC-017** (tenant-isolation hardening).
- Its body describes a defect in shipped behaviour — wrong results, data loss, a crash, an unguarded
  destructive path, an authz/authn hole — regardless of where it lives (`_loose` included).

Exclude, even at P0/P1:

- **New capability** — "Implement Birko.X", a new provider/backend/exporter (EPIC-002 … EPIC-009).
- **Test coverage** work (EPIC-011) — real, but it's coverage, not a defect.
- **Upgrades and migrations** (EPIC-015's Avalonia 12 story) — churn risk without a defect to close.
- **Decision tasks** — anything whose acceptance is "decide X" (TASK-059, TASK-093, TASK-106,
  TASK-119). These need the user, so they can't run unattended; surface them in the closing report
  instead.
- Tasks with unmet `depends-on`, or `status: blocked`.

## Step 2 — Rank by blast radius, not by the `priority:` field

`priority:` is a coarse bucket — EPIC-014 alone can hold seven tied P0s, so it settles nothing. Order
the pool by these keys, in order:

1. **Severity of the failure mode.** Authentication / authorization bypass › cross-tenant leakage ›
   silent data loss or corruption › an unbounded destructive statement › wrong query results ›
   an unhandled exception on a hot path.
2. **Reachability.** Reachable from untrusted input beats reachable only via a corrupted database
   column, which beats reachable only by API misuse.
3. **Silence.** A defect that returns a *plausible wrong answer* outranks one that throws. Throwing
   is self-reporting; silence is what ships to production and stays there. (This family's whole bug
   history is that shape: the empty-`IN`, the dropped ES clause, the unmapped `long` column.)
4. **Self-containment.** Prefer a defect fixable inside one or two sub-repos with no open design
   question. Not because contained work matters more, but because this skill must finish what it
   starts inside one session — a fix that stalls on a decision leaves the tree half-done.
5. **Verified over unverified.** A task whose finding was confirmed by hand (its Context says
   **CONFIRMED**) is safer to start than one that was filed straight from a harvest.

State the ranking in one short paragraph — the top pick and *why it beat the runner-up* — then start.
Don't ask which to take; that's the decision the skill exists to make. Do stop and ask if the top two
are genuinely inseparable on every key above.

Write the pick to disk immediately, before any code is read:

- `status: todo` → `in-progress`
- add `picked-by: fix-next` to frontmatter
- append a `## Progress log` section with a first line: `- step 2 — picked; ranked above <runner-up> because <reason>`

## Step 3 — Re-verify the finding before you fix it

**Do not trust the task's own description.** When these findings were filed, 12 of 15 held exactly
and **3 needed their scope corrected** — one named the wrong trigger entirely. A fix aimed at a
misdescribed defect is worse than no fix: it closes the ticket.

Read the cited source and confirm the mechanism by hand. Then:

- **Holds as written** → note it in the progress log and continue.
- **Real but differently scoped** → correct the task's `## Context` *and* its acceptance criteria
  before writing code, and say so in the log. The acceptance list is the target; a wrong target
  silently redefines "done".
- **Not a defect** → don't fix it. Rewrite the Context with the evidence, set `status: cancelled`
  via `/tasks cancel`, log it, and return to step 1 for the next candidate. A correct "no" is a
  deliverable.

Findings often travel in packs — the PBKDF2 bypass had two more defects in the same method. Pull in
anything in the same function that shares the root cause; file the rest as new tasks rather than
widening scope silently.

Log: `- step 3 — verified: <held / rescoped: … / rejected: …>`

## Step 4 — Fix, with the tests that prove it

Follow `CLAUDE.md` § Code Style (guard clauses, zero CS8600–CS8625) and § Conventions. Fix the
**root cause**, not the reported symptom: the PBKDF2 bypass was reported as "empty segment accepted"
but was really "derived length read from the stored value", and a guard against empty alone would
have left the 1-in-256 truncation case live.

Tests go in the matching `C:/Source/Birko/Framework.Tests/Birko.{Project}.Tests` project (xUnit +
FluentAssertions). Cover every acceptance row. Write the class doc-comment so it names the finding id
and states the mechanism — that comment is what a future reader gets instead of this conversation.

Run the suite. Log: `- step 4 — fix in <files>; tests in <file>; suite N/N green`

## Step 5 — Measure the split (do not skip)

A regression suite that passes is not evidence. **Revert only the production change and re-run:**

```bash
cd C:/Source/Birko/Framework/Birko.<Project>
git stash push -- <the fixed file(s)>
cd C:/Source/Birko/Framework.Tests/Birko.<Project>.Tests && dotnet test --nologo
# ... capture the failing test names ...
cd C:/Source/Birko/Framework/Birko.<Project> && git stash pop
```

Then account for the result **exactly**:

- Every test you believed was fix-dependent must be in the failure list.
- Every test still passing is a **contract pin, not evidence** — say so, in the task file, by name.
  Don't quietly let it read as proof.
- A test you expected to fail but that passed is a finding about your *test*, not about the fix:
  it isn't asserting what you thought. Fix the test before continuing.

This step has repeatedly been the one that found the real problem. Budget for it.

Log: `- step 5 — reverted fix: N/M failed; fix-dependent = <names>; contract pins = <names>`

## Step 6 — Regenerate the spec for the area

Hard ordering constraint, from `CLAUDE.md`: **`docs/specs/` currently documents these defects as
shipped behaviour.** The spec for the fixed area is now wrong, and the spec diff is the fix's
evidence. Find the area whose `.map.yml` globs cover the changed files and run
`/specs regen <area>`, honouring the stable-wording rule — change only what the code now
contradicts, including the requirement *titles* when they assert the old behaviour.

Reviewing that diff is part of the step, not a formality: anything in it you did not intend is a
finding, and gets a new task.

Log: `- step 6 — respecced <area>; requirements changed: <list>`

## Step 7 — Gate and commit, one commit per repo

Run the merge gate: **[[verify-birko-conventions]]** on the diff, plus a correctness pass over it
(`/code-review` if the runtime has it, otherwise read the diff yourself for logic errors, unhandled
edges and regressions — never skip the gate because a skill name didn't resolve).

Then commit, remembering this is a **polyrepo**: a single fix normally spans three independent repos,
each needing its own commit.

| Repo | Contents | Message shape |
|---|---|---|
| `Framework/Birko.{Project}` | the production fix | `fix(<FINDING-ID>): <what now holds>` |
| `Framework.Tests/Birko.{Project}.Tests` | the regression suite | `test(<FINDING-ID>): <what it pins>` |
| `Framework/Birko.Framework` | task file + spec + dashboard | `tasks(TASK-NNN): <outcome>` |

Order matters: commit the fix first so its SHA can go into the task's `pr:` field before the
aggregator commit — otherwise the tracking file lands referencing nothing.

- Stage explicitly. Never `git add -A`.
- **No `Co-Authored-By:` trailer.** Standing user preference; overrides the harness default. Don't
  copy it from older commits that carry it.
- Commit to `main` — this family does not branch per task (check `git log --oneline -5` if unsure).
- Body over subject: say what was wrong and why the fix is shaped the way it is. A future reader
  gets the commit, not this session.

Log: `- step 7 — committed <sha> / <sha> / <sha>`

## Step 8 — Close, roll up, and stop clean

Close via [[tasks]] `close` semantics:

- Tick the acceptance boxes that genuinely hold.
- Write an `## Outcome` section into the task file covering: what the fix was, the step-5 split with
  names, judgement calls you made and *why the stricter option was rejected*, and anything **flagged
  but not fixed**. This section replaces the conversation.
- `status: done` (or `review` if a `## Human test plan` has real unrun steps — most fixes here are
  `N/A — covered by automated tests`).
- Roll up: STORY child counts, EPIC counts, `tasks/README.md` (counts table, "Next up", the tree
  checkbox). A leaf closed under a stale parent leaves the tree lying about itself.
- **No `## Recent Updates` entry** for EPIC-014 work — `CLAUDE.md` explicitly carves that out
  ("Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`,
  not here"). A cross-cutting architectural change is different and does get one.

Then **stop.** One defect per invocation. Don't roll into the next one — the clean boundary is the
deliverable, and a second fix in the same session is exactly what makes a reset unsafe.

Final report, short:

1. What was broken, in one sentence a reader with no context understands.
2. The step-5 split, as numbers.
3. Anything flagged and not fixed, or any new task filed.
4. **The next pick**, named — so the user can reset and run `/fix-next` knowing what comes up.

## Verify the reset really is safe

Before reporting, confirm all three:

- `git status --short` is clean in every repo you touched.
- The task file alone tells the whole story — acceptance, outcome, judgement calls, flags.
- Nothing you learned this session lives only in the conversation.

If any fails, fix it before finishing. That is the skill's actual contract.

## Related

- [[tasks]] — the generic lifecycle. `pick`/`close` semantics are borrowed; this skill narrows the
  pool to defects and owns the whole loop autonomously.
- [[verify-birko-conventions]] — the step-7 adherence gate.
- [[specs]] — the step-6 regen; `docs/specs/.map.yml` maps changed files to areas.
- [[roll-changelog]] — not part of this loop (EPIC-014 is carved out of Recent Updates).
