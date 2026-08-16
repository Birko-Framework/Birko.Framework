---
id: TASK-227
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-131, TASK-226]
findings: []
pr: null
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

- [ ] Decide the fix. The two candidates, both cheap, neither obviously better:
      **(a)** `verify` prefers the spec file's own last-commit date over `generated-at` when the spec is
      git-tracked, falling back to the stamp — self-correcting, needs no regen, but makes staleness depend
      on commit history rather than on a recorded field, which is a real semantic change;
      **(b)** `regen` re-stamps in a second pass, or the commit step amends the stamp — keeps `generated-at`
      authoritative but adds a write-after-commit that a non-git or dirty-tree project cannot always do.
- [ ] Whichever is chosen, `verify`'s § *Staleness definition* states the ordering hazard explicitly, so
      the next reader does not have to rediscover that a stamp predates its own spec
- [ ] A test or worked example pins that a source committed **between** the stamp and the spec's own
      commit is not reported as drift
- [ ] `regen`'s existing dirty-tree note is reconciled with whatever is decided — it already half-knows
      about this ("`generated-at` refers to HEAD while uncommitted changes were included")

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
