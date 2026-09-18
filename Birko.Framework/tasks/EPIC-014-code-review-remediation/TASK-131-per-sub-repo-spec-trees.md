---
id: TASK-131
parent: EPIC-014
feature: FEATURE-014
status: done
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

> **Rewritten 2026-08-16 after the measurement in § Progress log.** The goal — criterion 3, *"`/specs
> verify` reports genuine staleness"* — is unchanged and is what "done" still means. Only the **mechanism**
> changed: per-sub-repo trees reach 4 of 25 areas, a per-sibling baseline reaches all 25. The two criteria
> that were *about* the per-sub-repo mechanism moved to [[TASK-226]] rather than being dropped. Swapping a
> mechanism while the defining acceptance holds still is re-scoping; rewriting that acceptance too would
> have been redefining "done" to fit the answer.

- [x] A machine-readable per-sibling baseline exists — `source-commits:` in spec frontmatter, keyed by the
      path prefix exactly as it appears in `sources`, so no name-to-repo mapping has to be guessed
- [x] `/specs verify` resolves external sources against **their own** repo (`git -C <sibling> diff
      <sha>..HEAD`), and a missing entry reports **unknown baseline**, never fresh — the failure this whole
      task is about is a check that cannot fire passing quietly
- [x] `/specs regen` records the shas going forward (step 5c), so the amnesty below is one-time
- [x] **The guard fires, on real drift rather than a synthetic touch:** first run reports **15 of 25 areas
      stale, 40 changed source files**, none of it previously visible
- [x] **And it can still report fresh** — the other 10 are genuinely unchanged, verified independently
      (`Birko.Data.Migrations`, `.BackgroundJobs`, `.Workflow`, `.Serialization`, `.Data.Sync` each have
      **0 commits** since their baseline). A check that only ever says "stale" is as useless as one that
      only ever says "fresh"; both directions are demonstrated
- [x] The reconstructed-baseline caveat is stated **in-file** on all 25 specs, not only in `.map.yml`
- [x] [[roadmap]]'s DV7 evaluates through `source-commits` and reports **which repo** drifted
- [x] `.map.yml`'s prose no longer names per-sub-repo trees as "the fix" — it records what they actually
      cover (16%) and points at [[TASK-226]]

## Out of scope

- **Per-sub-repo spec trees** — [[TASK-226]]. Still worth doing for the 4 single-repo areas and the 64
  unspecced single-repo projects, but it is no longer on the critical path for the staleness guard.
- **DV11 / `shaped-by` provenance** — untouched here. Note the two are now coupled in a useful way:
  `source-commits` gives each area a per-repo baseline, which is the same join the provenance derivation
  needs. Recorded, not built.

## Notes

Sequencing: this is groundwork, not a defect fix, so it should not preempt STORY-051's four open P0s
(TASK-109, TASK-112, TASK-113, TASK-116). But it should land before the next large harvest, because
every partial regen until then repeats the manual per-repo drift walk described above.

> **Precondition cleared 2026-08-16** — all four P0s are `done`, and no large harvest is pending.

## Progress log

- **2026-08-16 — picked. Measured first, and the measurement contradicts this task's premise.**

  **Finding A — per-sub-repo trees fix 4 of 25 areas, not 25.** Classified every area in `.map.yml` by
  how many sibling repos its source globs reach. Only **four** are single-repo and could host their spec
  next to their code: `store-lazy-initialization` (Birko.Data.Stores), `specifications-and-paging`
  (Birko.Data.Patterns), `entity-tagging` (Birko.Data.Tagging), `entity-localization`
  (Birko.Data.Localization). The other **21 span 2–13 repos** — `settings-configuration-chain` 13,
  `views-and-aggregation` 12, `schema-index-and-ddl` 10, `background-jobs` 10 — including every
  high-traffic area this session touched. A per-sub-repo tree *cannot* host a cross-repo area; that is
  precisely why they are at the aggregator. So the fix named in `.map.yml`'s prose, and inherited by this
  task's title and § Context, addresses **16%** of the problem and leaves the guard decorative for the
  rest.

  **Finding B — the rejected option was rejected for the wrong reason, and it works.** § *Why not just fix
  the aggregator's staleness math* dismisses "record sibling shas in the stamp" because it "produces stamps
  this repo cannot resolve". True of the single `generated-at` field — a foreign sha there breaks
  `git diff`. But the shas are already being recorded in prose (`## Regen provenance`, present in
  `bulk-filter-operations`, `tenant-isolation`, `views-and-aggregation`), and that section says the same
  thing: *"recorded here rather than in the stamp because this repo cannot resolve them"*. **This repo
  cannot, but `git -C ../Birko.X` can.** Measured, using `bulk-filter-operations`' own recorded shas:

  | Repo | Recorded sha | `.cs` changed since |
  |---|---|---|
  | Birko.Data.SQL | `d8c2f40` | 18 |
  | Birko.Data.MongoDB | `88f96ee` | 7 |
  | Birko.Data.Stores | `3cd8b2a` | 2 |
  | Birko.Data.InMemory | `4f680b7` | 2 |

  **29 changed files in one area**, which the aggregator's guard reports as zero. The guard is not merely
  unable to fire — it is actively concealing drift that is already there, and the data needed to see it is
  already written down in the spec, in prose, one field short of being machine-readable.

  Acceptance criteria not yet rewritten — the re-scope is a decision, raised with the user rather than
  taken.

