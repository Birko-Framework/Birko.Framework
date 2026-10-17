using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Helpers;

namespace Birko.VersionControl.Git
{
    /// <summary>
    /// Git operations on a local repository, through the git executable on <see cref="ProcessRunner"/> (TASK-534).
    /// Backported from DraCode's <c>GitService</c>; see the remarks for what changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One operation at a time per repository.</b> A per-repository lock (shared by every instance in the process) is held
    /// for a whole operation, so <see cref="MergeAsync"/>'s checkout, merge and restore are not interleaved with another
    /// caller's commands. DraCode locked per command.
    /// </para>
    /// <para>
    /// <b>The merge check does not touch the working tree.</b> <see cref="CheckMergeAsync"/> runs
    /// <c>git merge-tree --write-tree</c> (git 2.38+). DraCode checked out the target and ran a trial
    /// <c>merge --no-commit</c> in the user's working tree. <see cref="MergeAsync"/> refuses a dirty working tree.
    /// </para>
    /// <para>
    /// <b>Arguments.</b> Every argument goes through <see cref="ProcessStartInfo.ArgumentList"/>; branch and ref names are
    /// validated (<see cref="RequireRefName"/>), so a name cannot become an option (<c>-f</c>), and file paths follow
    /// <c>--</c>. A failed command throws <see cref="GitCommandException"/> with git's exit code and stderr, unless the
    /// method's result says otherwise.
    /// </para>
    /// </remarks>
    public class GitService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryLocks = new(StringComparer.OrdinalIgnoreCase);
        private const char FieldSeparator = '\u001f';

        private readonly GitServiceOptions _options;

        public GitService(GitServiceOptions? options = null)
        {
            _options = options ?? new GitServiceOptions();
        }

        /// <summary>The branch a method uses when no target is given.</summary>
        public string DefaultBranch => _options.DefaultBranch;

        // ── installation and repository ──

        /// <summary>The git version string, or <c>null</c> when git cannot be started.</summary>
        public async Task<string?> GetVersionAsync(CancellationToken ct = default)
        {
            try
            {
                var result = await RunAsync(Directory.GetCurrentDirectory(), ["--version"], ct).ConfigureAwait(false);
                return result.Succeeded ? result.StandardOutput.Trim() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
        {
            if (!Directory.Exists(path)) return false;
            var result = await RunAsync(path, ["rev-parse", "--git-dir"], ct).ConfigureAwait(false);
            return result.Succeeded;
        }

        /// <summary>Creates a repository at <paramref name="path"/> on <see cref="DefaultBranch"/>; nothing when one exists.</summary>
        public async Task InitAsync(string path, CancellationToken ct = default)
        {
            Directory.CreateDirectory(path);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            if (await IsRepositoryAsync(path, ct).ConfigureAwait(false)) return;
            await GitAsync(path, ct, "init", "-b", RequireRefName(_options.DefaultBranch)).ConfigureAwait(false);
            if (_options.AuthorName != null) await GitAsync(path, ct, "config", "user.name", _options.AuthorName).ConfigureAwait(false);
            if (_options.AuthorEmail != null) await GitAsync(path, ct, "config", "user.email", _options.AuthorEmail).ConfigureAwait(false);
        }

        // ── branches ──

        /// <summary>The checked-out branch, or <c>null</c> on a detached HEAD.</summary>
        public async Task<string?> GetCurrentBranchAsync(string path, CancellationToken ct = default)
        {
            var output = (await GitAsync(path, ct, "branch", "--show-current").ConfigureAwait(false)).Trim();
            return output.Length == 0 ? null : output;
        }

        public async Task<bool> BranchExistsAsync(string path, string branch, CancellationToken ct = default)
        {
            var result = await RunAsync(path, ["rev-parse", "--verify", "--quiet", "refs/heads/" + RequireRefName(branch)], ct).ConfigureAwait(false);
            return result.Succeeded;
        }

        /// <summary>Creates <paramref name="branch"/> at <paramref name="startPoint"/> (HEAD when null); nothing when it exists.</summary>
        public async Task CreateBranchAsync(string path, string branch, string? startPoint = null, CancellationToken ct = default)
        {
            RequireRefName(branch);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            if (await BranchExistsAsync(path, branch, ct).ConfigureAwait(false)) return;
            if (startPoint == null) await GitAsync(path, ct, "branch", branch).ConfigureAwait(false);
            else await GitAsync(path, ct, "branch", branch, RequireRefName(startPoint)).ConfigureAwait(false);
        }

        public async Task CheckoutAsync(string path, string branch, CancellationToken ct = default)
        {
            RequireRefName(branch);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, "checkout", branch, "--").ConfigureAwait(false);
        }

        public async Task DeleteBranchAsync(string path, string branch, bool force = false, CancellationToken ct = default)
        {
            RequireRefName(branch);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, "branch", force ? "-D" : "-d", branch).ConfigureAwait(false);
        }

