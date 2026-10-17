using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.AI.Tools;

namespace Birko.VersionControl.Git.Tools
{
    /// <summary>
    /// Shared plumbing of the git agent tools (TASK-534): the repository is the agent's working directory, or — when the
    /// tool was given a <c>resolveProjectPath</c> callback — the path that callback returns for the <c>project_name</c>
    /// input. A git failure becomes an <c>Error:</c> result the model can read; the caller's cancellation propagates.
    /// </summary>
    public abstract class GitToolBase : Tool
    {
        /// <summary>The most diff text a tool returns; the rest is cut and the cut is said.</summary>
        public const int MaxOutputChars = 20_000;

        protected GitToolBase(GitService git, Func<string, string?>? resolveProjectPath)
        {
            Git = git ?? throw new ArgumentNullException(nameof(git));
            ResolveProjectPath = resolveProjectPath;
        }

        protected GitService Git { get; }
        protected Func<string, string?>? ResolveProjectPath { get; }

        protected object ProjectProperty => new { type = "string", description = "Name of the project whose repository to use" };

        public sealed override async Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
        {
            string path;
            if (ResolveProjectPath != null)
            {
                var project = Text(input, "project_name");
                if (project == null) return "Error: project_name is required";
                var resolved = ResolveProjectPath(project);
                if (string.IsNullOrEmpty(resolved)) return $"Error: no project named '{project}'";
                path = resolved;
            }
            else
            {
                path = workingDirectory;
            }

            try
            {
                return await ExecuteGitAsync(path, input, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is GitCommandException or ArgumentException or InvalidOperationException)
            {
                return "Error: " + ex.Message;
            }
        }

        protected abstract Task<string> ExecuteGitAsync(string repositoryPath, Dictionary<string, object> input, CancellationToken ct);

        protected static string? Text(Dictionary<string, object> input, string name)
        {
            var value = input.TryGetValue(name, out var v) ? v?.ToString() : null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        protected static bool Flag(Dictionary<string, object> input, string name, bool fallback)
            => input.TryGetValue(name, out var v) && bool.TryParse(v?.ToString(), out var b) ? b : fallback;

        protected static string Cap(string text)
            => text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + $"\n… ({text.Length - MaxOutputChars} more characters cut)";
    }

    /// <summary><c>git_status</c>: the current branch and changes, unmerged branches, a merge check, or init.</summary>
    public sealed class GitStatusTool(GitService git, Func<string, string?>? resolveProjectPath = null) : GitToolBase(git, resolveProjectPath)
    {
        public override string Name => "git_status";
        public override string Description =>
            "Git status of a repository: 'status' (current branch and changed files), 'branches' (branches not merged into the target), 'check_merge' (whether a branch merges cleanly, without touching the working tree), 'init' (create the repository).";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                action = new { type = "string", @enum = new[] { "status", "branches", "check_merge", "init" } },
                project_name = ProjectProperty,
                branch_name = new { type = "string", description = "Branch to check (check_merge)" },
                target_branch = new { type = "string", description = "Target branch (default: the repository's default branch)" },
            },
            required = new[] { "action" },
        };

        protected override async Task<string> ExecuteGitAsync(string path, Dictionary<string, object> input, CancellationToken ct)
        {
            var action = Text(input, "action")?.ToLowerInvariant();
            if (action == "init")
            {
                await Git.InitAsync(path, ct).ConfigureAwait(false);
                return $"Repository ready at {path} (branch {Git.DefaultBranch}).";
            }
            if (!await Git.IsRepositoryAsync(path, ct).ConfigureAwait(false))
                return "Error: not a git repository (use action 'init' to create one)";

            switch (action)
            {
                case "status":
                {
                    var branch = await Git.GetCurrentBranchAsync(path, ct).ConfigureAwait(false) ?? "(detached HEAD)";
                    var entries = await Git.GetStatusAsync(path, ct).ConfigureAwait(false);
                    var sb = new StringBuilder($"Branch: {branch}\n");
                    sb.Append(entries.Count == 0 ? "Working tree clean." : string.Join('\n', entries.Select(e => $"{e.Index}{e.WorkTree} {e.Path}")));
                    return sb.ToString();
                }
                case "branches":
                {
                    var branches = await Git.GetUnmergedBranchesAsync(path, Text(input, "target_branch"), ct).ConfigureAwait(false);
                    return branches.Count == 0 ? "No unmerged branches." : "Unmerged branches:\n" + string.Join('\n', branches);
                }
                case "check_merge":
                {
                    var branch = Text(input, "branch_name");
                    if (branch == null) return "Error: branch_name is required for check_merge";
                    var check = await Git.CheckMergeAsync(path, branch, Text(input, "target_branch"), ct).ConfigureAwait(false);
                    return check.CanMerge
                        ? $"{branch} merges cleanly ({check.CommitsToMerge} commit(s){(check.CanFastForward ? ", fast-forward possible" : "")})."
                        : $"{branch} conflicts in:\n" + string.Join('\n', check.Conflicts);
                }
                default:
                    return "Error: action must be one of status, branches, check_merge, init";
            }
        }
    }

