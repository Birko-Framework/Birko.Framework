---
id: TASK-534
parent: STORY-060
feature: null
status: done
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

- [x] One process runner in `Birko.Helpers`: stdout and stderr drained concurrently, timeout, cancellation kills the process tree; `RunCommandTool` moves onto it
- [x] A git service on that runner, in a new project (e.g. `Birko.VersionControl.Git`, via new-birko-subproject)
- [x] Agent tools for status, diff, commit and merge; the project → path lookup is a constructor callback
- [x] Tests against a temporary repo, plus one that writes > 64 KB to stderr before stdout finishes (the deadlock)

## Out of scope

- Feature-branch naming (`CreateFeatureBranchName`) — DraCode policy

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-17)

**`Birko.Helpers.ProcessRunner`**, the one runner:
- `RunAsync(ProcessRunRequest)` returns `ProcessRunResult(ExitCode, StandardOutput, StandardError, TimedOut,
  Duration)`.
- Arguments go through `ArgumentList` (`RawArguments` for a caller holding one string).
- Both pipes are drained from the start.
- A timeout kills the tree and is reported; the caller's cancellation kills the tree and throws.
- `Environment` and `StandardInput` are supported.

The types live in the `Birko.Helpers` namespace: a `Birko.Helpers.Process` namespace would shadow
`System.Diagnostics.Process` for every file inside `Birko.Helpers.*`. `RunCommandTool` now runs on it. A caller's
cancellation used to come back as "Error: Process timed out"; it now propagates. The spec scenario is inverted
(rule 56).

**New `Birko.VersionControl.Git`** (via /new-birko-subproject):
- `GitService` covers init, branches, status (`--porcelain -z`), staging (paths after `--`), commit (`NoChanges`),
  head and commit files, branch diff and log, the merge check, merge, abort, and worktrees.
- `GitStatusTool`, `GitDiffTool` (capped at 20,000 characters), `GitCommitTool` and `GitMergeTool`, each with the
  optional `resolveProjectPath` constructor callback.

DraCode's defects, fixed rather than copied:
- **Pipe deadlock, no timeout, no cancellation**: fixed by the runner.
- **The merge check ran in the working tree.** It checked out the target and trial-merged, so it touched the user's
  files and failed on a dirty tree. It is now `git merge-tree --write-tree`, which needs git 2.38+ (local 2.47, CI
  ubuntu-latest); an older git gets an explicit message. `MergeAsync` refuses a dirty tree and changes nothing on a
  conflict.
- **Log parsing split on `|`**, which a commit subject can contain. Fields are now separated by U+001F.
- **Lock per command, not per operation.** Now held per operation, process-wide, per repository.
- **A branch named `-f` could become an option.** `RequireRefName` follows `check-ref-format` and refuses a leading
  `-`.

Out of scope as stated: `CreateFeatureBranchName` stays in DraCode.

**Tests:**
- `ProcessRunnerTests` (10): 256 KB to stderr before stdout, timeout, cancellation, exit code, one argument with
  spaces and quotes, stdin, environment, working directory, missing program, bad requests. Red: the stdout-first
  order (DraCode's), applied as a mutation, deadlocks the first test (55 s, failed).
- `GitServiceTests` (29, temporary repositories): the merge check leaves HEAD, the files, the uncommitted work and
  MERGE_HEAD untouched; a `--no-ff` merge has two parents and returns to the original branch; a conflict changes
  nothing; a dirty tree is refused; 8 refused names; 8 concurrent stage+commit; worktrees; the four tools with and
  without a resolver; the diff cap.
- Regression: every importer of `Birko.Helpers.projitems` / `Birko.AI.projitems` (see the commit).
