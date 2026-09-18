---
id: TASK-460
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-457]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Move the 350 archived repos into an attic org

## Context

[[TASK-457]] consolidated the framework into one repo and **archived** the 350 superseded per-project
repos rather than deleting them, so every existing URL keeps resolving. That decision stands — see
*Not deletion* below.

The side effect is cosmetic but real. Measured 2026-09-18:

| `github.com/birko` | count |
|---|---|
| total repos | **378** |
| archived (old polyrepo corpses) | **350** |
| live / actually worked on | **28** |

So the account reads as 378 repos when 28 are real. Anyone visiting — or you, browsing your own
profile — has to filter to see anything.

## What to do

Create a second organisation used purely as an archive (`Birko-Framework-Attic`, `birko-attic`, name
to be chosen) and **transfer all 350 archived repos into it**.

| | now | after |
|---|---|---|
| `github.com/birko` | 378 | **28** — live work only |
| `Birko-Framework` org | 5 live repos | 5 live repos, unchanged |
| attic org | — | 350 archived |

## Why this works where transferring into `Birko-Framework` did not

That was tried and rejected during TASK-457: `birko/Birko.Framework` **collides** with
`Birko-Framework/Birko.Framework`, which is the monorepo. GitHub refuses a transfer onto an existing
name. An attic org has no such collision because the monorepo is not in it.

It also keeps the live org clean — burying 5 live repos under 350 archived ones was the second reason
that transfer was declined.

## What is preserved

**Transfers set up redirects**, so nothing breaks:

- the **21 files** referencing old `github.com/birko/Birko.*` URLs (13 in this repo's docs and tasks,
  8 across consumer repos)
- the **1 fork** (`denli8/Birko.Data.ElasticSearch`) and its lineage
- the **8 stars** across 7 repos
- the **2 tags** on `Birko.Data.ElasticSearch` (`1.7.10`, an annotated "ES 7.10.x" compatibility
  marker, and a `1.0` release) — these exist *only* on the archived repo, since the migration fetched
  `main` and not tags (recorded as a correction on [[TASK-457]])

## Not deletion

Deleting the 350 was considered on 2026-09-18 and **declined**, on evidence rather than caution: the
repos are public and had a fork and 8 stars, so "I was the only one using them" was not true; the two
ElasticSearch tags live only there; 21 files would 404; and keeping them costs nothing while deletion
is irreversible. **Do not quietly turn this task into a deletion.** If deletion is ever revisited,
migrate those tags into the monorepo first, namespaced (`Birko.Data.ElasticSearch/1.7.10` — a bare
`1.0` means nothing across 178 projects).

## Cost and whether it is worth doing

~10 minutes of scripted API calls; the loop already exists from the archiving pass in
`scratchpad/migration/archive-old.sh` and needs its transfer step re-enabled against a new target.
One extra free org.

**It is purely cosmetic** — the repos are already archived, read-only and unreachable by accident, so
they cost nothing functionally. Worth doing if the profile is browsed; skippable otherwise. That is
why this is P3.

## Acceptance

1. Create the org, then **show the transfer plan before running it** — the list of 350 and the target.
2. Transfer in batches, logging each result; a failed transfer must be visible, not swallowed.
3. Verify afterwards: `github.com/birko` returns **28** repos; a spot-check of 3 old URLs still
   redirects to the attic org; the fork's lineage is intact; the 2 tags are still readable.
4. Requires `admin:org` on the gh token for the *destination* org as well as the source.