- **2026-08-16 — re-scope approved; implemented.** See § Outcome.

## Outcome

**What was wrong.** For the whole life of this spec map, `/specs verify` could not observe a single source
change. Every one of the 25 areas globs into a sibling repo; `generated-at` names *this* repo's HEAD; so
`git diff <generated-at>..HEAD -- ../Birko.X/...` matched nothing and every area reported **fresh
forever**. Not a weakened guard — a guard that was green by construction, which is the same shape this
codebase has recorded repeatedly (a scope check that tests the wrong thing and therefore always passes).

**The fix is a per-sibling baseline, not per-sub-repo trees.** Each spec carries `source-commits:`, keyed
by the path prefix exactly as it appears in `sources`, and `verify` resolves each external glob with
`git -C <sibling> diff <sha>..HEAD`. The task's own § *Why not just fix the aggregator's staleness math*
had rejected this — *"produces stamps this repo cannot resolve"* — and that reasoning was **right about
the wrong field**: a foreign sha in `generated-at` does break, but a keyed map alongside it does not,
because each sha is resolved *in its own repo*. Three of the specs were already recording exactly these
shas in prose, under a `## Regen provenance` heading that gave the same justification. The data was one
field short of usable for weeks.

**Split — the guard fires, and can still say fresh.** First run: **15 of 25 areas stale, 40 changed source
files**, none previously visible. The other **10 are genuinely fresh**, verified independently rather than
assumed — `Birko.Data.Migrations`, `.BackgroundJobs`, `.Workflow`, `.Serialization` and `.Data.Sync` each
have **0 commits** since their baseline. Both directions matter: a check that only ever reports stale is
as worthless as the one being replaced, and "fresh" would also be what a silently-failing glob match
produced.

**Mutation, on the branch most likely to fail open.** Removed one entry
(`../Birko.Data.Migrations.SQL: f72bf7d`) from `migrations.md`: the area moved `fresh → unknown baseline`
and the fresh count dropped 10 → 9. That is the whole point of the design — a missing baseline must never
read as fresh, or the fix reintroduces the defect for any repo somebody forgets. Restored by rewriting the
exact line; back to 15/10.

**Judgement calls.**

- **The mechanism was swapped and the defining acceptance was not.** Criterion 3 (*"reports genuine
  staleness"*) is what "done" means here and it survived untouched; only the two criteria describing the
  per-sub-repo mechanism moved out, to [[TASK-226]]. If criterion 3 had needed rewriting too, that would
  have been redefining done to fit the answer.
- **Baselines are reconstructed from `generated-on`, not from today's HEAD.** Today's HEAD was the obvious
  choice and would have amnestied everything — 12 specs date from 2026-07-30, so **17 days** of sibling
  drift would have been silently forgiven, and the guard's first run would have reported a comforting
  0 stale. Using each spec's own date recovers most of it: that is where 15 of the 15 stale areas come
  from. Deliberately taken at **start**-of-day, so a commit made on the regen day itself reads as drift —
  over-reporting is the safe direction; under-reporting is how this defect happened in the first place.
- **The amnesty is stated, not absorbed.** Drift *within* a spec's own regen day is the one thing this
  cannot see. Written into `.map.yml` and onto all 25 specs in-file, because a reconstructed baseline that
  looks like a recorded one is a measurement claim that was never measured.
- **Only 4 of 128 shas could be recovered from prose, so none were used.** Mixing 4 recovered shas (which
  mark *scoped* regens, not the full-regen baseline) with 124 date-derived ones would have put two
  different meanings in one field for a 3% gain. Uniform beat marginally-more-precise.
- **The generic skill changed, not a Birko-local copy.** Polyrepo aggregators are not a Birko problem, and
  the field is absent-by-default so single-repo projects are unaffected. Verified backwards-compatible by
  construction: an area with no external sources never reaches the new branch.
