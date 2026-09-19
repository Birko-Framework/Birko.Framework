---
id: TASK-300
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-07
depends-on: []
blocks: []
related: [TASK-267]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A skill instruction is not an enforcement mechanism — only a hook cannot be skipped

## Context

Spawned at [[TASK-267]]'s close, from its own *"⚠ Deliberately not done"* section. That task fixed the
mechanism by which this repo's project-local convention checks are *reachable*: the generic
`verify-conventions` skill gained a step 0 that globs `.claude/skills/verify-*conventions*/SKILL.md`,
runs its own pass, hands off, names the extension on its report header, and reports a 🛑 for one it
found but did not run.

That is as hard as a skill system allows — the instruction is in the file that always loads, at the
top, with a blocker for the negative case. **Nothing compels an agent to execute it.** The generic
skill's own § *Where this runs* already names [[update-config]] as the mechanism that can: a
`PreToolUse` / pre-commit hook is configured in `settings.json` and executed by the harness, not by
Claude, so it is the one door that cannot be reasoned past.

## Why P3, and why it is filed rather than acted on

**The trigger condition has not been observed.** TASK-267's escalation clause is conditional — *"if
the gate is seen to skip step 0 in practice, that is the escalation"* — and step 0 has fired correctly
on every run since it shipped (measured: TASK-267's own close, and the three-entry 🛑 it produced on
its first ordinary use, which is a gate working rather than a gate being skipped).

Filed anyway because § Task tracking's rule is that work described in a closed task's prose
evaporates, and this is a real, currently-unenforced gap with a named owner skill. It is rankable here
and invisible there. Contrast TASK-254, which answered *"is X owed the same treatment?"* with **no**
and correctly filed nothing — that was symmetry; this is a stated remedy nobody scheduled.

## Acceptance criteria

1. **Measure first whether the gate is actually being skipped.** Before building anything, establish a
   rate: how many `/tasks close` and `/fix-next` runs in this repo since TASK-267 executed step 0, and
   how many did not. A hook that guards a door nobody walks past is cost with no measured return, and
   this repo's own rules (§ TASK-283) say to re-measure a premise before deciding you are blocked *or*
   safe. **If the rate is zero, closing this task as "not needed, measured" is a legitimate outcome** —
   record the number.
2. If a hook is built: it runs the convention gate on staged changes and **fails the commit** on a 🛑.
   It must not fire on a 💡.
3. **The opt-out is part of the fix and is tested** (§ SH-H037). A commit that legitimately needs to
   bypass the gate must have a door that opens, and that door must be exercised — a guard whose escape
   hatch throws is a wall wearing a door's label.
4. It must not duplicate the skill's checks in a second implementation. The hook **invokes** the
   existing pass; a second copy of the rule list is the shape § Conventions keeps recording as a rule
   with one statement and two implementations.
5. Scope: this repo's gate. Whether the generic layer should ship such a hook for every adopting repo
   is a `project-lifecycle-skills` question — file it there if it arises, and remember **task numbers
   collide across trees**, so never express it as `depends-on:`.

## Out of scope

- Changing what the Birko checks assert (TASK-267's boundary, inherited).
- The `## Recent Updates` rolling rule — `~10 KB of entries` (retuned 2026-09-19; was `~5–8 entries`)
  — that is a `/roll-birko-changelog` run, not a hook.
