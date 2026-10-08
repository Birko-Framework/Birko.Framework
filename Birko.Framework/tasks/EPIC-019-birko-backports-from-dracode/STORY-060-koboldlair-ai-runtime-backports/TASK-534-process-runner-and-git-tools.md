---
id: TASK-534
parent: STORY-060
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# One process runner in Birko.Helpers; a git service and agent git tools on top of it

## Context

DraCode's `Services/GitService.cs` (765) + `Models/Git/GitBranch.cs` (141), and the tools `GitStatusTool`, `GitDiffTool`,
`GitCommitTool`, `GitMergeTool` (~800 lines) cover init, branch, checkout, stage, commit, merge with a pre-check, diff and
log between branches, and worktrees, using `ArgumentList` and a lock per repository. Birko has nothing for git.

⚠ `RunGitCommandAsync` reads stdout to the end and only then stderr — the pipe-buffer deadlock Birko already fixed in
`Birko.AI/Tools/RunCommandTool.cs` (lines 51–54). It has no timeout and no cancellation. Two process runners, one of
them fixed: rule 16.

Adopted in the consumer by DraCode TASK-132 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] One process runner in `Birko.Helpers`: stdout and stderr drained concurrently, timeout, cancellation kills the process tree; `RunCommandTool` moves onto it
- [ ] A git service on that runner, in a new project (e.g. `Birko.VersionControl.Git`, via new-birko-subproject)
- [ ] Agent tools for status, diff, commit and merge; the project → path lookup is a constructor callback
- [ ] Tests against a temporary repo, plus one that writes > 64 KB to stderr before stdout finishes (the deadlock)

## Out of scope

- Feature-branch naming (`CreateFeatureBranchName`) — DraCode policy

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
