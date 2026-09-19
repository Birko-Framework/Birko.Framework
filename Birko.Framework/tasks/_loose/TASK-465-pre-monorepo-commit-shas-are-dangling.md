---
id: TASK-465
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-457, TASK-118]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Every `pr:` SHA recorded before the monorepo resolves to nothing

Found while signing off [[TASK-118]], whose `pr:` named four commits and **none of them exist**:

```
$ git cat-file -t c4dd307
NOT FOUND
```

[[TASK-457]] consolidated 349 repositories with `git filter-repo --to-subdirectory-filter`, which
**rewrites every commit hash** — the tree of each commit changes, so its SHA does. History is intact
and `git log --follow` still works; only the identifiers moved. Nobody recorded that the task files
point at the old ones.

**Measured: 11 task files** carry a `pr:` in the old `[Repo@sha, …]` form. Every one is dangling.

## Why it matters more than it looks

The `pr:` field is the only link from a closed task to the change that closed it. This family does
not branch per task and has no PRs, so there is no second route — no branch name, no merge commit, no
issue thread. A dangling SHA means *"what actually shipped for this task?"* has no answer, which is
exactly the question anyone re-opening a closed defect asks first. Several of the entries in
`CLAUDE.md` § Conventions cite a task's commit as the evidence for a rule.

## They are recoverable, which is what makes this cheap

The rewritten commits keep their **messages**, and this family's messages carry the finding or task id:

```
git log --all --oneline --grep=SH-H048     # -> 4b586710, b6afe04a, 2726f2b3, 70302b0b
git log --oneline -1 -- <a file the change touched>
```

TASK-118's four were recovered that way in a couple of minutes and its field now carries the new SHAs.

## Acceptance

1. Each of the 11 files' `pr:` entries is re-mapped to the SHA that survives, by message or by a file
   the change touched. Where an entry genuinely cannot be resolved, say so **in the field** — a
   silently dropped reference is worse than a recorded gap.
2. The new form drops the `Repo@` prefix: one repo, so naming it is noise that will read as a second
   repository to a future reader.
3. ⚠ **Check the prose, not just the frontmatter.** Task bodies, `## Outcome` sections and
   `CLAUDE.md` § Recent Updates also quote SHAs inline (TASK-255 cites `531d816`, TASK-259 and others
   quote commits in their reasoning). The frontmatter is the easy half; a claim like *"`git show
   531d816` shows the parameter did not exist before that commit"* is load-bearing reasoning that now
   cannot be checked.
4. Record the mapping rule itself somewhere durable — probably beside the integration model in
   `CLAUDE.md`, which already explains why this family has one commit per fix. A reader who hits a
   dangling SHA should find the `--grep` recipe rather than re-deriving it.

## Out of scope

- Rewriting history again to restore the old hashes. Not possible, and not desirable.
- The consumer repos. They were deliberately left untouched by TASK-457, so their SHAs are intact.
