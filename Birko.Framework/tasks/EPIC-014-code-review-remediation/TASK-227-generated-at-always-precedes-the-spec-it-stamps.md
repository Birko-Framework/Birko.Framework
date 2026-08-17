---
id: TASK-227
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-131, TASK-226]
findings: []
pr: "project-lifecycle-skills db652cd"
github-issue: null
jira-key: null
---

# `generated-at` always names the commit *before* the spec it stamps, so staleness is measured from too early

## Context

Found while draining [[TASK-131]]'s newly-visible DV7 backlog. It is a defect in the **generic** `specs`
skill, not in this repo's data, and it silently biases every staleness answer in the pessimistic direction.

`regen` step 5 stamps `generated-at:` with `git rev-parse HEAD` at harvest time — *before* the spec file
it is writing has been committed. The spec then lands in a later commit. So `generated-at` can only ever
name the commit **preceding** the one that carries the spec, and any source committed in between is
counted as drift the spec has in fact already absorbed.

**Measured across all 25 areas in this repo: 25 of 25 stamps are older than the commit that last wrote
their own spec file.** Not a few stragglers — it is structural, and it cannot be otherwise with the
current ordering. Gaps range from minutes to eight hours:

| Area | Stamp | Spec's own last write | Gap |
|---|---|---|---|
| `caching` | 2026-08-12 10:42:44 | 2026-08-12 14:42:55 | 4h |
| `core-model-contracts` | 2026-08-16 07:51:36 | 2026-08-16 16:17:32 | 8h |
| `background-jobs` | 2026-07-30 08:57:00 | 2026-07-30 16:07:38 | 7h |
| `schema-index-and-ddl` | 2026-08-12 14:42:55 | 2026-08-14 13:03:03 | 2d |

**What it cost, concretely.** TASK-131's first reconstruction anchored sibling baselines on
`generated-at` and reported **15 stale areas / 40 changed files**. Re-anchored on the commit that
actually wrote each spec, the real answer is **6 areas / 10 files**. Nine areas were false positives —
including `caching`, whose content was written three minutes *after* the SH-H006 commits it was being
accused of missing. Regenerating those nine would have been pure waste, and the diff review would have
found nothing, which is the outcome most likely to teach a reader to stop reviewing diffs.

**Why it matters beyond this repo.** For an in-repo project the gap is usually small and the sources
usually ride in the spec's own commit, so it rarely bites. It bites hard in a **polyrepo aggregator**,
where sibling commits land continuously and independently of when the spec is committed — exactly the
configuration TASK-131 just made measurable.

## Acceptance criteria

- [x] Decide the fix. The two candidates, both cheap, neither obviously better:
      **(a)** `verify` prefers the spec file's own last-commit date over `generated-at` when the spec is
      git-tracked, falling back to the stamp — self-correcting, needs no regen, but makes staleness depend
      on commit history rather than on a recorded field, which is a real semantic change;
      **(b)** `regen` re-stamps in a second pass, or the commit step amends the stamp — keeps `generated-at`
      authoritative but adds a write-after-commit that a non-git or dirty-tree project cannot always do.
- [x] Whichever is chosen, `verify`'s § *Staleness definition* states the ordering hazard explicitly, so
      the next reader does not have to rediscover that a stamp predates its own spec
- [x] A test or worked example pins that a source committed **between** the stamp and the spec's own
      commit is not reported as drift
- [x] `regen`'s existing dirty-tree note is reconciled with whatever is decided — it already half-knows
      about this ("`generated-at` refers to HEAD while uncommitted changes were included")

## Outcome

**Chosen: (a), refined — `verify` anchors on the LATER of `generated-at` and the spec's own last commit**,
not simply on the spec's commit. Both directions are real: after a regen that has not been committed yet,
the stamp is the *newer* of the two and preferring the commit blindly would lose that. Guarded by
`git merge-base --is-ancestor`, so a cherry-pick or a rewritten history falls back to the stamp rather than
comparing dates across unrelated lines.

**(b) was rejected on the constraint the task already named**: re-stamping after the commit needs an amend
or a second commit, which a dirty tree or a non-git project cannot do — and it would still be wrong for
anyone whose commit habits differ. The stamp stays a **floor** and now says so; the reader resolves the
rest.

**The mechanism, which the finding stated but did not name.** The defect needs an **uncommitted source
change at harvest time** — which is the normal way this is used, because `regen` step 7 explicitly tells you
to commit the spec *with the related work*. If the source had been committed before the harvest,
`generated-at` would already name it and the stamp would be fine. **That is why the defect is invisible when
you reason about it from a clean tree**, and it is why the first worked example written for this fix was
wrong: `git diff c1..HEAD` excludes c1's own changes, so the sequence as drafted demonstrated nothing.
Running it in a scratch repo is what corrected it.

Reproduced end to end, old anchor versus new:

```
old anchor (generated-at): [src/RedisCache.cs]   -> "stale"
new anchor (spec commit):  []                    -> fresh
```

**Two things carried into the skill rather than left implicit:**

- **The known bias is written down.** Anchoring on the spec's own commit treats everything in that commit as
  absorbed, and a commit that *touches* a spec without regenerating it — a frontmatter fix, a bulk
  re-stamp, a rename — still moves its anchor. Observed here: `e6a16e0` touched all 25 specs while
  regenerating only 6. Under-reporting in that narrow case is the deliberate trade against structurally
  over-reporting on every spec.
- **Spawned [[TASK-240]]**, found by running the example: the same check was passing `.map.yml` globs to git
  as pathspecs, so `X/**/*.cs` silently missed every file sitting directly in `X/`. **47 of 74 globs in this
  repo's map, 124 files.** Fixed in the same commit.

**Not verifiable against this repo's own numbers, and worth saying so.** Every area here sources from
sibling repos, so the in-repo anchor path never runs and both anchors report 0 stale. The measurement that
matters — 25 of 25 stamps older than their spec's own commit — reproduces exactly; the behavioural proof had
to be a scratch repo.

## Out of scope

- The external-source baseline mechanism itself ([[TASK-131]], landed). This task is about the *anchor*
  being early, not about which repo it is resolved in.
- Re-reconstructing this repo's baselines. They are already anchored on each spec's own last-write commit,
  which is the accurate answer; only 6 areas carry exactly-recorded shas, and the rest are correct as
  reconstructed.

## Human test plan

N/A — the check either reports a between-commits source as drift or it does not, which an automated run
observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-227` — leave empty until then._
