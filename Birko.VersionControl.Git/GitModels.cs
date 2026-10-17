using System;
using System.Collections.Generic;

namespace Birko.VersionControl.Git
{
    /// <summary>Settings of <see cref="GitService"/>.</summary>
    public sealed class GitServiceOptions
    {
        /// <summary>The git executable, resolved through PATH.</summary>
        public string GitExecutable { get; init; } = "git";

        /// <summary>How long one git command may run before it is killed.</summary>
        public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(2);

        /// <summary>The branch <c>InitAsync</c> creates and the default merge / diff target.</summary>
        public string DefaultBranch { get; init; } = "main";

        /// <summary>Written as the repository's <c>user.name</c> by <c>InitAsync</c>, and the commit author when both are set.</summary>
        public string? AuthorName { get; init; }

        /// <summary>Written as the repository's <c>user.email</c> by <c>InitAsync</c>, and the commit author when both are set.</summary>
        public string? AuthorEmail { get; init; }
    }

    /// <summary>A git command that exited with an error.</summary>
    public sealed class GitCommandException : Exception
    {
        public GitCommandException(IReadOnlyList<string> arguments, int exitCode, string standardError)
            : base($"git {string.Join(' ', arguments)} failed ({exitCode}): {standardError}")
        {
            Arguments = arguments;
            ExitCode = exitCode;
            StandardError = standardError;
        }

        public IReadOnlyList<string> Arguments { get; }
        public int ExitCode { get; }
        public string StandardError { get; }
    }

    /// <summary>The outcome of <see cref="GitService.CommitAsync"/>; a failure throws.</summary>
    public enum CommitOutcome
    {
        Committed,
        /// <summary>Nothing was staged, so no commit was made.</summary>
        NoChanges,
    }

    /// <summary>One line of <c>git status --porcelain</c>: the index and working-tree states and the path.</summary>
    public sealed record GitStatusEntry(string Path, char Index, char WorkTree)
    {
        public bool IsUntracked => Index == '?';
        public bool IsStaged => Index is not (' ' or '?' or '!');
    }

    public sealed record GitCommit(string Sha, string Subject, string Author, DateTimeOffset Date);

    public sealed record GitBranchDiff(string Diff, string Stat, int FilesChanged);

    /// <param name="CommitsToMerge">Commits on the source the target does not have.</param>
    /// <param name="Conflicts">The paths that would conflict; empty when <paramref name="CanMerge"/>.</param>
    public sealed record GitMergeCheck(bool CanMerge, bool CanFastForward, int CommitsToMerge, IReadOnlyList<string> Conflicts);

    public sealed record GitMergeResult(bool Merged, string? MergeCommitSha, IReadOnlyList<string> Conflicts);
}
