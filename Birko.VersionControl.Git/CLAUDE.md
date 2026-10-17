# Birko.VersionControl.Git

## Overview
Git operations on a local repository through the git executable (TASK-534): `GitService`, and the `git_status`,
`git_diff`, `git_commit` and `git_merge` agent tools on top of it. Backported from DraCode.KoboldLair's `GitService`
and git tools, with their defects fixed rather than copied:
- DraCode's runner read stdout to the end before stderr, with no timeout and no cancellation (the pipe deadlock
  Birko had fixed in `RunCommandTool`). Every command now runs on `Birko.Helpers.ProcessRunner`.
- Its merge check checked out the target and ran a trial `merge --no-commit` in the user's working tree.
  `CheckMergeAsync` uses `git merge-tree --write-tree`, which touches nothing. **This needs git 2.38 or later**; an
  older git fails with a message saying so.
- A commit subject containing `|` broke its log parsing; fields are now separated by U+001F.
- Its repository lock was per command, so a merge's checkout / merge / restore could interleave with another caller.
  The lock (per repository, process-wide) is held per operation.

## Project Location
`C:\Source\Birko\Framework\Birko.VersionControl.Git\`

## Components
- `GitService(GitServiceOptions?)`. Repository: `GetVersionAsync` (null without git), `IsRepositoryAsync`, `InitAsync`
  (on `DefaultBranch`, writes `AuthorName` / `AuthorEmail` as the repository's identity).
  - Branches: `GetCurrentBranchAsync`, `BranchExistsAsync`, `CreateBranchAsync` (no-op when it exists),
    `CheckoutAsync`, `DeleteBranchAsync`, `GetUnmergedBranchesAsync`.
  - Working tree: `GetStatusAsync` (parsed `--porcelain -z`), `StageAllAsync`, `StageAsync` (paths after `--`),
    `CommitAsync`, which returns `NoChanges` when nothing is staged. Also `GetHeadShaAsync` and `GetFilesInCommitAsync`.
  - Comparing: `GetBranchDiffAsync` (`target...source`), `GetBranchLogAsync`.
  - Merging: `CheckMergeAsync`; `MergeAsync` (`--no-ff`) returns to the original branch, changes nothing on a
    conflict, and refuses a dirty working tree. `AbortMergeAsync`.
  - Worktrees: `CreateWorktreeAsync` (default `.worktrees/{sanitized}`, reused), `RemoveWorktreeAsync`,
    `PruneWorktreesAsync`.
  - Names: `RequireRefName` follows `check-ref-format` and refuses a leading `-`, so a name never becomes an option.
    It takes a branch or commit name, not a revision expression (`HEAD~1` is refused). `SanitizeBranchName` makes a
    name component.
  - A failed command throws `GitCommandException` (arguments, exit code, stderr). The caller's cancellation kills git
    and propagates. Every command has `CommandTimeout` (2 minutes by default) and `GIT_TERMINAL_PROMPT=0`.
- `Tools/GitTools.cs`: `GitStatusTool` (`status` / `branches` / `check_merge` / `init`), `GitDiffTool` (`stat` /
  `diff`, capped at 20,000 characters / `log`), `GitCommitTool` (stages all by default), `GitMergeTool`
  (`delete_branch`).
  - Each takes the service and an optional `Func<string, string?> resolveProjectPath`, which maps `project_name` to
    a path. Without it, the agent's working directory is the repository.
  - Failures come back as `Error: ...` text; cancellation propagates.
  - DraCode's feature-branch naming (`CreateFeatureBranchName`) stayed in DraCode; it is its policy.

```csharp
var git = new GitService(new GitServiceOptions { AuthorName = "Agent", AuthorEmail = "agent@local" });
var check = await git.CheckMergeAsync(repo, "feature/x");             // working tree untouched
if (check.CanMerge) await git.MergeAsync(repo, "feature/x");

var tools = new Tool[] { new GitStatusTool(git, projects.PathOf), new GitCommitTool(git, projects.PathOf) };
```

## Dependencies
- Birko.Helpers (`ProcessRunner`)
- Birko.AI.Contracts (`Tool`, for the agent tools; zero-dependency)
- git 2.38+ on PATH at run time

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.VersionControl.Git.Tests`
(temporary repositories; need git on PATH).