    /// <summary><c>git_diff</c>: what a branch changed since it left the target — summary, full diff, or commit log.</summary>
    public sealed class GitDiffTool(GitService git, Func<string, string?>? resolveProjectPath = null) : GitToolBase(git, resolveProjectPath)
    {
        public override string Name => "git_diff";
        public override string Description => "Changes on a branch since it left the target branch: 'stat' (summary), 'diff' (full diff, capped), 'log' (commits).";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                project_name = ProjectProperty,
                branch_name = new { type = "string" },
                target_branch = new { type = "string", description = "Target branch (default: the repository's default branch)" },
                mode = new { type = "string", @enum = new[] { "stat", "diff", "log" }, @default = "stat" },
            },
            required = new[] { "branch_name" },
        };

        protected override async Task<string> ExecuteGitAsync(string path, Dictionary<string, object> input, CancellationToken ct)
        {
            var branch = Text(input, "branch_name");
            if (branch == null) return "Error: branch_name is required";
            var target = Text(input, "target_branch");
            switch (Text(input, "mode")?.ToLowerInvariant() ?? "stat")
            {
                case "log":
                    var commits = await Git.GetBranchLogAsync(path, branch, target, ct).ConfigureAwait(false);
                    return commits.Count == 0 ? "No commits." : string.Join('\n', commits.Select(c => $"{c.Sha[..8]} {c.Date:yyyy-MM-dd} {c.Author}: {c.Subject}"));
                case "diff":
                    var full = await Git.GetBranchDiffAsync(path, branch, target, ct).ConfigureAwait(false);
                    return full.FilesChanged == 0 ? "No changes." : Cap(full.Diff);
                case "stat":
                    var stat = await Git.GetBranchDiffAsync(path, branch, target, ct).ConfigureAwait(false);
                    return stat.FilesChanged == 0 ? "No changes." : $"{stat.FilesChanged} file(s) changed\n{stat.Stat}";
                default:
                    return "Error: mode must be one of stat, diff, log";
            }
        }
    }

    /// <summary><c>git_commit</c>: stage (everything by default) and commit.</summary>
    public sealed class GitCommitTool(GitService git, Func<string, string?>? resolveProjectPath = null) : GitToolBase(git, resolveProjectPath)
    {
        public override string Name => "git_commit";
        public override string Description => "Stage the working tree's changes (all of them unless stage_all is false) and commit them with a message.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                project_name = ProjectProperty,
                message = new { type = "string" },
                stage_all = new { type = "boolean", @default = true },
            },
            required = new[] { "message" },
        };

        protected override async Task<string> ExecuteGitAsync(string path, Dictionary<string, object> input, CancellationToken ct)
        {
            var message = Text(input, "message");
            if (message == null) return "Error: message is required";
            if (Flag(input, "stage_all", true)) await Git.StageAllAsync(path, ct).ConfigureAwait(false);
            var outcome = await Git.CommitAsync(path, message, ct: ct).ConfigureAwait(false);
            if (outcome == CommitOutcome.NoChanges) return "Nothing to commit: no staged changes.";
            var sha = await Git.GetHeadShaAsync(path, ct).ConfigureAwait(false);
            return $"Committed {sha?[..8]}: {message.Split('\n')[0]}";
        }
    }

    /// <summary><c>git_merge</c>: merge a branch into the target with a merge commit, refusing on conflicts.</summary>
    public sealed class GitMergeTool(GitService git, Func<string, string?>? resolveProjectPath = null) : GitToolBase(git, resolveProjectPath)
    {
        public override string Name => "git_merge";
        public override string Description => "Merge a branch into the target branch with a merge commit. Nothing changes when it would conflict; the conflicting files are listed.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                project_name = ProjectProperty,
                branch_name = new { type = "string" },
                target_branch = new { type = "string", description = "Target branch (default: the repository's default branch)" },
                delete_branch = new { type = "boolean", description = "Delete the merged branch afterwards", @default = false },
            },
            required = new[] { "branch_name" },
        };

        protected override async Task<string> ExecuteGitAsync(string path, Dictionary<string, object> input, CancellationToken ct)
        {
            var branch = Text(input, "branch_name");
            if (branch == null) return "Error: branch_name is required";
            var result = await Git.MergeAsync(path, branch, Text(input, "target_branch"), ct: ct).ConfigureAwait(false);
            if (!result.Merged) return $"Not merged: {branch} conflicts in:\n" + string.Join('\n', result.Conflicts);
            if (Flag(input, "delete_branch", false)) await Git.DeleteBranchAsync(path, branch, ct: ct).ConfigureAwait(false);
            return $"Merged {branch} into {Text(input, "target_branch") ?? Git.DefaultBranch}: {result.MergeCommitSha?[..8]}";
        }
    }
}