        /// <summary>Local branches with commits <paramref name="target"/> (default <see cref="DefaultBranch"/>) does not have.</summary>
        public async Task<IReadOnlyList<string>> GetUnmergedBranchesAsync(string path, string? target = null, CancellationToken ct = default)
        {
            var into = RequireRefName(target ?? _options.DefaultBranch);
            var output = await GitAsync(path, ct, "branch", "--no-merged", into, "--format=%(refname:short)").ConfigureAwait(false);
            return Lines(output).Where(b => b != into).ToList();
        }

        // ── working tree and commits ──

        /// <summary>The working tree's changes (<c>git status --porcelain=v1 -z</c>), staged and unstaged, untracked included.</summary>
        public async Task<IReadOnlyList<GitStatusEntry>> GetStatusAsync(string path, CancellationToken ct = default)
        {
            var output = await GitAsync(path, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all").ConfigureAwait(false);
            var entries = new List<GitStatusEntry>();
            var records = output.Split('\0');
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record.Length < 4) continue;
                var entry = new GitStatusEntry(record[3..], record[0], record[1]);
                if (entry.Index is 'R' or 'C') i++; // a rename's source path follows as its own record
                entries.Add(entry);
            }
            return entries;
        }

        public async Task StageAllAsync(string path, CancellationToken ct = default)
        {
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, "add", "-A").ConfigureAwait(false);
        }

        public async Task StageAsync(string path, IEnumerable<string> files, CancellationToken ct = default)
        {
            var list = files.ToList();
            if (list.Count == 0) return;
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, ["add", "--", .. list]).ConfigureAwait(false);
        }

        /// <summary>
        /// Commits what is staged. <see cref="CommitOutcome.NoChanges"/> when nothing is staged — suspicious when the caller
        /// expected changes, and not an error.
        /// </summary>
        public async Task<CommitOutcome> CommitAsync(string path, string message, string? authorName = null, string? authorEmail = null,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            var staged = await RunAsync(path, ["diff", "--cached", "--quiet"], ct).ConfigureAwait(false);
            if (staged.ExitCode == 0) return CommitOutcome.NoChanges;

            var args = new List<string> { "commit", "-m", message };
            var name = authorName ?? _options.AuthorName;
            var email = authorEmail ?? _options.AuthorEmail;
            if (name != null && email != null) args.Add($"--author={name} <{email}>");
            await GitAsync(path, ct, args.ToArray()).ConfigureAwait(false);
            return CommitOutcome.Committed;
        }

        /// <summary>The full SHA of HEAD, or <c>null</c> in a repository with no commit.</summary>
        public async Task<string?> GetHeadShaAsync(string path, CancellationToken ct = default)
        {
            var result = await RunAsync(path, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }

        /// <summary>The paths a commit added, modified or deleted.</summary>
        public async Task<IReadOnlyList<string>> GetFilesInCommitAsync(string path, string commit, CancellationToken ct = default)
        {
            var output = await GitAsync(path, ct, "diff-tree", "--no-commit-id", "--name-only", "-r", "--root", RequireRefName(commit)).ConfigureAwait(false);
            return Lines(output);
        }

        // ── comparing branches ──

        /// <summary>The changes on <paramref name="source"/> since it left <paramref name="target"/> (<c>target...source</c>).</summary>
        public async Task<GitBranchDiff> GetBranchDiffAsync(string path, string source, string? target = null, CancellationToken ct = default)
        {
            var range = $"{RequireRefName(target ?? _options.DefaultBranch)}...{RequireRefName(source)}";
            var stat = await GitAsync(path, ct, "diff", "--stat", range).ConfigureAwait(false);
            var diff = await GitAsync(path, ct, "diff", range).ConfigureAwait(false);
            var numstat = await GitAsync(path, ct, "diff", "--numstat", range).ConfigureAwait(false);
            return new GitBranchDiff(diff, stat, Lines(numstat).Count);
        }

        /// <summary>The commits on <paramref name="source"/> that <paramref name="target"/> does not have, newest first.</summary>
        public async Task<IReadOnlyList<GitCommit>> GetBranchLogAsync(string path, string source, string? target = null, CancellationToken ct = default)
        {
            // Fields separated by U+001F: a subject may contain any printable character, '|' included (DraCode split on it).
            var output = await GitAsync(path, ct, "log", "--format=%H%x1f%s%x1f%an%x1f%aI",
                $"{RequireRefName(target ?? _options.DefaultBranch)}..{RequireRefName(source)}").ConfigureAwait(false);
            return Lines(output)
                .Select(line => line.Split(FieldSeparator))
                .Where(parts => parts.Length == 4)
                .Select(parts => new GitCommit(parts[0], parts[1], parts[2],
                    DateTimeOffset.Parse(parts[3], CultureInfo.InvariantCulture)))
                .ToList();
        }

        // ── merging ──

        /// <summary>
        /// Whether <paramref name="source"/> merges into <paramref name="target"/> without conflicts, computed by
        /// <c>git merge-tree --write-tree</c>: the working tree, the index and HEAD are not touched.
        /// </summary>
        public async Task<GitMergeCheck> CheckMergeAsync(string path, string source, string? target = null, CancellationToken ct = default)
        {
            var into = RequireRefName(target ?? _options.DefaultBranch);
            RequireRefName(source);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            return await CheckMergeCoreAsync(path, source, into, ct).ConfigureAwait(false);
        }

        private async Task<GitMergeCheck> CheckMergeCoreAsync(string path, string source, string into, CancellationToken ct)
        {
            var tree = await RunAsync(path, ["merge-tree", "--write-tree", "--name-only", "--no-messages", into, source], ct).ConfigureAwait(false);
            if (tree.ExitCode > 1 || tree.TimedOut)
                throw new GitCommandException(["merge-tree", "--write-tree", into, source], tree.ExitCode,
                    tree.StandardError + (tree.ExitCode == 129 ? " (git merge-tree --write-tree needs git 2.38 or later)" : ""));
            var conflicts = tree.ExitCode == 1 ? Lines(tree.StandardOutput).Skip(1).Distinct().ToList() : [];

            var ahead = await GitAsync(path, ct, "rev-list", "--count", $"{into}..{source}").ConfigureAwait(false);
            var fastForward = await RunAsync(path, ["merge-base", "--is-ancestor", into, source], ct).ConfigureAwait(false);
            return new GitMergeCheck(conflicts.Count == 0, fastForward.ExitCode == 0,
                int.Parse(ahead.Trim(), CultureInfo.InvariantCulture), conflicts);
        }

        /// <summary>
        /// Merges <paramref name="source"/> into <paramref name="target"/> with a merge commit (<c>--no-ff</c>), then checks
        /// out the branch that was current before. Nothing is changed when the merge would conflict.
        /// </summary>
        /// <exception cref="InvalidOperationException">The working tree has uncommitted changes.</exception>
        public async Task<GitMergeResult> MergeAsync(string path, string source, string? target = null, string? message = null,
            CancellationToken ct = default)
        {
            var into = RequireRefName(target ?? _options.DefaultBranch);
            RequireRefName(source);
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);

            var check = await CheckMergeCoreAsync(path, source, into, ct).ConfigureAwait(false);
            if (!check.CanMerge) return new GitMergeResult(false, null, check.Conflicts);

            var dirty = await GitAsync(path, ct, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
            if (dirty.Trim().Length > 0)
                throw new InvalidOperationException("The working tree has uncommitted changes; commit or stash them before merging.");

            var original = (await GitAsync(path, ct, "branch", "--show-current").ConfigureAwait(false)).Trim();
            try
            {
                if (original != into) await GitAsync(path, ct, "checkout", into, "--").ConfigureAwait(false);
                var merge = await RunAsync(path, ["merge", "--no-ff", "-m", message ?? $"Merge branch '{source}' into {into}", source], ct).ConfigureAwait(false);
                if (!merge.Succeeded)
                {
                    await RunAsync(path, ["merge", "--abort"], CancellationToken.None).ConfigureAwait(false);
                    throw new GitCommandException(["merge", "--no-ff", source], merge.ExitCode, merge.StandardError + merge.StandardOutput);
                }
                var sha = (await GitAsync(path, ct, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                return new GitMergeResult(true, sha, []);
            }
            finally
            {
                if (original.Length > 0 && original != into)
                    await RunAsync(path, ["checkout", original, "--"], CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async Task AbortMergeAsync(string path, CancellationToken ct = default)
        {
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, "merge", "--abort").ConfigureAwait(false);
        }

        // ── worktrees ──

        /// <summary>
        /// Checks out <paramref name="branch"/> in its own worktree (default <c>{repository}/.worktrees/{sanitized branch}</c>)
        /// and returns its path; an existing valid worktree there is reused.
        /// </summary>
        public async Task<string> CreateWorktreeAsync(string path, string branch, string? worktreePath = null, CancellationToken ct = default)
        {
            RequireRefName(branch);
            worktreePath = Path.GetFullPath(worktreePath ?? Path.Combine(path, ".worktrees", SanitizeBranchName(branch)));
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            if (Directory.Exists(worktreePath) && await IsRepositoryAsync(worktreePath, ct).ConfigureAwait(false))
                return worktreePath;
            Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
            await GitAsync(path, ct, "worktree", "add", "--", worktreePath, branch).ConfigureAwait(false);
            return worktreePath;
        }

        public async Task RemoveWorktreeAsync(string path, string worktreePath, CancellationToken ct = default)
        {
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            var result = await RunAsync(path, ["worktree", "remove", "--force", "--", Path.GetFullPath(worktreePath)], ct).ConfigureAwait(false);
            if (result.Succeeded) return;
            if (Directory.Exists(worktreePath)) Directory.Delete(worktreePath, recursive: true);
            await GitAsync(path, ct, "worktree", "prune").ConfigureAwait(false);
        }

        /// <summary>Forgets worktrees whose directory is gone (after a crash).</summary>
        public async Task PruneWorktreesAsync(string path, CancellationToken ct = default)
        {
            using var _ = await LockAsync(path, ct).ConfigureAwait(false);
            await GitAsync(path, ct, "worktree", "prune").ConfigureAwait(false);
        }

        // ── names ──

        private static readonly Regex InvalidRef = new(@"(^[-/.])|(\.\.)|(@\{)|([\x00-\x20\x7f~^:?*\[\\])|(//)|([/.]$)|(\.lock$)|(/\.)|(\.lock/)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns <paramref name="name"/> when it is a usable branch or commit name; throws otherwise. Follows
        /// <c>git check-ref-format</c>'s rules, and refuses a leading '-' so a name is never read as an option.
        /// </summary>
        public static string RequireRefName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "@" || InvalidRef.IsMatch(name))
                throw new ArgumentException($"'{name}' is not a valid git branch or ref name.", nameof(name));
            return name;
        }

        /// <summary>A name safe as a branch-name component: lowercase, [a-z0-9-], at most 50 characters.</summary>
        public static string SanitizeBranchName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            var sanitized = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (sanitized.Length > 50) sanitized = sanitized[..50].TrimEnd('-');
            return sanitized;
        }

        // ── running git ──

        private Task<ProcessRunResult> RunAsync(string path, IReadOnlyList<string> args, CancellationToken ct)
            => ProcessRunner.RunAsync(new ProcessRunRequest(_options.GitExecutable, args)
            {
                WorkingDirectory = path,
                Timeout = _options.CommandTimeout,
                // Never wait for a terminal prompt (credentials, editor): there is no terminal.
                Environment = new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_EDITOR"] = "true" },
            }, ct);

        private async Task<string> GitAsync(string path, CancellationToken ct, params string[] args)
        {
            var result = await RunAsync(path, args, ct).ConfigureAwait(false);
            if (result.TimedOut)
                throw new GitCommandException(args, -1, $"git timed out after {_options.CommandTimeout}.");
            if (result.ExitCode != 0)
                throw new GitCommandException(args, result.ExitCode, result.StandardError.Trim());
            return result.StandardOutput;
        }

        private static async Task<IDisposable> LockAsync(string path, CancellationToken ct)
        {
            var key = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var gate = RepositoryLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            return new Releaser(gate);
        }

        private static List<string> Lines(string output)
            => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private sealed class Releaser(SemaphoreSlim gate) : IDisposable
        {
            private int _released;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
            }
        }
    }
}
