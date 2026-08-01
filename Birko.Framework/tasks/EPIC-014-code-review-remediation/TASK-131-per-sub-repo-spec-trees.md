---
id: TASK-131
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-01
depends-on: []
blocks: []
related: [TASK-110, TASK-115, TASK-128]
pr: null
github-issue: null
jira-key: null
---

# Per-sub-repo `docs/specs/` trees — the aggregator's staleness guard cannot fire

**Filed as an epic-direct task under EPIC-014** rather than under a severity story: this is not a
finding from either sweep, it is the spec layer's own infrastructure. `docs/specs/.map.yml` names the
fix in prose — *"Per-sub-repo specs (each Birko.X/docs/specs/) are the fix and remain deliberate
follow-up work"* — and it existed in no epic, story or task until this one.

## Context

Every one of the aggregator's **25 areas globs out of this repo** via `../Birko.X/...`, because the
family is a polyrepo and the cross-cutting contracts live in sibling repos. `generated-at` stamps
*this* repo's HEAD. So the staleness primitive `/specs verify` defines —
`git diff --name-only <generated-at>..HEAD -- <sources>` — **can never observe a source change**, and
by extension neither can [[roadmap]]'s DV7 or DV8. The map documents the caveat honestly; what was not
visible until now is that the guard is not merely weaker, it is decorative.

**Measured demonstration.** `security-and-authorization` is stamped `0fc2d23`
(2026-07-31 06:51:53 — this repo's HEAD when its regen ran, which is the convention). Its source fix,
SH-H039 closing the Pbkdf2 malformed-hash bypass, is `Birko.Security@2a19150` at **07:05:25** — the
stamp predates the change it exists to guard by fourteen minutes, and no `git diff` in this repo can
see it. Nothing is wrong with that spec today (the respec landed at `6d39283`, twenty-four seconds
after the fix), which is exactly the problem: a guard that cannot fire also cannot tell you it did not.

**What this cost in practice, today.** The TASK-110/115/128 regens carried their scope as prose inside
`generated-at` — including one stamp naming `10f5611`, a **`Birko.Data.Tenant` sha** that makes
`git diff 10f5611..HEAD` fail with *unknown revision* in this repo. Re-stamping those three (done
2026-08-01) restored all 25 to resolvable shas, but resolvable is not the same as *useful*: to scope
that regen I had to walk each sibling repo's `git log --since` by hand to learn which of 82 source
files could have drifted. That per-repo walk is precisely the work a co-located spec tree makes
automatic.

## Why not just fix the aggregator's staleness math

Considered and rejected as the whole answer:

- **Content-hash the sources instead of diffing commits.** `/specs verify` can already compare file
  content, and this would catch drift — but it cannot say *which commit* introduced it, so the diff
  review loses its provenance and `shaped-by:` cannot be computed.
- **Record sibling shas in the stamp.** This is what the three partial regens effectively tried. It
  produces stamps this repo cannot resolve, breaking the very check it means to strengthen. Sibling
  shas belong in a `## Regen provenance` body section (where they now are), not in a machine field.
- Both are worth having *in addition*, but neither removes the reason the map named per-sub-repo trees:
  a spec sitting next to its own code has a single history to diff against.

## Acceptance criteria

- [ ] Decide the split: which areas stay cross-cutting at this aggregator (contracts genuinely spanning
      several `Birko.*` repos) and which become per-sub-repo. The 25 current areas are the input; the
      map's out-of-scope block names 64 single-repo projects that have no spec tree at all.
- [ ] Establish the per-sub-repo shape — `Birko.X/docs/specs/.map.yml` + `<area>.md`, globs relative to
      that repo so `generated-at` and the sources share one history.
- [ ] `/specs verify` reports genuine staleness in at least one sub-repo tree, demonstrated by touching a
      source and seeing the area go stale. This is the criterion the aggregator tree cannot satisfy.
- [ ] The aggregator's remaining cross-repo areas state their weakened guarantee in-file (not only in
      `.map.yml`), so a reader of one spec learns its stamp cannot see its sources.
- [ ] [[roadmap]]'s DV7/DV8 evaluate against the per-sub-repo trees rather than silently passing.

## Notes

Sequencing: this is groundwork, not a defect fix, so it should not preempt STORY-051's four open P0s
(TASK-109, TASK-112, TASK-113, TASK-116). But it should land before the next large harvest, because
every partial regen until then repeats the manual per-repo drift walk described above.
