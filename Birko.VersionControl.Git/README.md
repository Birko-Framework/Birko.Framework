# Birko.VersionControl.Git

Git operations on a local repository, and agent tools for them.

## Features
- `GitService`: init, branches, status, staging, commits, branch diff and log, worktrees
- A merge check that never touches the working tree (`git merge-tree`), and a merge that changes nothing on conflict
- Branch names validated so they can never become git options; every command has a timeout and honours cancellation
- Agent tools: `git_status`, `git_diff`, `git_commit`, `git_merge`

## Usage

```csharp
var git = new GitService(new GitServiceOptions { AuthorName = "Agent", AuthorEmail = "agent@local" });
await git.InitAsync(repo);
await git.StageAllAsync(repo);
await git.CommitAsync(repo, "Add feature");
var check = await git.CheckMergeAsync(repo, "feature/x");
```

Requires git 2.38 or later on PATH.

## Running tests

```
dotnet test tests/Birko.VersionControl.Git.Tests
```

## License
See the repository's root `LICENSE`.
