using Birko.Helpers;
using Birko.VersionControl.Git;
using Birko.VersionControl.Git.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.VersionControl.Git.Tests;

/// <summary>
/// TASK-534 — <see cref="GitService"/> and the git agent tools against a temporary repository per test. Against
/// DraCode's service it replaces: the merge check no longer checks out and trial-merges in the working tree, a commit
/// subject containing '|' no longer breaks the log, a branch name cannot become an option, and a whole operation holds
/// the repository lock.
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("birko_git_").FullName;
    private readonly GitService _git = new(new GitServiceOptions { AuthorName = "Birko Test", AuthorEmail = "test@birko.local" });

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal); // git's object files are read-only on Windows
        try { Directory.Delete(_repo, recursive: true); } catch { /* not the assertion */ }
    }

    private async Task Commit(string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, file), content);
        await _git.StageAllAsync(_repo);
        (await _git.CommitAsync(_repo, message)).Should().Be(CommitOutcome.Committed);
    }

    private async Task<string> Raw(params string[] args)
        => (await ProcessRunner.RunAsync(new ProcessRunRequest("git", args) { WorkingDirectory = _repo })).StandardOutput.Trim();

    private async Task Initialized()
    {
        await _git.InitAsync(_repo);
        await Commit("README.md", "base\n", "initial");
    }

    // ── repository, commits, status ──

    [Fact]
    public async Task Init_creates_the_default_branch_and_the_configured_identity_and_is_idempotent()
    {
        (await _git.IsRepositoryAsync(_repo)).Should().BeFalse();
        await _git.InitAsync(_repo);
        await _git.InitAsync(_repo);

        (await _git.IsRepositoryAsync(_repo)).Should().BeTrue();
        (await _git.GetCurrentBranchAsync(_repo)).Should().Be("main");
        (await Raw("config", "user.email")).Should().Be("test@birko.local");
        (await _git.GetVersionAsync()).Should().StartWith("git version");
        (await _git.GetHeadShaAsync(_repo)).Should().BeNull("no commit yet");
    }

    [Fact]
    public async Task A_commit_with_nothing_staged_is_no_changes_not_an_error()
    {
        await Initialized();
        (await _git.CommitAsync(_repo, "nothing")).Should().Be(CommitOutcome.NoChanges);
    }

    [Fact]
    public async Task Status_lists_staged_unstaged_and_untracked_paths()
    {
        await Initialized();
        await File.WriteAllTextAsync(Path.Combine(_repo, "README.md"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "new file.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(_repo, "staged.txt"), "x");
        await _git.StageAsync(_repo, ["staged.txt"]);

        var status = await _git.GetStatusAsync(_repo);

        status.Should().Contain(e => e.Path == "README.md" && e.WorkTree == 'M' && !e.IsStaged);
        status.Should().Contain(e => e.Path == "new file.txt" && e.IsUntracked);
        status.Should().Contain(e => e.Path == "staged.txt" && e.IsStaged);
    }

    [Fact]
    public async Task The_files_of_a_commit_are_listed_including_the_root_commit()
    {
        await Initialized();
        var root = await _git.GetHeadShaAsync(_repo);
        (await _git.GetFilesInCommitAsync(_repo, root!)).Should().Equal("README.md");
    }

    // ── branches, diff, log ──

    [Fact]
    public async Task A_branch_log_keeps_a_subject_containing_the_old_separator()
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "feature/x");
        await _git.CheckoutAsync(_repo, "feature/x");
        await Commit("a.txt", "a", "fix: a | b | c");

        var log = await _git.GetBranchLogAsync(_repo, "feature/x");

        log.Should().ContainSingle();
        log[0].Subject.Should().Be("fix: a | b | c");
        log[0].Author.Should().Be("Birko Test");
        log[0].Sha.Should().HaveLength(40);
    }

    [Fact]
    public async Task A_branch_diff_reports_what_the_branch_changed()
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "feature/x");
        await _git.CheckoutAsync(_repo, "feature/x");
        await Commit("a.txt", "alpha\n", "add a");
        await Commit("b.txt", "beta\n", "add b");

        var diff = await _git.GetBranchDiffAsync(_repo, "feature/x");

        diff.FilesChanged.Should().Be(2);
        diff.Diff.Should().Contain("+alpha");
        diff.Stat.Should().Contain("a.txt");
        (await _git.GetUnmergedBranchesAsync(_repo)).Should().Equal("feature/x");
    }

    [Fact]
    public async Task Creating_an_existing_branch_is_a_no_op_and_delete_removes_it()
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "topic");
        await _git.CreateBranchAsync(_repo, "topic");
        (await _git.BranchExistsAsync(_repo, "topic")).Should().BeTrue();

        await _git.DeleteBranchAsync(_repo, "topic");
        (await _git.BranchExistsAsync(_repo, "topic")).Should().BeFalse();
    }

    // ── merge ──

    private async Task Diverge(bool conflicting)
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "feature/x");
        await _git.CheckoutAsync(_repo, "feature/x");
        await Commit("README.md", "feature\n", "feature edit");
        await _git.CheckoutAsync(_repo, "main");
        if (conflicting) await Commit("README.md", "main\n", "main edit");
        else await Commit("other.txt", "main\n", "main edit");
    }

    [Fact]
    public async Task The_merge_check_finds_conflicts_without_touching_the_working_tree()
    {
        await Diverge(conflicting: true);
        await File.WriteAllTextAsync(Path.Combine(_repo, "wip.txt"), "uncommitted work"); // must survive the check
        var head = await _git.GetHeadShaAsync(_repo);

        var check = await _git.CheckMergeAsync(_repo, "feature/x");

        check.CanMerge.Should().BeFalse();
        check.Conflicts.Should().Equal("README.md");
        (await _git.GetCurrentBranchAsync(_repo)).Should().Be("main");
        (await _git.GetHeadShaAsync(_repo)).Should().Be(head);
        (await File.ReadAllTextAsync(Path.Combine(_repo, "README.md"))).Should().Be("main\n");
        (await File.ReadAllTextAsync(Path.Combine(_repo, "wip.txt"))).Should().Be("uncommitted work");
        Directory.Exists(Path.Combine(_repo, ".git", "MERGE_HEAD")).Should().BeFalse();
        File.Exists(Path.Combine(_repo, ".git", "MERGE_HEAD")).Should().BeFalse();
    }

    [Fact]
    public async Task A_clean_merge_makes_a_merge_commit_and_returns_to_the_original_branch()
    {
        await Diverge(conflicting: false);
        await _git.CreateBranchAsync(_repo, "elsewhere");
        await _git.CheckoutAsync(_repo, "elsewhere");

        var check = await _git.CheckMergeAsync(_repo, "feature/x");
        check.CanMerge.Should().BeTrue();
        check.CanFastForward.Should().BeFalse();
        check.CommitsToMerge.Should().Be(1);

        var result = await _git.MergeAsync(_repo, "feature/x");

        result.Merged.Should().BeTrue();
        (await Raw("rev-parse", "main")).Should().Be(result.MergeCommitSha);
        (await Raw("rev-list", "--parents", "-n", "1", "main")).Split(' ').Should().HaveCount(3, "a merge commit has two parents");
        (await _git.GetCurrentBranchAsync(_repo)).Should().Be("elsewhere");
        (await _git.GetUnmergedBranchesAsync(_repo)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_conflicting_merge_changes_nothing_and_names_the_files()
    {
        await Diverge(conflicting: true);
        var head = await _git.GetHeadShaAsync(_repo);

        var result = await _git.MergeAsync(_repo, "feature/x");

        result.Merged.Should().BeFalse();
        result.Conflicts.Should().Equal("README.md");
        (await _git.GetHeadShaAsync(_repo)).Should().Be(head);
        (await _git.GetStatusAsync(_repo)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_merge_into_a_dirty_working_tree_is_refused()
    {
        await Diverge(conflicting: false);
        await File.WriteAllTextAsync(Path.Combine(_repo, "README.md"), "uncommitted\n");

        var act = () => _git.MergeAsync(_repo, "feature/x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*uncommitted*");
        (await File.ReadAllTextAsync(Path.Combine(_repo, "README.md"))).Should().Be("uncommitted\n");
    }

    // ── names, worktrees, concurrency ──

    [Theory]
    [InlineData("-f")]
    [InlineData("--force")]
    [InlineData("a..b")]
    [InlineData("has space")]
    [InlineData("ends.lock")]
    [InlineData("trailing/")]
    [InlineData("a~1")]
    [InlineData("")]
    public async Task A_name_that_is_not_a_ref_is_refused_before_git_runs(string name)
    {
        await Initialized();
        var act = () => _git.CheckoutAsync(_repo, name);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("feature/x-1")]
    [InlineData("main")]
    [InlineData("release/2026.10")]
    public void A_valid_ref_name_is_accepted(string name) => GitService.RequireRefName(name).Should().Be(name);

    [Fact]
    public void Branch_names_are_sanitized() => GitService.SanitizeBranchName("  Add OAuth: GitHub & Google!! ").Should().Be("add-oauth-github-google");

    [Fact]
    public async Task A_worktree_is_created_reused_and_removed()
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "feature/w");

        var path = await _git.CreateWorktreeAsync(_repo, "feature/w");
        (await _git.CreateWorktreeAsync(_repo, "feature/w")).Should().Be(path);

        File.Exists(Path.Combine(path, "README.md")).Should().BeTrue();
        (await _git.GetCurrentBranchAsync(path)).Should().Be("feature/w");

        await _git.RemoveWorktreeAsync(_repo, path);
        Directory.Exists(path).Should().BeFalse();
        await _git.PruneWorktreesAsync(_repo);
    }

    [Fact]
    public async Task Concurrent_operations_on_one_repository_do_not_collide_on_the_index_lock()
    {
        await Initialized();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(_repo, $"f{i}.txt"), i.ToString());
            await _git.StageAsync(_repo, [$"f{i}.txt"]);
            await _git.CommitAsync(_repo, $"add f{i}");
        })));

        (await _git.GetStatusAsync(_repo)).Should().BeEmpty("every file was committed by some commit");
        int.Parse(await Raw("rev-list", "--count", "HEAD")).Should().BeInRange(2, 9);
    }

    [Fact]
    public async Task A_failing_command_throws_with_gits_message()
    {
        await Initialized();
        var act = () => _git.CheckoutAsync(_repo, "no-such-branch");
        var thrown = await act.Should().ThrowAsync<GitCommandException>();
        thrown.Which.ExitCode.Should().NotBe(0);
        thrown.Which.StandardError.Should().NotBeEmpty();
    }

    // ── tools ──

    [Fact]
    public async Task The_tools_commit_report_status_check_and_merge_through_the_project_resolver()
    {
        await Diverge(conflicting: false);
        Func<string, string?> resolve = name => name == "demo" ? _repo : null;
        var status = new GitStatusTool(_git, resolve);
        var commit = new GitCommitTool(_git, resolve);
        var diff = new GitDiffTool(_git, resolve);
        var merge = new GitMergeTool(_git, resolve);

        await File.WriteAllTextAsync(Path.Combine(_repo, "tool.txt"), "t");
        (await status.ExecuteAsync("", new() { ["action"] = "status", ["project_name"] = "demo" })).Should().Contain("?? tool.txt");
        (await commit.ExecuteAsync("", new() { ["message"] = "via tool", ["project_name"] = "demo" })).Should().StartWith("Committed ");
        (await commit.ExecuteAsync("", new() { ["message"] = "again", ["project_name"] = "demo" })).Should().StartWith("Nothing to commit");
        (await status.ExecuteAsync("", new() { ["action"] = "check_merge", ["branch_name"] = "feature/x", ["project_name"] = "demo" }))
            .Should().Contain("merges cleanly");
        (await diff.ExecuteAsync("", new() { ["branch_name"] = "feature/x", ["mode"] = "log", ["project_name"] = "demo" }))
            .Should().Contain("feature edit");
        (await merge.ExecuteAsync("", new() { ["branch_name"] = "feature/x", ["delete_branch"] = true, ["project_name"] = "demo" }))
            .Should().StartWith("Merged feature/x into main");
        (await _git.BranchExistsAsync(_repo, "feature/x")).Should().BeFalse();
    }

    [Fact]
    public async Task A_tool_answers_errors_as_text_and_uses_the_working_directory_without_a_resolver()
    {
        await Diverge(conflicting: true);
        var withResolver = new GitStatusTool(_git, _ => null);
        (await withResolver.ExecuteAsync("", new() { ["action"] = "status", ["project_name"] = "nope" })).Should().Be("Error: no project named 'nope'");
        (await withResolver.ExecuteAsync("", new() { ["action"] = "status" })).Should().Be("Error: project_name is required");

        var merge = new GitMergeTool(_git);
        (await merge.ExecuteAsync(_repo, new() { ["branch_name"] = "feature/x" })).Should().StartWith("Not merged").And.Contain("README.md");
        (await merge.ExecuteAsync(_repo, new() { ["branch_name"] = "-f" })).Should().StartWith("Error:");
    }

    [Fact]
    public async Task A_long_diff_is_capped_and_says_so()
    {
        await Initialized();
        await _git.CreateBranchAsync(_repo, "big");
        await _git.CheckoutAsync(_repo, "big");
        await Commit("big.txt", string.Join('\n', Enumerable.Range(0, 5000).Select(i => $"line {i} " + new string('x', 20))), "big");

        var text = await new GitDiffTool(_git).ExecuteAsync(_repo, new() { ["branch_name"] = "big", ["mode"] = "diff" });

        text.Length.Should().BeLessThan(GitToolBase.MaxOutputChars + 100);
        text.Should().Contain("more characters cut");
    }
}
